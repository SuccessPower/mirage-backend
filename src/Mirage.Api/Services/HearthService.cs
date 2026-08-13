using Microsoft.EntityFrameworkCore;
using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;

namespace Mirage.Api.Services;

// Hearth is the platform-wide feed for married users. It is backed by one auto-managed Community
// (Community.HearthCategory) that every married user is joined to on first access, so posts,
// comments, mentions and moderation all reuse the community stack rather than a parallel one.
//
// Membership keys off UserProfile.RelationshipStatus == Married, deliberately NOT off an approved
// Couple: plenty of married members join without their spouse ever signing up, and they get the
// same feed. Where a spouse *is* on the platform and the couple is approved, both names are shown
// together as one author (see ResolveIdentityAsync).
internal static class HearthService
{
    public const string HearthName = "Hearth";
    private const string HearthDescription = "Married life, shared with couples walking the same road.";

    public static async Task<bool> IsMarriedAsync(IMirageDbContext db, Guid userId,
        CancellationToken cancellationToken) =>
        await db.Profiles.AsNoTracking()
            .AnyAsync(x => x.UserId == userId && x.RelationshipStatus == RelationshipStatus.Married,
                cancellationToken);

    // Returns the Hearth community id, joining the user if they are married, or null if they are
    // not — callers turn null into a 403. Safe to call on every request; it is a single indexed
    // lookup once the membership row exists.
    public static async Task<Guid?> EnsureMembershipAsync(IMirageDbContext db, Guid userId,
        CancellationToken cancellationToken)
    {
        if (!await IsMarriedAsync(db, userId, cancellationToken)) return null;

        var hearthId = await GetOrCreateAsync(db, userId, cancellationToken);

        var membership = await db.CommunityMembers
            .SingleOrDefaultAsync(x => x.CommunityId == hearthId && x.UserId == userId, cancellationToken);

        if (membership is null)
        {
            db.CommunityMembers.Add(new CommunityMember(hearthId, userId));
            await db.SaveChangesAsync(cancellationToken);
        }
        else if (membership.LeftAt is not null || membership.Status != CommunityMemberStatus.Approved)
        {
            // Someone who left Hearth and later comes back — or whose status was reset when their
            // relationship status changed — is simply re-approved. Hearth has no join queue.
            membership.Rejoin(CommunityMemberStatus.Approved);
            await db.SaveChangesAsync(cancellationToken);
        }

        return hearthId;
    }

    private static async Task<Guid> GetOrCreateAsync(IMirageDbContext db, Guid createdByUserId,
        CancellationToken cancellationToken)
    {
        var existing = await db.Communities.AsNoTracking()
            .Where(x => x.Category == Community.HearthCategory && x.OrganisationId == null)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is { } id) return id;

