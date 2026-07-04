using Mirage.Domain.Common;
using Mirage.Domain.Enums;

namespace Mirage.Domain.Entities;

// A mentor's broadcast group: posts, group chat, and scheduled meetings shared with every
// mentee whose MentorRequest has been accepted. Membership is derived from MentorRequest
// (Status == Accepted), not a separate join table.

public sealed class MentorPost : Entity
{
    private MentorPost() { }

    public MentorPost(Guid mentorProfileId, string content, string? imageUrl)
    {
        MentorProfileId = mentorProfileId;
        Content = content.Trim();
        ImageUrl = imageUrl?.Trim();
    }

    public Guid MentorProfileId { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public string? ImageUrl { get; private set; }
}

public sealed class MentorGroupMessage : Entity
{
    private MentorGroupMessage() { }

    public MentorGroupMessage(Guid mentorProfileId, Guid senderId, string content, MessageType type, string? attachmentUrl)
    {
        MentorProfileId = mentorProfileId;
        SenderId = senderId;
        Content = content.Trim();
        Type = type;
        AttachmentUrl = attachmentUrl?.Trim();
    }

    public Guid MentorProfileId { get; private set; }
    public Guid SenderId { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public MessageType Type { get; private set; } = MessageType.Text;
    public string? AttachmentUrl { get; private set; }
}

// A private 1:1 channel between a mentor and one accepted mentee, keyed by the
// MentorRequest that represents their relationship.
public sealed class MentorMessage : Entity
{
    private MentorMessage() { }

    public MentorMessage(Guid mentorRequestId, Guid senderId, string content, MessageType type, string? attachmentUrl)
    {
        MentorRequestId = mentorRequestId;
        SenderId = senderId;
        Content = content.Trim();
        Type = type;
        AttachmentUrl = attachmentUrl?.Trim();
    }

    public Guid MentorRequestId { get; private set; }
    public Guid SenderId { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public MessageType Type { get; private set; } = MessageType.Text;
    public string? AttachmentUrl { get; private set; }
}

public sealed class MentorMeeting : Entity
{
    private MentorMeeting() { }

    public MentorMeeting(Guid mentorProfileId, Guid scheduledByUserId, string title, string meetingLink,
        DateTimeOffset scheduledAt, int? durationMinutes)
    {
        MentorProfileId = mentorProfileId;
        ScheduledByUserId = scheduledByUserId;
        Title = title.Trim();
        MeetingLink = meetingLink.Trim();
        ScheduledAt = scheduledAt;
        DurationMinutes = durationMinutes;
    }

    public Guid MentorProfileId { get; private set; }
    public Guid ScheduledByUserId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string MeetingLink { get; private set; } = string.Empty;
    public DateTimeOffset ScheduledAt { get; private set; }
    public int? DurationMinutes { get; private set; }
}
