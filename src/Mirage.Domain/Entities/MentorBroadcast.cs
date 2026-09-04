using Mirage.Domain.Common;
using Mirage.Domain.Enums;

namespace Mirage.Domain.Entities;

// A mentor's broadcast groups: posts, group chat, and scheduled meetings shared with the mentees
// whose MentorRequest has been accepted. Membership is derived from MentorRequest
// (Status == Accepted), not a separate join table.
//
// A mentor runs two groups, not one: free mentees and paid mentees (MentorRequest.Tier). Every
// broadcast carries an Audience saying which group it is addressed to, and Everyone addresses
// both at once. A mentee only ever sees Everyone plus their own tier's traffic.

public sealed class MentorPost : Entity
{
    private MentorPost() { }

    public MentorPost(Guid mentorProfileId, string content, string? imageUrl,
        MentorAudience audience = MentorAudience.Everyone)
    {
        MentorProfileId = mentorProfileId;
        Content = content.Trim();
        ImageUrl = imageUrl?.Trim();
        Audience = audience;
    }

    public Guid MentorProfileId { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public string? ImageUrl { get; private set; }
    public MentorAudience Audience { get; private set; } = MentorAudience.Everyone;
}

public sealed class MentorGroupMessage : Entity
{
    private MentorGroupMessage() { }

    public MentorGroupMessage(Guid mentorProfileId, Guid senderId, string content, MessageType type,
        string? attachmentUrl, MentorAudience audience = MentorAudience.Everyone)
    {
        MentorProfileId = mentorProfileId;
        SenderId = senderId;
        Content = content.Trim();
        Type = type;
        AttachmentUrl = attachmentUrl?.Trim();
        Audience = audience;
    }

    public Guid MentorProfileId { get; private set; }
    public Guid SenderId { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public MessageType Type { get; private set; } = MessageType.Text;
    public string? AttachmentUrl { get; private set; }

    // A mentee posting into their own group can only address that group, so this mirrors the
    // sender's tier; only the mentor ever chooses Everyone.
    public MentorAudience Audience { get; private set; } = MentorAudience.Everyone;
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
        DateTimeOffset scheduledAt, int? durationMinutes, Guid? mentorRequestId = null,
        MentorAudience audience = MentorAudience.Everyone)
    {
        MentorProfileId = mentorProfileId;
        ScheduledByUserId = scheduledByUserId;
        Title = title.Trim();
        MeetingLink = meetingLink.Trim();
        ScheduledAt = scheduledAt;
        DurationMinutes = durationMinutes;
        MentorRequestId = mentorRequestId;
        Audience = audience;
    }

    public Guid MentorProfileId { get; private set; }
    public Guid? MentorRequestId { get; private set; }
    public Guid ScheduledByUserId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string MeetingLink { get; private set; } = string.Empty;
    public DateTimeOffset ScheduledAt { get; private set; }
    public int? DurationMinutes { get; private set; }

    // Ignored on a private 1:1 meeting (MentorRequestId set) — that meeting has exactly one
    // audience, the mentee it belongs to.
    public MentorAudience Audience { get; private set; } = MentorAudience.Everyone;
}
