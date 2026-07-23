using Microsoft.EntityFrameworkCore;
using Mirage.Application.Abstractions;
using Mirage.Domain.Enums;

namespace Mirage.Api.Services;

// Conversations are uncapped for every tier. This only reports the open-conversation counts
// (regular pool vs. the married-friendship pool) for the informational "in progress" indicator
// on the client — nothing here blocks opening a new conversation.
internal static class ConversationLimits
{
    // Returns the regular pool count (dating/marriage matches, ordinary friendship matches, and
    // couple friendships) and the married-friendship pool count separately.
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
}
