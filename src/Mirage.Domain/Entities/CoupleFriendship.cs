using Mirage.Domain.Common;
using Mirage.Domain.Enums;

namespace Mirage.Domain.Entities;

// The friendship between two approved couples: one shared conversation thread visible to all
// four spouses. Created immediately on "become friends" (no approval handshake). The pair is
// stored in a normalized order (Couple1Id < Couple2Id) so it doesn't matter which couple — or
// which spouse within a couple — initiates; either side always resolves to the same row. The
// unique (Couple1Id, Couple2Id) index means re-befriending after ending reuses this row via
// Reactivate().
public sealed class CoupleFriendship : Entity
{
    private CoupleFriendship() { }

    public CoupleFriendship(Guid couple1Id, Guid couple2Id)
    {
        if (couple1Id == couple2Id) throw new ArgumentException("A couple cannot befriend itself.");
        Couple1Id = couple1Id.CompareTo(couple2Id) < 0 ? couple1Id : couple2Id;
        Couple2Id = couple1Id.CompareTo(couple2Id) < 0 ? couple2Id : couple1Id;
    }

    public Guid Couple1Id { get; private set; }
    public Guid Couple2Id { get; private set; }
    public CoupleFriendshipStatus Status { get; private set; } = CoupleFriendshipStatus.Active;
    public DateTimeOffset? EndedAt { get; private set; }
    // Bumped whenever a message is sent (via ExecuteUpdateAsync in the message endpoints/hub,
    // not through a domain method) so the inbox can sort threads by recency.
    public DateTimeOffset? LastActivityAt { get; private set; }

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
