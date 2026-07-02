using Mirage.Domain.Common;
using Mirage.Domain.Enums;

namespace Mirage.Domain.Entities;

// A private channel between a counsellor and a client, scoped to their CounsellingSession
// relationship — messages and follow-up meetings beyond the session's initial booking time.

public sealed class CounsellingMessage : Entity
{
    private CounsellingMessage() { }

    public CounsellingMessage(Guid sessionId, Guid senderId, string content, MessageType type, string? attachmentUrl)
    {
        SessionId = sessionId;
        SenderId = senderId;
        Content = content.Trim();
        Type = type;
        AttachmentUrl = attachmentUrl?.Trim();
    }

    public Guid SessionId { get; private set; }
    public Guid SenderId { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public MessageType Type { get; private set; } = MessageType.Text;
    public string? AttachmentUrl { get; private set; }
}

public sealed class CounsellingMeeting : Entity
{
    private CounsellingMeeting() { }

    public CounsellingMeeting(Guid sessionId, Guid scheduledByUserId, string title, string meetingLink,
        DateTimeOffset scheduledAt, int? durationMinutes)
    {
        SessionId = sessionId;
        ScheduledByUserId = scheduledByUserId;
        Title = title.Trim();
        MeetingLink = meetingLink.Trim();
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
