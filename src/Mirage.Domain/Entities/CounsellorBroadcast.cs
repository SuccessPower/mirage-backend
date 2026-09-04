using Mirage.Domain.Common;
using Mirage.Domain.Enums;

namespace Mirage.Domain.Entities;

// A counsellor's group: posts, group chat, and scheduled meetings shared with the clients they
// are actually working with. The mentorship side has had this since it shipped; counselling was
// 1:1 (or 1:couple) per session only, so a counsellor running a marriage course for several
// couples at once had nowhere to hold it.
//
// Membership is derived, not stored: a client with a live CounsellingSession against this
// counsellor is in the group, and so is the partner who accepted that session — which is what
// makes it a group of couples rather than a group of individuals.

public sealed class CounsellorPost : Entity
{
    private CounsellorPost() { }

    public CounsellorPost(Guid counsellorProfileId, string content, string? imageUrl)
    {
        CounsellorProfileId = counsellorProfileId;
        Content = content.Trim();
        ImageUrl = imageUrl?.Trim();
    }

    public Guid CounsellorProfileId { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public string? ImageUrl { get; private set; }
}

public sealed class CounsellorGroupMessage : Entity
{
    private CounsellorGroupMessage() { }

    public CounsellorGroupMessage(Guid counsellorProfileId, Guid senderId, string content, MessageType type,
        string? attachmentUrl)
    {
        CounsellorProfileId = counsellorProfileId;
        SenderId = senderId;
        Content = content.Trim();
        Type = type;
        AttachmentUrl = attachmentUrl?.Trim();
    }

    public Guid CounsellorProfileId { get; private set; }
    public Guid SenderId { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public MessageType Type { get; private set; } = MessageType.Text;
    public string? AttachmentUrl { get; private set; }
}

// Deliberately separate from CounsellingMeeting, which belongs to one session. This one belongs
// to the counsellor's whole group and has no session to hang off.
public sealed class CounsellorGroupMeeting : Entity
{
    private CounsellorGroupMeeting() { }

    public CounsellorGroupMeeting(Guid counsellorProfileId, Guid scheduledByUserId, string title,
        string meetingLink, DateTimeOffset scheduledAt, int? durationMinutes)
    {
        CounsellorProfileId = counsellorProfileId;
        ScheduledByUserId = scheduledByUserId;
        Title = title.Trim();
        MeetingLink = meetingLink.Trim();
        ScheduledAt = scheduledAt;
        DurationMinutes = durationMinutes;
    }

    public Guid CounsellorProfileId { get; private set; }
    public Guid ScheduledByUserId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string MeetingLink { get; private set; } = string.Empty;
    public DateTimeOffset ScheduledAt { get; private set; }
    public int? DurationMinutes { get; private set; }
}
