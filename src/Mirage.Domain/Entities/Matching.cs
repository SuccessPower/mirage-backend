using Mirage.Domain.Common;
using Mirage.Domain.Enums;

namespace Mirage.Domain.Entities;

public sealed class UserLike : Entity
{
    private UserLike() { }
    public UserLike(Guid sourceUserId, Guid targetUserId, LikeType type)
    {
        SourceUserId = sourceUserId;
        TargetUserId = targetUserId;
        Type = type;
    }
    public Guid SourceUserId { get; private set; }
    public Guid TargetUserId { get; private set; }
    public LikeType Type { get; private set; }
}

public sealed class Match : Entity
{
    private Match() { }
    public Match(Guid user1Id, Guid user2Id)
    {
        if (user1Id.CompareTo(user2Id) < 0) { User1Id = user1Id; User2Id = user2Id; }
        else { User1Id = user2Id; User2Id = user1Id; }
        MatchedAt = DateTimeOffset.UtcNow;
    }
    public Guid User1Id { get; private set; }
    public Guid User2Id { get; private set; }
    // A match starts gated: chat only opens once one party requests it and the
    // other approves — this is the pre-chat "request/approve or cancel" handshake.
    public MatchStatus Status { get; private set; } = MatchStatus.PendingRequest;
    public Guid? ChatRequestedByUserId { get; private set; }
    public DateTimeOffset MatchedAt { get; private set; }
    public DateTimeOffset? LastActivityAt { get; private set; }

    public void RequestChat(Guid userId)
    {
        if (Status != MatchStatus.PendingRequest)
            throw new InvalidOperationException("A chat request can only be sent while the match is pending.");
        if (ChatRequestedByUserId == userId) return; // idempotent resend
        ChatRequestedByUserId = userId;
        Touch();
    }

    public void ApproveChat(Guid userId)
    {
        if (Status != MatchStatus.PendingRequest || ChatRequestedByUserId is null)
            throw new InvalidOperationException("There is no pending chat request to approve.");
        if (ChatRequestedByUserId == userId)
            throw new InvalidOperationException("You cannot approve your own chat request.");
        Status = MatchStatus.Active;
        LastActivityAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public void OpenForCouple()
    {
        if (Status == MatchStatus.Blocked)
            throw new InvalidOperationException("A blocked match cannot be opened for couple chat.");
        Status = MatchStatus.Active;
        ChatRequestedByUserId = null;
        LastActivityAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public void Close()
    {
        Status = MatchStatus.Closed;
        LastActivityAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public void Block()
    {
        Status = MatchStatus.Blocked;
        LastActivityAt = DateTimeOffset.UtcNow;
        Touch();
    }
}
