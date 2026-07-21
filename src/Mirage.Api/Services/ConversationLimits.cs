using Microsoft.EntityFrameworkCore;
using Mirage.Api.Endpoints;
using Mirage.Application.Abstractions;
using Mirage.Domain.Enums;

namespace Mirage.Api.Services;

// Free-tier members can hold at most three open conversations at once; Plus and Premium are
// unlimited. An open conversation is an Active match chat, a chat request the user sent that is
// still pending (it becomes Active the moment the other party approves, with no cap check on the
// requester at that point — so the slot must be held from the request), or an active couple
// friendship the user participates in (as the befriending friend or as either partner of the
// couple). The user's own spouse chat is exempt — otherwise a married Free user could never
// befriend anyone.
//
// Married members who connect with each other through Friendship (the marital peer group) draw
// from a separate, more generous pool capped at MarriedFriendshipLimit — they're building a
// social circle of other married couples, which the ordinary 3-conversation cap would make
// impractical. A match counts as "married friendship" whenever both parties are currently married
// and are not each other's spouse (spouse chats are already exempt above); nothing else can put
// two married profiles on the same match, since Dating/Marriage never match married members and
// Friendship only ever opens a normal Like/Match between two married users.
internal static class ConversationLimits
{
    public const int FreeTierLimit = 3;
    public const int MarriedFriendshipLimit = 20;

    public const string LimitMessage =
        "You've reached the free plan's limit of 3 open conversations. " +
        "End a conversation from your inbox to free a slot — Premium with unlimited conversations is coming soon.";

    public const string MarriedFriendshipLimitMessage =
        "You've reached the limit of 20 open married-friendship conversations. " +
        "End one from your inbox to free a slot.";

    // Regular-pool-only check — used for couple friendships (befriending a whole couple isn't a
    // married-married peer match, so it never draws from the married-friendship pool).
    public static async Task<IResult?> CheckAsync(HttpContext context, Guid userId,
        IMirageDbContext db, CancellationToken cancellationToken)
    {
        var tier = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => x.SubscriptionTier)
            .SingleOrDefaultAsync(cancellationToken);
        if (tier is SubscriptionTier.Plus or SubscriptionTier.Premium) return null;

        var (regular, _) = await CountOpenAsync(userId, db, cancellationToken);
        return regular >= FreeTierLimit ? EndpointHelpers.Conflict(context, LimitMessage) : null;
    }

    // Match-aware check — used wherever a Match's chat slot is being opened/approved, so a
    // married-married Friendship pair is checked against the married-friendship pool instead.
    public static async Task<IResult?> CheckAsync(HttpContext context, Guid userId, Guid otherUserId,
        IMirageDbContext db, CancellationToken cancellationToken)
    {
        var tier = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => x.SubscriptionTier)
            .SingleOrDefaultAsync(cancellationToken);
        if (tier is SubscriptionTier.Plus or SubscriptionTier.Premium) return null;

        var (regular, marriedFriendship) = await CountOpenAsync(userId, db, cancellationToken);

        if (await IsMarriedFriendshipPairAsync(userId, otherUserId, db, cancellationToken))
            return marriedFriendship >= MarriedFriendshipLimit
                ? EndpointHelpers.Conflict(context, MarriedFriendshipLimitMessage)
                : null;

        return regular >= FreeTierLimit
            ? EndpointHelpers.Conflict(context, LimitMessage)
            : null;
    }

    // Returns the regular pool count (dating/marriage matches, ordinary friendship matches, and
    // couple friendships) and the married-friendship pool count separately, so each can be
    // checked against its own cap.
    public static async Task<(int Regular, int MarriedFriendship)> CountOpenAsync(Guid userId, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var spouseIds = db.Couples
            .Where(c => c.Status == CoupleStatus.Approved && (c.User1Id == userId || c.User2Id == userId))
            .Select(c => c.User1Id == userId ? c.User2Id : c.User1Id);

        var otherPartyIds = await db.Matches.AsNoTracking().Where(x =>
                (x.User1Id == userId || x.User2Id == userId)
                && (x.Status == MatchStatus.Active
                    || (x.Status == MatchStatus.PendingRequest && x.ChatRequestedByUserId == userId))
                && !spouseIds.Contains(x.User1Id == userId ? x.User2Id : x.User1Id))
            .Select(x => x.User1Id == userId ? x.User2Id : x.User1Id)
            .ToListAsync(cancellationToken);

        var activeFriendships = await db.CoupleFriendships.AsNoTracking().CountAsync(f =>
            f.Status == CoupleFriendshipStatus.Active
            && (f.FriendUserId == userId || db.Couples.Any(c => c.Id == f.CoupleId
                && (c.User1Id == userId || c.User2Id == userId))), cancellationToken);

        if (otherPartyIds.Count == 0) return (activeFriendships, 0);

        var marriedFriendshipCount = await db.Profiles.AsNoTracking()
            .CountAsync(p => otherPartyIds.Contains(p.UserId) && p.RelationshipStatus == RelationshipStatus.Married,
                cancellationToken);

        return (otherPartyIds.Count - marriedFriendshipCount + activeFriendships, marriedFriendshipCount);
    }

    private static async Task<bool> IsMarriedFriendshipPairAsync(Guid userId, Guid otherUserId,
        IMirageDbContext db, CancellationToken cancellationToken)
    {
        var statuses = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId || x.UserId == otherUserId)
            .Select(x => x.RelationshipStatus)
            .ToListAsync(cancellationToken);
        if (statuses.Count != 2 || statuses.Any(s => s != RelationshipStatus.Married)) return false;

        var user1Id = userId.CompareTo(otherUserId) < 0 ? userId : otherUserId;
        var user2Id = userId.CompareTo(otherUserId) < 0 ? otherUserId : userId;
        var isSpouse = await db.Couples.AsNoTracking().AnyAsync(
            c => c.Status == CoupleStatus.Approved && c.User1Id == user1Id && c.User2Id == user2Id,
            cancellationToken);
        return !isSpouse;
    }
}
