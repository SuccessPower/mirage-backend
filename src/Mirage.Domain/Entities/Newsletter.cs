using Mirage.Domain.Common;
using Mirage.Domain.Enums;

namespace Mirage.Domain.Entities;

public sealed class Newsletter : Entity
{
    private Newsletter() { }
    public Newsletter(Guid authorUserId, string title, string subject, string excerpt, string contentHtml,
        string[] imageUrls)
    {
        AuthorUserId = authorUserId;
        Update(title, subject, excerpt, contentHtml, imageUrls);
    }

    public Guid AuthorUserId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Subject { get; private set; } = string.Empty;
    public string Excerpt { get; private set; } = string.Empty;
    public string ContentHtml { get; private set; } = string.Empty;
    public string[] ImageUrls { get; private set; } = [];
    public NewsletterStatus Status { get; private set; } = NewsletterStatus.Draft;
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public int RecipientCount { get; private set; }
    public int DeliveredCount { get; private set; }
    public int FailedCount { get; private set; }
    public string? FailureReason { get; private set; }

    public void Update(string title, string subject, string excerpt, string contentHtml, string[] imageUrls)
    {
        if (Status is NewsletterStatus.Sending or NewsletterStatus.Sent) throw new InvalidOperationException("A sending or sent newsletter cannot be edited.");
        Title = title.Trim(); Subject = subject.Trim(); Excerpt = excerpt.Trim(); ContentHtml = contentHtml.Trim();
        ImageUrls = imageUrls.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(); Touch();
    }
    public void Schedule(DateTimeOffset when, int recipients)
    {
        if (Status is not (NewsletterStatus.Draft or NewsletterStatus.Scheduled or NewsletterStatus.Cancelled or NewsletterStatus.Failed))
            throw new InvalidOperationException("Only a draft, scheduled, cancelled, or failed newsletter can be scheduled.");
        if (when <= DateTimeOffset.UtcNow) throw new InvalidOperationException("Schedule time must be in the future.");
        ScheduledFor = when; RecipientCount = recipients; Status = NewsletterStatus.Scheduled; FailureReason = null; Touch();
    }
    public void Cancel() { if (Status != NewsletterStatus.Scheduled) throw new InvalidOperationException("Only scheduled newsletters can be cancelled."); Status = NewsletterStatus.Cancelled; Touch(); }
    public void StartSending(int recipients) { Status = NewsletterStatus.Sending; RecipientCount = recipients; DeliveredCount = 0; FailedCount = 0; Touch(); }
    public void Complete(int sent, int failed) { DeliveredCount = sent; FailedCount = failed; SentAt = DateTimeOffset.UtcNow; Status = NewsletterStatus.Sent; Touch(); }
    public void Fail(string reason) { Status = NewsletterStatus.Failed; FailureReason = reason[..Math.Min(reason.Length, 1000)]; Touch(); }
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
