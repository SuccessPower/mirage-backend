using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;
using Mirage.Infrastructure.Identity;
using Mirage.Infrastructure.Persistence;

namespace Mirage.Api.Services;

// Sweeps for members whose birthday (DateOfBirth) or wedding anniversary (WeddingAnniversaryDate)
// falls on today's month/day and publishes a celebratory Testimonial + congratulatory comment,
// both authored by the seeded "Mirage Team" account (see SystemAccounts), plus sends the member a
// "Happy Birthday"/"Happy Anniversary" email. Same batched, per-item try/catch shape as
// DobValidationBackfillService. Driven by CelebrationPostWorker.
public sealed class CelebrationPostService(MirageDbContext db, IMemoryCache cache, IEmailService email,
    IConfiguration configuration, ILogger<CelebrationPostService> logger)
{
    private const string CommentBody = "Wishing you a wonderful day, from all of us at Mirage! 🎉";

    public async Task<int> RunBatchAsync(int batchSize, CancellationToken cancellationToken)
    {
        await DeleteExpiredCelebrationsAsync(cancellationToken);
        var mirageTeamUserId = await ResolveMirageTeamUserIdAsync(cancellationToken);
        if (mirageTeamUserId is null)
        {
            logger.LogWarning("Celebration post sweep skipped: the Mirage Team system account was not found.");
            return 0;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var created = 0;
        created += await RunForTypeAsync(CelebrationType.Birthday, today, mirageTeamUserId.Value, batchSize, cancellationToken);
        created += await RunForTypeAsync(CelebrationType.Anniversary, today, mirageTeamUserId.Value, batchSize, cancellationToken);
        return created;
    }

    private async Task<int> RunForTypeAsync(CelebrationType type, DateOnly today, Guid mirageTeamUserId,
        int batchSize, CancellationToken cancellationToken)
    {
        var candidateIds = type == CelebrationType.Birthday
            ? await db.Profiles.AsNoTracking()
                .Where(p => !p.CelebrationOptOut && p.DateOfBirth != default
                    && p.DateOfBirth.Month == today.Month && p.DateOfBirth.Day == today.Day)
                .Select(p => p.UserId).ToListAsync(cancellationToken)
            : await db.Profiles.AsNoTracking()
                .Where(p => !p.CelebrationOptOut && p.WeddingAnniversaryDate != null
                    && p.WeddingAnniversaryDate!.Value.Month == today.Month && p.WeddingAnniversaryDate!.Value.Day == today.Day)
                .Select(p => p.UserId).ToListAsync(cancellationToken);

        if (candidateIds.Count == 0) return 0;

        var alreadyCelebrated = await db.Testimonials.AsNoTracking()
            .Where(t => t.CelebrationType == type && candidateIds.Contains(t.TaggedUserId!.Value)
                && t.CreatedAt.Year == today.Year)
            .Select(t => t.TaggedUserId!.Value)
            .ToListAsync(cancellationToken);
        var pending = candidateIds.Except(alreadyCelebrated).Take(batchSize).ToList();
        if (pending.Count == 0) return 0;

        var created = 0;
        foreach (var userId in pending)
        {
            try
            {
                var recipient = await db.Profiles.AsNoTracking().Where(p => p.UserId == userId)
                    .Select(p => new
                    {
                        p.DisplayName,
                        Email = db.Users.Where(user => user.Id == p.UserId).Select(user => user.Email).FirstOrDefault()
                    }).SingleAsync(cancellationToken);
                var displayName = recipient.DisplayName;

                var (title, body) = type == CelebrationType.Birthday
                    ? ($"🎉 Happy Birthday, {displayName}!",
                        $"Join us in wishing {displayName} a very happy birthday! May the year ahead be full of joy, growth, and beautiful moments.")
                    : ($"💍 Happy Anniversary, {displayName}!",
                        $"Join us in celebrating {displayName}'s wedding anniversary today! Here's to many more years of love and partnership.");

                var post = new Testimonial(mirageTeamUserId, title, body, taggedUserId: userId, celebrationType: type);
                db.Testimonials.Add(post);
                await db.SaveChangesAsync(cancellationToken);

                db.TestimonialComments.Add(new TestimonialComment(post.Id, mirageTeamUserId, CommentBody));
                await db.SaveChangesAsync(cancellationToken);
                created++;

                if (!string.IsNullOrWhiteSpace(recipient.Email))
                {
                    var appUrl = (configuration["Frontend:BaseUrl"] ?? "https://www.themiragehub.com").TrimEnd('/');
                    if (!await email.SendCelebrationEmailAsync(recipient.Email, displayName, type,
                            $"{appUrl}/testimonials/{post.Id}", cancellationToken))
                        logger.LogWarning("Celebration post sweep: {CelebrationType} email send failed for UserId {UserId}.",
                            type, userId);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Celebration post sweep: failed to post {CelebrationType} celebration for UserId {UserId} — will retry next run.",
                    type, userId);
            }
        }

        logger.LogInformation("Celebration post sweep: published {Created}/{Pending} {CelebrationType} celebration(s).",
            created, pending.Count, type);
        return created;
    }

    private async Task DeleteExpiredCelebrationsAsync(CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
        var deleted = await db.Testimonials
            .Where(post => post.CelebrationType != null && post.CreatedAt <= cutoff)
            .ExecuteDeleteAsync(cancellationToken);
        if (deleted > 0)
            logger.LogInformation("Celebration post sweep: deleted {Deleted} post(s) older than 24 hours.", deleted);
    }

    private async Task<Guid?> ResolveMirageTeamUserIdAsync(CancellationToken cancellationToken)
    {
        return await cache.GetOrCreateAsync(SystemAccounts.MirageTeamUserIdCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24);
            var normalizedEmail = SystemAccounts.MirageTeamEmail.ToUpperInvariant();
            return await db.Users.AsNoTracking().Where(x => x.NormalizedEmail == normalizedEmail)
                .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(cancellationToken);
        });
    }
}
