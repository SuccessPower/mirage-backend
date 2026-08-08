using Mirage.Domain.Common;
using Mirage.Domain.Enums;

namespace Mirage.Domain.Entities;

public sealed class Message : Entity
{
    private Message() { }

    public Message(Guid matchId, Guid senderId, string content, MessageType type = MessageType.Text,
        string? attachmentUrl = null, Guid? replyToMessageId = null, bool encryptedPayload = false)
    {
        if (!encryptedPayload && type == MessageType.Image && string.IsNullOrWhiteSpace(attachmentUrl))
            throw new ArgumentException("Image messages require an attachment URL.");
        MatchId = matchId;
        SenderId = senderId;
        Content = (content ?? string.Empty).Trim();
        Type = type;
        AttachmentUrl = attachmentUrl?.Trim();
        ReplyToMessageId = replyToMessageId;
    }

    public Guid MatchId { get; private set; }
    public Guid SenderId { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public MessageType Type { get; private set; } = MessageType.Text;
    public string? AttachmentUrl { get; private set; }
    public string? Ciphertext { get; private set; }
    public string? EncryptionNonce { get; private set; }
    public string? ClientMessageId { get; private set; }
    public int EncryptionVersion { get; private set; }
    public Guid? ReplyToMessageId { get; private set; }
    public bool IsRead { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }
    public Match Match { get; private set; } = null!;
    public Message? ReplyToMessage { get; private set; }

    public bool IsEncrypted => EncryptionVersion > 0;

    public void SetEncryptedContent(string ciphertext, string nonce, string clientMessageId, int version = 1)
    {
        if (version != 1) throw new ArgumentOutOfRangeException(nameof(version));
        Ciphertext = Require(ciphertext, 12_000, nameof(ciphertext));
        EncryptionNonce = Require(nonce, 100, nameof(nonce));
        ClientMessageId = Require(clientMessageId, 100, nameof(clientMessageId));
        EncryptionVersion = version;
        Content = string.Empty;
        AttachmentUrl = null;
        Touch();
    }

    private static string Require(string value, int maxLength, string name)
    {
        value = value?.Trim() ?? string.Empty;
        if (value.Length is 0 || value.Length > maxLength) throw new ArgumentException("Invalid encrypted message payload.", name);
        return value;
    }

    public void MarkRead()
    {
        if (IsRead) return;
        IsRead = true;
        ReadAt = DateTimeOffset.UtcNow;
        Touch();
    }
}