        var hearth = new Community(createdByUserId, HearthName, Community.HearthCategory, HearthDescription);
        db.Communities.Add(hearth);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return hearth.Id;
        }
        catch (DbUpdateException)
        {
            // Another first visitor won the race — the singleton index (see AddHearthFeed) rejected
            // this insert. Drop our copy and read theirs.
            db.Communities.Remove(hearth);
            return await db.Communities.AsNoTracking()
                .Where(x => x.Category == Community.HearthCategory && x.OrganisationId == null)
                .Select(x => x.Id)
                .FirstAsync(cancellationToken);
        }
    }

    // Every community the user's Hearth feed draws from: Hearth itself plus any church "Married"
    // community they belong to, so a post shared to their church circle lands in the same feed.
    public static async Task<List<Guid>> FeedCommunityIdsAsync(IMirageDbContext db, Guid userId,
        Guid hearthId, CancellationToken cancellationToken)
    {
        var marriedCircleIds = await db.CommunityMembers.AsNoTracking()
            .Where(m => m.UserId == userId && m.LeftAt == null && m.Status == CommunityMemberStatus.Approved)
            .Join(db.Communities.AsNoTracking().Where(c => c.Category == Community.ChurchMarriedCategory),
                m => m.CommunityId, c => c.Id, (m, c) => c.Id)
            .ToListAsync(cancellationToken);

        marriedCircleIds.Add(hearthId);
        return marriedCircleIds.Distinct().ToList();
    }

    // How a user is presented as an author on Hearth. With an approved Couple whose spouse is also
    // on the platform this is "Tobi & Ada" with both avatars; on their own it is just their own
    // name — the feed never implies a spouse who isn't there.
    public static async Task<Dictionary<Guid, HearthIdentity>> ResolveIdentitiesAsync(IMirageDbContext db,
        IEnumerable<Guid> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.Distinct().ToArray();
        var result = new Dictionary<Guid, HearthIdentity>();
        if (ids.Length == 0) return result;

        var profiles = await db.Profiles.AsNoTracking()
            .Where(p => ids.Contains(p.UserId))
            .Select(p => new { p.UserId, p.DisplayName, p.AvatarUrl, p.City, p.WeddingAnniversaryDate })
            .ToListAsync(cancellationToken);

        var couples = await db.Couples.AsNoTracking()
            .Where(c => c.Status == CoupleStatus.Approved &&
                        (ids.Contains(c.User1Id) || ids.Contains(c.User2Id)))
            .Select(c => new { c.User1Id, c.User2Id })
            .ToListAsync(cancellationToken);

        var spouseOf = new Dictionary<Guid, Guid>();
        foreach (var couple in couples)
        {
            spouseOf[couple.User1Id] = couple.User2Id;
            spouseOf[couple.User2Id] = couple.User1Id;
        }

        var spouseIds = spouseOf.Values.Where(x => !ids.Contains(x)).Distinct().ToArray();
        var spouseProfiles = spouseIds.Length == 0
            ? []
            : await db.Profiles.AsNoTracking()
                .Where(p => spouseIds.Contains(p.UserId))
                .Select(p => new { p.UserId, p.DisplayName, p.AvatarUrl })
                .ToListAsync(cancellationToken);

        var allNames = profiles.Select(p => new { p.UserId, p.DisplayName, p.AvatarUrl })
            .Concat(spouseProfiles)
            .DistinctBy(x => x.UserId)
            .ToDictionary(x => x.UserId);

        foreach (var profile in profiles)
        {
            string? spouseName = null;
            string? spouseAvatarUrl = null;
            Guid? spouseUserId = null;

            if (spouseOf.TryGetValue(profile.UserId, out var partnerId) &&
                allNames.TryGetValue(partnerId, out var partner))
            {
                spouseUserId = partnerId;
                spouseName = partner.DisplayName;
                spouseAvatarUrl = partner.AvatarUrl;
            }

            result[profile.UserId] = new HearthIdentity(
                profile.UserId,
                profile.DisplayName,
                profile.AvatarUrl,
                spouseUserId,
                spouseName,
                spouseAvatarUrl,
                DisplayNameFor(profile.DisplayName, spouseName),
                profile.City,
                profile.WeddingAnniversaryDate,
                YearsMarried(profile.WeddingAnniversaryDate));
        }

        return result;
    }

    public static async Task<HearthIdentity?> ResolveIdentityAsync(IMirageDbContext db, Guid userId,
        CancellationToken cancellationToken)
    {
        var identities = await ResolveIdentitiesAsync(db, [userId], cancellationToken);
        return identities.GetValueOrDefault(userId);
    }

    // "Tobi & Ada" when both are here, "Tobi" when the spouse isn't on the platform. Only the first
    // name is used for the pair so the byline stays short — "Tobi Onawale & Ada Onawale" reads badly.
    private static string DisplayNameFor(string name, string? spouseName) =>
        string.IsNullOrWhiteSpace(spouseName) ? name : $"{FirstName(name)} & {FirstName(spouseName)}";

    private static string FirstName(string name)
    {
        var trimmed = name.Trim();
        var space = trimmed.IndexOf(' ');
        return space > 0 ? trimmed[..space] : trimmed;
    }

    public static int? YearsMarried(DateOnly? anniversary)
    {
        if (anniversary is not { } date) return null;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (date > today) return null;
        var years = today.Year - date.Year;
        if (date.AddYears(years) > today) years--;
        return years;
    }

    // Days until the next anniversary, so the feed can count down to it. Null when the couple
    // hasn't told us their date.
    public static int? DaysToAnniversary(DateOnly? anniversary)
    {
        if (anniversary is not { } date) return null;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var next = new DateOnly(today.Year, date.Month, Math.Min(date.Day,
            DateTime.DaysInMonth(today.Year, date.Month)));
        if (next < today)
            next = new DateOnly(today.Year + 1, date.Month, Math.Min(date.Day,
                DateTime.DaysInMonth(today.Year + 1, date.Month)));
        return next.DayNumber - today.DayNumber;
    }
}

internal sealed record HearthIdentity(
    Guid UserId,
    string DisplayName,
    string? AvatarUrl,
    Guid? SpouseUserId,
    string? SpouseName,
    string? SpouseAvatarUrl,
    string CoupleName,
    string City,
    DateOnly? WeddingAnniversaryDate,
    int? YearsMarried);
