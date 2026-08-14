using Mirage.Domain.Common;
using Mirage.Domain.Enums;

namespace Mirage.Domain.Entities;

public sealed class Newsletter : Entity
{
    private Newsletter() { }
    public Newsletter(Guid authorUserId, string title, string subject, string excerpt, string contentHtml,
        string[] imageUrls, string? thumbnailUrl = null)
    {
        AuthorUserId = authorUserId;
        Update(title, subject, excerpt, contentHtml, imageUrls, thumbnailUrl);
    }

    public Guid AuthorUserId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Subject { get; private set; } = string.Empty;
    public string Excerpt { get; private set; } = string.Empty;
    public string ContentHtml { get; private set; } = string.Empty;
    public string[] ImageUrls { get; private set; } = [];
    /// <summary>Card art for the Journal index and the email's opening image. Falls back to the first gallery
    /// photograph when an author does not choose one.</summary>
    public string? ThumbnailUrl { get; private set; }

    /// <summary>Optional audience narrowing, applied on top of "subscribed and confirmed". Null gender and an
    /// empty status list both mean "everyone", so an unfiltered edition behaves exactly as before.</summary>
    public Sex? AudienceSex { get; private set; }
    public RelationshipStatus[] AudienceRelationshipStatuses { get; private set; } = [];

    public void SetAudience(Sex? sex, RelationshipStatus[]? statuses)
    {
        if (Status is NewsletterStatus.Sending or NewsletterStatus.Sent)
            throw new InvalidOperationException("The audience of a sending or sent newsletter cannot be changed.");
        AudienceSex = sex;
        AudienceRelationshipStatuses = (statuses ?? []).Distinct().OrderBy(x => x).ToArray();
        Touch();
    }
    public NewsletterStatus Status { get; private set; } = NewsletterStatus.Draft;

    /// <summary>Bumped whenever the content changes or a reviewer asks for changes. Approvals are counted per
    /// round, so an edit or a rejection silently voids every approval gathered so far — nothing can be sent on
    /// the strength of a sign-off given to different words.</summary>
    public int ReviewRound { get; private set; } = 1;
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public int RecipientCount { get; private set; }
    public int DeliveredCount { get; private set; }
    public int FailedCount { get; private set; }
    public string? FailureReason { get; private set; }

    public void Update(string title, string subject, string excerpt, string contentHtml, string[] imageUrls,
        string? thumbnailUrl = null)
    {
        if (Status is NewsletterStatus.Sending or NewsletterStatus.Sent) throw new InvalidOperationException("A sending or sent newsletter cannot be edited.");
        if (Status is NewsletterStatus.Scheduled) throw new InvalidOperationException("A scheduled edition is locked. Cancel the schedule to bring it back to your drafts before editing.");
        // Editing something already under review restarts the review: the approvals were for the old text.
        if (Status is NewsletterStatus.InReview or NewsletterStatus.Approved)
        {
            ReviewRound++;
            Status = NewsletterStatus.InReview;
        }
        Title = title.Trim(); Subject = subject.Trim(); Excerpt = excerpt.Trim(); ContentHtml = contentHtml.Trim();
        ImageUrls = imageUrls.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        ThumbnailUrl = string.IsNullOrWhiteSpace(thumbnailUrl) ? null : thumbnailUrl.Trim();
        Touch();
    }

    /// <summary>A published edition is part of the reader-facing archive, with likes and comments hanging off it,
    /// so only work that never reached an inbox can be deleted.</summary>
    public bool CanBeDeleted => Status is NewsletterStatus.Draft or NewsletterStatus.Cancelled
        or NewsletterStatus.Failed or NewsletterStatus.InReview;
    /// <summary>Moves a draft into review. From here the author can no longer send it themselves.</summary>
    public void SubmitForReview()
    {
        if (Status is not (NewsletterStatus.Draft or NewsletterStatus.Cancelled or NewsletterStatus.Failed))
            throw new InvalidOperationException("Only a draft, cancelled, or failed newsletter can be submitted for review.");
        Status = NewsletterStatus.InReview;
        Touch();
    }

    public void WithdrawFromReview()
    {
        if (Status is not (NewsletterStatus.InReview or NewsletterStatus.Approved))
            throw new InvalidOperationException("Only a newsletter in review can be withdrawn.");
        Status = NewsletterStatus.Draft;
        ReviewRound++;
        Touch();
    }

    public void MarkApproved()
    {
        if (Status != NewsletterStatus.InReview) throw new InvalidOperationException("Only a newsletter in review can be approved.");
        Status = NewsletterStatus.Approved;
        Touch();
    }

    /// <summary>A reviewer asked for changes: the round advances, so every approval so far stops counting.</summary>
    public void RequestChanges()
    {
        if (Status is not (NewsletterStatus.InReview or NewsletterStatus.Approved))
            throw new InvalidOperationException("Only a newsletter in review can have changes requested.");
        Status = NewsletterStatus.InReview;
        ReviewRound++;
        Touch();
    }

    /// <summary>Places an approved edition on the calendar. This is the checker's move — the author never makes it.</summary>
    public void Schedule(DateTimeOffset when, int recipients)
    {
        if (Status is not (NewsletterStatus.Approved or NewsletterStatus.Cancelled))
            throw new InvalidOperationException("A newsletter must clear review before it can be scheduled.");
        if (when <= DateTimeOffset.UtcNow) throw new InvalidOperationException("Schedule time must be in the future.");
        ScheduledFor = when; RecipientCount = recipients; Status = NewsletterStatus.Scheduled; FailureReason = null; Touch();
    }

