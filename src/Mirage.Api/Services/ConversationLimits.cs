using Microsoft.EntityFrameworkCore;
using Mirage.Api.Endpoints;
using Mirage.Application.Abstractions;
using Mirage.Domain.Enums;

namespace Mirage.Api.Services;

// Free-tier members can hold at most three open conversations at once; Plus and Premium are
// unlimited. An open conversation is an Active match chat or an active couple friendship the
// user participates in (as the befriending friend or as either partner of the couple). The
// user's own spouse chat is exempt — otherwise a married Free user could never befriend anyone.
internal static class ConversationLimits
{
    public const int FreeTierLimit = 3;

    public const string LimitMessage =
        "You've reached the free plan's limit of 3 open conversations. " +
        "End a conversation from your inbox or upgrade for unlimited conversations.";

    public static async Task<IResult?> CheckAsync(HttpContext context, Guid userId,
        IMirageDbContext db, CancellationToken cancellationToken)
    {
        var tier = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => x.SubscriptionTier)
            .SingleOrDefaultAsync(cancellationToken);
        if (tier is SubscriptionTier.Plus or SubscriptionTier.Premium) return null;
        return await CountOpenAsync(userId, db, cancellationToken) >= FreeTierLimit
            ? EndpointHelpers.Conflict(context, LimitMessage)
            : null;
    }

    public static async Task<int> CountOpenAsync(Guid userId, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var spouseIds = db.Couples
            .Where(c => c.Status == CoupleStatus.Approved && (c.User1Id == userId || c.User2Id == userId))
            .Select(c => c.User1Id == userId ? c.User2Id : c.User1Id);

        var activeMatches = await db.Matches.AsNoTracking().CountAsync(x =>
            (x.User1Id == userId || x.User2Id == userId)
            && x.Status == MatchStatus.Active
            && !spouseIds.Contains(x.User1Id == userId ? x.User2Id : x.User1Id), cancellationToken);

        var activeFriendships = await db.CoupleFriendships.AsNoTracking().CountAsync(f =>
            f.Status == CoupleFriendshipStatus.Active
            && (f.FriendUserId == userId || db.Couples.Any(c => c.Id == f.CoupleId
                && (c.User1Id == userId || c.User2Id == userId))), cancellationToken);

        return activeMatches + activeFriendships;
    }
}
