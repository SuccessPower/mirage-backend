using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;
using Mirage.Infrastructure.Identity;
using Mirage.Infrastructure.Persistence;

namespace Mirage.Api.Services;

// Sweeps for members whose birthday (DateOfBirth) or wedding anniversary (WeddingAnniversaryDate)
// falls on *their local* today (TimeZoneId, UTC fallback) and publishes a permanent
// CelebrationEntry. An anniversary becomes one shared entry featuring both spouses of an approved
// Couple — unless a spouse opted out, in which case only the other is featured. Each featured
// member is emailed exactly once per celebration: EmailSentAt/PartnerEmailSentAt is stamped only
// after a successful send (send-then-stamp, same idiom as DobValidationBackfillService), so a
// failed send is retried every sweep until the member's local day rolls over. Driven by
// CelebrationPostWorker every 10 minutes so celebrations land close to local midnight.
public sealed class CelebrationPostService(MirageDbContext db, IEmailService email,
    IMemoryCache cache, IConfiguration configuration, ILogger<CelebrationPostService> logger)
{
    private const string TeamWishBody = "Wishing you a wonderful day, from all of us at Mirage! 🎉";

    private sealed record Candidate(Guid UserId, string DisplayName, DateOnly Date, string? TimeZoneId);

    public async Task<int> RunBatchAsync(int batchSize, CancellationToken cancellationToken)
    {
        var utcNow = DateTimeOffset.UtcNow;
        var created = 0;
        created += await CreateEntriesAsync(CelebrationType.Birthday, utcNow, batchSize, cancellationToken);
        created += await CreateEntriesAsync(CelebrationType.Anniversary, utcNow, batchSize, cancellationToken);
        await BackfillTeamWishesAsync(utcNow, cancellationToken);
        await SendPendingEmailsAsync(utcNow, cancellationToken);
        return created;
    }

    private async Task<int> CreateEntriesAsync(CelebrationType type, DateTimeOffset utcNow, int batchSize,
        CancellationToken cancellationToken)
    {
        // Whatever the timezone, a member's "today" is UTC today ± 1 day — match the month/day in
        // SQL against that three-day window, then confirm against each member's local date below.
        var utcToday = DateOnly.FromDateTime(utcNow.UtcDateTime);
        var (m0, d0) = (utcToday.AddDays(-1).Month, utcToday.AddDays(-1).Day);
        var (m1, d1) = (utcToday.Month, utcToday.Day);
        var (m2, d2) = (utcToday.AddDays(1).Month, utcToday.AddDays(1).Day);

        var candidates = type == CelebrationType.Birthday
            ? await db.Profiles.AsNoTracking()
                .Where(p => !p.CelebrationOptOut && p.DateOfBirth != default)
                .Where(p => (p.DateOfBirth.Month == m0 && p.DateOfBirth.Day == d0)
                    || (p.DateOfBirth.Month == m1 && p.DateOfBirth.Day == d1)
                    || (p.DateOfBirth.Month == m2 && p.DateOfBirth.Day == d2))
                .Select(p => new Candidate(p.UserId, p.DisplayName, p.DateOfBirth, p.TimeZoneId))
                .ToListAsync(cancellationToken)
            : await db.Profiles.AsNoTracking()
                .Where(p => !p.CelebrationOptOut && p.WeddingAnniversaryDate != null)
                .Where(p => (p.WeddingAnniversaryDate!.Value.Month == m0 && p.WeddingAnniversaryDate!.Value.Day == d0)
                    || (p.WeddingAnniversaryDate!.Value.Month == m1 && p.WeddingAnniversaryDate!.Value.Day == d1)
                    || (p.WeddingAnniversaryDate!.Value.Month == m2 && p.WeddingAnniversaryDate!.Value.Day == d2))
                .Select(p => new Candidate(p.UserId, p.DisplayName, p.WeddingAnniversaryDate!.Value, p.TimeZoneId))
                .ToListAsync(cancellationToken);

        var active = candidates
            .Select(c => new { Candidate = c, LocalToday = LocalToday(c.TimeZoneId, utcNow) })
            .Where(x => x.LocalToday.Month == x.Candidate.Date.Month && x.LocalToday.Day == x.Candidate.Date.Day)
            .ToList();
        if (active.Count == 0) return 0;

        var ids = active.Select(x => x.Candidate.UserId).ToList();
        var years = active.Select(x => x.LocalToday.Year).Distinct().ToList();
        var existing = await db.CelebrationEntries.AsNoTracking()
            .Where(e => e.Type == type && years.Contains(e.Year)
                && (ids.Contains(e.UserId) || (e.PartnerUserId != null && ids.Contains(e.PartnerUserId.Value))))
            .Select(e => new { e.UserId, e.PartnerUserId, e.Year })
            .ToListAsync(cancellationToken);
        var celebrated = new HashSet<(Guid UserId, int Year)>();
        foreach (var e in existing)
        {
            celebrated.Add((e.UserId, e.Year));
            if (e.PartnerUserId is not null) celebrated.Add((e.PartnerUserId.Value, e.Year));
        }

        var pending = active.Where(x => !celebrated.Contains((x.Candidate.UserId, x.LocalToday.Year)))
            .Take(batchSize).ToList();
        if (pending.Count == 0) return 0;

        var created = 0;
        foreach (var item in pending)
        {
            // A shared entry created earlier in this same loop may have covered this member.
            if (celebrated.Contains((item.Candidate.UserId, item.LocalToday.Year))) continue;
            try
            {
                var entry = type == CelebrationType.Birthday
                    ? BuildBirthdayEntry(item.Candidate, item.LocalToday.Year)
                    : await BuildAnniversaryEntryAsync(item.Candidate, item.LocalToday.Year, cancellationToken);
                db.CelebrationEntries.Add(entry);
                await db.SaveChangesAsync(cancellationToken);
                await SeedTeamWishAsync(entry.Id, cancellationToken);
                celebrated.Add((entry.UserId, entry.Year));
                if (entry.PartnerUserId is not null) celebrated.Add((entry.PartnerUserId.Value, entry.Year));
                created++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Celebration sweep: failed to create {CelebrationType} entry for UserId {UserId} — will retry next run.",
                    type, item.Candidate.UserId);
            }
        }

        logger.LogInformation("Celebration sweep: created {Created}/{Pending} {CelebrationType} entr(ies).",
            created, pending.Count, type);
        return created;
    }

    private static CelebrationEntry BuildBirthdayEntry(Candidate candidate, int year) =>
        new(candidate.UserId, null, CelebrationType.Birthday, year,
            $"🎉 Happy Birthday, {candidate.DisplayName}!",
            $"Join us in wishing {candidate.DisplayName} a very happy birthday! May the year ahead be full of joy, growth, and beautiful moments.");

    private async Task<CelebrationEntry> BuildAnniversaryEntryAsync(Candidate candidate, int year,
        CancellationToken cancellationToken)
    {
        var partnerUserId = await db.Couples.AsNoTracking()
            .Where(c => c.Status == CoupleStatus.Approved
                && (c.User1Id == candidate.UserId || c.User2Id == candidate.UserId))
            .Select(c => c.User1Id == candidate.UserId ? c.User2Id : c.User1Id)
            .FirstOrDefaultAsync(cancellationToken);

        Guid? featuredPartnerId = null;
        string? partnerName = null;
        if (partnerUserId != Guid.Empty)
        {
            var partner = await db.Profiles.AsNoTracking()
                .Where(p => p.UserId == partnerUserId)
                .Select(p => new { p.DisplayName, p.CelebrationOptOut })
                .SingleOrDefaultAsync(cancellationToken);
            if (partner is { CelebrationOptOut: false })
            {
                featuredPartnerId = partnerUserId;
                partnerName = partner.DisplayName;
            }
        }

        var (title, body) = featuredPartnerId is null
            ? ($"💍 Happy Anniversary, {candidate.DisplayName}!",
                $"Join us in celebrating {candidate.DisplayName}'s wedding anniversary today! Here's to many more years of love and partnership.")
            : ($"💍 Happy Anniversary, {candidate.DisplayName} & {partnerName}!",
                $"Join us in celebrating {candidate.DisplayName} and {partnerName}'s wedding anniversary today! Here's to many more years of love and partnership.");
        return new CelebrationEntry(candidate.UserId, featuredPartnerId, CelebrationType.Anniversary, year, title, body);
    }

    private async Task SendPendingEmailsAsync(DateTimeOffset utcNow, CancellationToken cancellationToken)
    {
        // Entries older than two days cannot still be "today" in any timezone — bound the scan.
        var cutoff = utcNow.AddDays(-2);
        var entries = await db.CelebrationEntries
            .Where(e => e.CreatedAt >= cutoff
                && (e.EmailSentAt == null || (e.PartnerUserId != null && e.PartnerEmailSentAt == null)))
            .ToListAsync(cancellationToken);
        if (entries.Count == 0) return;

        var recipientIds = entries
            .SelectMany(e => new[] { e.EmailSentAt is null ? e.UserId : (Guid?)null,
                e.PartnerEmailSentAt is null ? e.PartnerUserId : null })
            .OfType<Guid>().Distinct().ToList();
        var profiles = await db.Profiles.AsNoTracking()
            .Where(p => recipientIds.Contains(p.UserId))
            .Select(p => new { p.UserId, p.DisplayName, p.TimeZoneId, p.DateOfBirth, p.WeddingAnniversaryDate })
            .ToDictionaryAsync(p => p.UserId, cancellationToken);
        var addresses = await db.Users.AsNoTracking()
            .Where(u => recipientIds.Contains(u.Id) && u.Email != null)
            .ToDictionaryAsync(u => u.Id, u => u.Email!, cancellationToken);
        var appUrl = (configuration["Frontend:BaseUrl"] ?? "https://www.themiragehub.com").TrimEnd('/');

        foreach (var entry in entries)
        {
            var unsent = new List<Guid>(2);
            if (entry.EmailSentAt is null) unsent.Add(entry.UserId);
            if (entry.PartnerUserId is not null && entry.PartnerEmailSentAt is null) unsent.Add(entry.PartnerUserId.Value);

            foreach (var recipientId in unsent)
            {
                if (!profiles.TryGetValue(recipientId, out var profile)) continue;
                if (!addresses.TryGetValue(recipientId, out var address)) continue;

                // Only send while it is still the celebration day in the recipient's timezone —
                // once their local day rolls over, the retry window closes for good.
                var celebrationDate = entry.Type == CelebrationType.Birthday
                    ? (profile.DateOfBirth == default ? (DateOnly?)null : profile.DateOfBirth)
                    : profile.WeddingAnniversaryDate;
                if (celebrationDate is null) continue;
                var localToday = LocalToday(profile.TimeZoneId, utcNow);
                if (localToday.Month != celebrationDate.Value.Month || localToday.Day != celebrationDate.Value.Day) continue;

                try
                {
                    // The email's button links to the recipient's profile, where the permanent
                    // celebration entry (and the banner while it's live) is shown.
                    var celebrationUrl = $"{appUrl}/profiles/{recipientId}";
                    if (await email.SendCelebrationEmailAsync(address, profile.DisplayName, entry.Type,
                            celebrationUrl, cancellationToken))
                    {
                        entry.MarkEmailSent(recipientId);
                        await db.SaveChangesAsync(cancellationToken);
                    }
                    else
                    {
                        logger.LogWarning("Celebration sweep: {CelebrationType} email send failed for UserId {UserId} — will retry next run.",
                            entry.Type, recipientId);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Celebration sweep: {CelebrationType} email errored for UserId {UserId} — will retry next run.",
                        entry.Type, recipientId);
                }
            }
        }
    }

    // Covers entries published before wishes existed (or where seeding failed): any recent
    // entry with no wishes at all gets the Mirage Team's wish on the next sweep.
    private async Task BackfillTeamWishesAsync(DateTimeOffset utcNow, CancellationToken cancellationToken)
    {
        var cutoff = utcNow.AddDays(-2);
        var missing = await db.CelebrationEntries.AsNoTracking()
            .Where(e => e.CreatedAt >= cutoff && !db.CelebrationWishes.Any(w => w.CelebrationEntryId == e.Id))
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);
        foreach (var id in missing) await SeedTeamWishAsync(id, cancellationToken);
    }

    // The Mirage Team account leaves the first wish on every celebration, so the post never
    // looks empty and members see where to add their own. A missing system account only costs
    // the seeded wish — the celebration itself is already saved.
    private async Task SeedTeamWishAsync(Guid celebrationEntryId, CancellationToken cancellationToken)
    {
        try
        {
            var teamUserId = await cache.GetOrCreateAsync(SystemAccounts.MirageTeamUserIdCacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24);
                var normalizedEmail = SystemAccounts.MirageTeamEmail.ToUpperInvariant();
                return await db.Users.AsNoTracking().Where(x => x.NormalizedEmail == normalizedEmail)
                    .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(cancellationToken);
            });
            if (teamUserId is null)
            {
                logger.LogWarning("Celebration sweep: Mirage Team system account not found — skipping seeded wish.");
                return;
            }
            db.CelebrationWishes.Add(new CelebrationWish(celebrationEntryId, teamUserId.Value, TeamWishBody));
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Celebration sweep: failed to seed the Mirage Team wish for entry {EntryId}.",
                celebrationEntryId);
        }
    }

    private static DateOnly LocalToday(string? timeZoneId, DateTimeOffset utcNow)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId)) return DateOnly.FromDateTime(utcNow.UtcDateTime);
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, zone).DateTime);
        }
        catch (TimeZoneNotFoundException) { return DateOnly.FromDateTime(utcNow.UtcDateTime); }
        catch (InvalidTimeZoneException) { return DateOnly.FromDateTime(utcNow.UtcDateTime); }
    }
}
