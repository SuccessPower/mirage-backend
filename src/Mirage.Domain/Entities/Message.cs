using Mirage.Domain.Common;

namespace Mirage.Domain.Entities;

public sealed class Message : Entity
{
    private Message() { }

    public Message(Guid matchId, Guid senderId, string content)
    {
        MatchId = matchId;
        SenderId = senderId;
        Content = content.Trim();
    }

    public Guid MatchId { get; private set; }
    public Guid SenderId { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public bool IsRead { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }
    public Match Match { get; private set; } = null!;

    public void MarkRead()
    {
        if (IsRead) return;
        IsRead = true;
        ReadAt = DateTimeOffset.UtcNow;
        Touch();
    }
}
