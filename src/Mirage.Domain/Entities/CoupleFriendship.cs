using Mirage.Domain.Common;
using Mirage.Domain.Enums;

namespace Mirage.Domain.Entities;

// A married member's friendship with another approved couple: one shared conversation thread
// visible to exactly three people — the friend plus both partners of the couple. Created
// immediately on "become friends" (no approval handshake). The unique (CoupleId, FriendUserId)
// index means re-befriending after ending reuses this row via Reactivate().
public sealed class CoupleFriendship : Entity
{
    private CoupleFriendship() { }

    public CoupleFriendship(Guid coupleId, Guid friendUserId)
    {
        CoupleId = coupleId;
        FriendUserId = friendUserId;
    }

    public Guid CoupleId { get; private set; }
    public Guid FriendUserId { get; private set; }
    public CoupleFriendshipStatus Status { get; private set; } = CoupleFriendshipStatus.Active;
    public DateTimeOffset? EndedAt { get; private set; }

    public void End()
    {
        if (Status == CoupleFriendshipStatus.Ended)
            throw new InvalidOperationException("Friendship is already ended.");
        Status = CoupleFriendshipStatus.Ended;
        EndedAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public void Reactivate()
    {
        if (Status == CoupleFriendshipStatus.Active)
            throw new InvalidOperationException("Friendship is already active.");
        Status = CoupleFriendshipStatus.Active;
        EndedAt = null;
        Touch();
    }
}

public sealed class CoupleFriendMessage : Entity
{
    private CoupleFriendMessage() { }

    public CoupleFriendMessage(Guid friendshipId, Guid senderId, string content, MessageType type = MessageType.Text,
        string? attachmentUrl = null)
    {
        if (type == MessageType.Image && string.IsNullOrWhiteSpace(attachmentUrl))
            throw new ArgumentException("Image messages require an attachment URL.");
        FriendshipId = friendshipId;
        SenderId = senderId;
        Content = content.Trim();
        Type = type;
        AttachmentUrl = attachmentUrl?.Trim();
    }

    public Guid FriendshipId { get; private set; }
    public Guid SenderId { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public MessageType Type { get; private set; } = MessageType.Text;
    public string? AttachmentUrl { get; private set; }
}