    /// <summary>Moves the send time of an edition already on the calendar. The author's move alone, and only the
    /// clock changes: the approved words and the chosen audience are exactly what the reviewers signed off on, so
    /// the approvals stand and nothing goes back through review.</summary>
    public void Reschedule(DateTimeOffset when, int recipients)
    {
        if (Status != NewsletterStatus.Scheduled)
            throw new InvalidOperationException("Only an edition already on the calendar can be rescheduled.");
        if (when <= DateTimeOffset.UtcNow) throw new InvalidOperationException("Schedule time must be in the future.");
        ScheduledFor = when; RecipientCount = recipients; Touch();
    }

    /// <summary>Takes a scheduled edition off the calendar and hands it back to the author as a draft. The review
    /// round advances with it: whatever happens to the text next, it has to be approved again before it can be
    /// scheduled a second time.</summary>
    public void Cancel()
    {
        if (Status != NewsletterStatus.Scheduled)
            throw new InvalidOperationException("Only scheduled newsletters can be cancelled.");
        Status = NewsletterStatus.Draft;
        ScheduledFor = null;
        RecipientCount = 0;
        ReviewRound++;
        Touch();
    }
    public void StartSending(int recipients) { Status = NewsletterStatus.Sending; RecipientCount = recipients; DeliveredCount = 0; FailedCount = 0; Touch(); }
    public void Complete(int sent, int failed) { DeliveredCount = sent; FailedCount = failed; SentAt = DateTimeOffset.UtcNow; Status = NewsletterStatus.Sent; Touch(); }
    public void Fail(string reason) { Status = NewsletterStatus.Failed; FailureReason = reason[..Math.Min(reason.Length, 1000)]; Touch(); }
}

/// <summary>One entry in an edition's review thread — a plain note, an approval, or a request for changes.
/// The round it was left in is stamped on it, so an approval from before an edit stops counting without the
/// conversation losing its history.</summary>
public sealed class NewsletterReview : Entity
{
    private NewsletterReview() { }
    public NewsletterReview(Guid newsletterId, Guid reviewerUserId, NewsletterReviewDecision decision,
        string? comment, int round)
    {
        NewsletterId = newsletterId; ReviewerUserId = reviewerUserId; Decision = decision;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(); Round = round;
    }
    public Guid NewsletterId { get; private set; }
    public Guid ReviewerUserId { get; private set; }
    public NewsletterReviewDecision Decision { get; private set; }
    public string? Comment { get; private set; }
    public int Round { get; private set; }
}

public sealed class NewsletterDelivery : Entity
{
    private NewsletterDelivery() { }
    public NewsletterDelivery(Guid newsletterId, Guid userId, string email)
    { NewsletterId = newsletterId; UserId = userId; Email = email; }
    public Guid NewsletterId { get; private set; }
    public Guid UserId { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public NewsletterDeliveryStatus Status { get; private set; } = NewsletterDeliveryStatus.Pending;
    public DateTimeOffset? SentAt { get; private set; }
    public string? Error { get; private set; }
    public void MarkSent() { Status = NewsletterDeliveryStatus.Sent; SentAt = DateTimeOffset.UtcNow; Touch(); }
    public void MarkFailed(string error) { Status = NewsletterDeliveryStatus.Failed; Error = error[..Math.Min(error.Length, 500)]; Touch(); }
}

public sealed class NewsletterLike : Entity
{
    private NewsletterLike() { }
    public NewsletterLike(Guid newsletterId, Guid userId) { NewsletterId = newsletterId; UserId = userId; }
    public Guid NewsletterId { get; private set; }
    public Guid UserId { get; private set; }
}

public sealed class NewsletterComment : Entity
{
    private NewsletterComment() { }
    public NewsletterComment(Guid newsletterId, Guid userId, string body, Guid? parentCommentId)
    { NewsletterId = newsletterId; UserId = userId; Body = body.Trim(); ParentCommentId = parentCommentId; }
    public Guid NewsletterId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid? ParentCommentId { get; private set; }
    public string Body { get; private set; } = string.Empty;
}

public sealed class NewsletterCommentLike : Entity
{
    private NewsletterCommentLike() { }
    public NewsletterCommentLike(Guid commentId, Guid userId) { CommentId = commentId; UserId = userId; }
    public Guid CommentId { get; private set; }
    public Guid UserId { get; private set; }
}

public sealed class PlatformManagerInvite : Entity
{
    private PlatformManagerInvite() { }
    public PlatformManagerInvite(string email, string tokenHash, Guid invitedByUserId, DateTimeOffset expiresAt)
    { Email = email; TokenHash = tokenHash; InvitedByUserId = invitedByUserId; ExpiresAt = expiresAt; }
    public string Email { get; private set; } = string.Empty;
    public string TokenHash { get; private set; } = string.Empty;
    public Guid InvitedByUserId { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? AcceptedAt { get; private set; }
    public bool IsAccepted => AcceptedAt.HasValue;
    public void Accept() { AcceptedAt = DateTimeOffset.UtcNow; Touch(); }
}
