using Mirage.Domain.Common;
using Mirage.Domain.Enums;

namespace Mirage.Domain.Entities;

public sealed class Message : Entity
{
    private Message() { }

    public Message(Guid matchId, Guid senderId, string content, MessageType type = MessageType.Text,
        string? attachmentUrl = null, Guid? replyToMessageId = null)
    {
        if (type == MessageType.Image && string.IsNullOrWhiteSpace(attachmentUrl))
            throw new ArgumentException("Image messages require an attachment URL.");
        MatchId = matchId;
        SenderId = senderId;
        Content = content.Trim();
        Type = type;
        AttachmentUrl = attachmentUrl?.Trim();
        ReplyToMessageId = replyToMessageId;
    }

    public Guid MatchId { get; private set; }
    public Guid SenderId { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public MessageType Type { get; private set; } = MessageType.Text;
    public string? AttachmentUrl { get; private set; }
    public Guid? ReplyToMessageId { get; private set; }
    public bool IsRead { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }
    public Match Match { get; private set; } = null!;
    public Message? ReplyToMessage { get; private set; }

    public void MarkRead()
    {
        if (IsRead) return;
        IsRead = true;
        ReadAt = DateTimeOffset.UtcNow;
        Touch();
    }
}
