using Mirage.Domain.Common;
using Mirage.Domain.Enums;

namespace Mirage.Domain.Entities;

// One thing a mentor or counsellor wants their whole group to hear, held until the moment they
// chose to say it. Posting to a group was already possible (MentorPost/CounsellorPost) but only
// right now, in the app, with the group screen open — so a mentor preparing a week of material
// on Sunday had to come back and post it six more times.
//
// A broadcast is not itself the thing the group sees. When its moment arrives the dispatcher
// materialises it into the real thing — a group post, or a private event — and notifies the
// audience. PublishedEntityId points at whatever it became, which is what makes a re-run of the
// dispatcher on an already-sent row a no-op rather than a second post.
//
// One entity serves both practices because the page is one page: a professional who is both a
// mentor and a counsellor writes broadcasts in one place and picks who hears each one. Exactly
// one of MentorProfileId/CounsellorProfileId is set, and that choice decides which group the
// Audience is read against — counselling has no free/paid split, so a counsellor's broadcast is
// always Everyone.
public sealed class ProfessionalBroadcast : Entity
{
    private ProfessionalBroadcast() { }

    public static ProfessionalBroadcast Message(Guid? mentorProfileId, Guid? counsellorProfileId,
        Guid authorUserId, string content, string? imageUrl, MentorAudience audience,
        DateTimeOffset scheduledFor) =>
        new()
        {
            MentorProfileId = mentorProfileId,
            CounsellorProfileId = counsellorProfileId,
            AuthorUserId = authorUserId,
            Kind = BroadcastKind.Message,
            Content = content.Trim(),
            ImageUrl = Clean(imageUrl),
            Audience = audience,
            ScheduledFor = scheduledFor,
        };

    public static ProfessionalBroadcast Event(Guid? mentorProfileId, Guid? counsellorProfileId,
        Guid authorUserId, string title, string? description, string? imageUrl, string location,
        DateTimeOffset startsAt, DateTimeOffset endsAt, int? capacity, MentorAudience audience,
        DateTimeOffset scheduledFor) =>
        new()
        {
            MentorProfileId = mentorProfileId,
            CounsellorProfileId = counsellorProfileId,
            AuthorUserId = authorUserId,
            Kind = BroadcastKind.Event,
            Title = title.Trim(),
            Content = description?.Trim() ?? string.Empty,
            ImageUrl = Clean(imageUrl),
            Location = location.Trim(),
            StartsAt = startsAt,
            EndsAt = endsAt,
            Capacity = capacity,
            Audience = audience,
            ScheduledFor = scheduledFor,
        };

    public Guid AuthorUserId { get; private set; }
    public Guid? MentorProfileId { get; private set; }
    public Guid? CounsellorProfileId { get; private set; }

    public BroadcastKind Kind { get; private set; } = BroadcastKind.Message;
    public MentorAudience Audience { get; private set; } = MentorAudience.Everyone;

    /// <summary>The message body, or the event description — an event's own copy lives here.</summary>
    public string Content { get; private set; } = string.Empty;
    public string? ImageUrl { get; private set; }

    // Event-only. Null on a message broadcast.
    public string? Title { get; private set; }
    public string? Location { get; private set; }
    public DateTimeOffset? StartsAt { get; private set; }
    public DateTimeOffset? EndsAt { get; private set; }
    public int? Capacity { get; private set; }

    public DateTimeOffset ScheduledFor { get; private set; }
    public BroadcastStatus Status { get; private set; } = BroadcastStatus.Scheduled;
    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>The MentorPost, CounsellorPost or OrgEvent this broadcast turned into.</summary>
    public Guid? PublishedEntityId { get; private set; }
    public int RecipientCount { get; private set; }
    public string? FailureReason { get; private set; }

    public void EditMessage(string content, string? imageUrl, MentorAudience audience,
        DateTimeOffset scheduledFor)
    {
        RequireScheduled();
        Content = content.Trim();
        ImageUrl = Clean(imageUrl);
        Audience = audience;
        ScheduledFor = scheduledFor;
        Touch();
    }

    public void EditEvent(string title, string? description, string? imageUrl, string location,
        DateTimeOffset startsAt, DateTimeOffset endsAt, int? capacity, MentorAudience audience,
        DateTimeOffset scheduledFor)
    {
        RequireScheduled();
        Title = title.Trim();
        Content = description?.Trim() ?? string.Empty;
        ImageUrl = Clean(imageUrl);
        Location = location.Trim();
        StartsAt = startsAt;
        EndsAt = endsAt;
        Capacity = capacity;
        Audience = audience;
        ScheduledFor = scheduledFor;
        Touch();
    }

    /// <summary>Bring a scheduled broadcast forward to now — "send this one now" on the page.</summary>
    public void SendNow()
    {
        RequireScheduled();
        ScheduledFor = DateTimeOffset.UtcNow;
        Touch();
    }

    public void Cancel()
    {
        RequireScheduled();
        Status = BroadcastStatus.Cancelled;
        Touch();
    }

    public void MarkSent(Guid publishedEntityId, int recipientCount)
    {
        Status = BroadcastStatus.Sent;
        PublishedEntityId = publishedEntityId;
        RecipientCount = recipientCount;
        SentAt = DateTimeOffset.UtcNow;
        FailureReason = null;
        Touch();
    }

    // A failure is terminal rather than retried: the dispatcher would otherwise keep re-running a
    // broadcast whose content the database itself rejected, and the professional would never be
    // told. Failed shows on the page with the reason, and they can fix it and schedule again.
    public void MarkFailed(string reason)
    {
        Status = BroadcastStatus.Failed;
        FailureReason = reason.Length > 500 ? reason[..500] : reason;
        Touch();
    }

    private void RequireScheduled()
    {
        if (Status != BroadcastStatus.Scheduled)
            throw new InvalidOperationException("Only a scheduled broadcast can be changed.");
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
