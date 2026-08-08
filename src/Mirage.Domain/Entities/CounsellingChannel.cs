using Mirage.Domain.Common;
using Mirage.Domain.Enums;

namespace Mirage.Domain.Entities;

// A private channel between a counsellor and a client, scoped to their CounsellingSession
// relationship — messages and follow-up meetings beyond the session's initial booking time.

public sealed class CounsellingMessage : Entity
{
    private CounsellingMessage() { }

    public CounsellingMessage(Guid sessionId, Guid senderId, string content, MessageType type, string? attachmentUrl,
        bool encryptedPayload = false)
    {
        SessionId = sessionId;
        SenderId = senderId;
        Content = (content ?? string.Empty).Trim();
        Type = type;
        AttachmentUrl = attachmentUrl?.Trim();
    }

    public Guid SessionId { get; private set; }
    public Guid SenderId { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public MessageType Type { get; private set; } = MessageType.Text;
    public string? AttachmentUrl { get; private set; }
    public string? Ciphertext { get; private set; }
    public string? EncryptionNonce { get; private set; }
    public string? ClientMessageId { get; private set; }
    public int EncryptionVersion { get; private set; }
    public bool IsEncrypted => EncryptionVersion > 0;

    public void SetEncryptedContent(string ciphertext, string nonce, string clientMessageId, int version = 1)
    {
        if (version != 1) throw new ArgumentOutOfRangeException(nameof(version));
        Ciphertext = Required(ciphertext, 12_000, nameof(ciphertext));
        EncryptionNonce = Required(nonce, 100, nameof(nonce));
        ClientMessageId = Required(clientMessageId, 100, nameof(clientMessageId));
        EncryptionVersion = version;
        Content = string.Empty;
        AttachmentUrl = null;
        Touch();
    }

    private static string Required(string value, int maxLength, string name)
    {
        value = value?.Trim() ?? string.Empty;
        if (value.Length is 0 || value.Length > maxLength) throw new ArgumentException("Invalid encrypted message payload.", name);
        return value;
    }
}

public sealed class CounsellingMeeting : Entity
{
    private CounsellingMeeting() { }

    // The meeting always happens inside Mirage's own video room — callers don't supply a URL,
    // since follow-up counselling must stay on-platform rather than routing to an external link.
    public CounsellingMeeting(Guid sessionId, Guid scheduledByUserId, string title,
        DateTimeOffset scheduledAt, int? durationMinutes)
    {
        SessionId = sessionId;
        ScheduledByUserId = scheduledByUserId;
        Title = title.Trim();
        MeetingLink = $"mirage-meeting-{Id:N}";
        ScheduledAt = scheduledAt;
        DurationMinutes = durationMinutes;
    }

    public Guid SessionId { get; private set; }
    public Guid ScheduledByUserId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string MeetingLink { get; private set; } = string.Empty;
    public DateTimeOffset ScheduledAt { get; private set; }
    public int? DurationMinutes { get; private set; }
}
