using Mirage.Domain.Entities;
using Mirage.Domain.Enums;
using Xunit;

namespace Mirage.Domain.Tests;

public sealed class NewsletterTests
{
    [Fact]
    public void Schedule_RecordsAudienceSnapshotAndUtcInstant()
    {
        var newsletter = Approved();
        var scheduledFor = DateTimeOffset.UtcNow.AddHours(2);

        newsletter.Schedule(scheduledFor, 42);

        Assert.Equal(NewsletterStatus.Scheduled, newsletter.Status);
        Assert.Equal(42, newsletter.RecipientCount);
        Assert.Equal(scheduledFor, newsletter.ScheduledFor);
    }

    [Fact]
    public void SentNewsletter_CannotBeEdited()
    {
        var newsletter = Create();
        newsletter.StartSending(1);
        newsletter.Complete(1, 0);

        Assert.Throws<InvalidOperationException>(() =>
            newsletter.Update("Changed", "Changed", "Changed", "<p>Changed</p>", []));
    }

    [Fact]
    public void SentNewsletter_CannotBeRescheduled()
    {
        var newsletter = Create();
        newsletter.StartSending(1);
        newsletter.Complete(1, 0);

        Assert.Throws<InvalidOperationException>(() => newsletter.Schedule(DateTimeOffset.UtcNow.AddHours(1), 1));
    }

    [Fact]
    public void Cancelling_ReturnsTheEditionToItsAuthorsDraftsAndVoidsApprovals()
    {
        var newsletter = Approved();
        newsletter.Schedule(DateTimeOffset.UtcNow.AddHours(1), 10);
        var approvedRound = newsletter.ReviewRound;

        newsletter.Cancel();

        Assert.Equal(NewsletterStatus.Draft, newsletter.Status);
        Assert.Null(newsletter.ScheduledFor);
        Assert.NotEqual(approvedRound, newsletter.ReviewRound);
    }

    [Fact]
    public void CancelledNewsletter_MustClearReviewAgainBeforeItCanBeScheduled()
    {
        var newsletter = Approved();
        newsletter.Schedule(DateTimeOffset.UtcNow.AddHours(1), 10);
        newsletter.Cancel();

        Assert.Throws<InvalidOperationException>(() => newsletter.Schedule(DateTimeOffset.UtcNow.AddHours(3), 12));

        newsletter.SubmitForReview();
        newsletter.MarkApproved();
        newsletter.Schedule(DateTimeOffset.UtcNow.AddHours(3), 12);
        Assert.Equal(NewsletterStatus.Scheduled, newsletter.Status);
    }

    [Fact]
    public void Rescheduling_MovesTheClockWithoutDisturbingTheApprovedText()
    {
        var newsletter = Approved();
        newsletter.Schedule(DateTimeOffset.UtcNow.AddHours(1), 10);
        var approvedRound = newsletter.ReviewRound;
        var later = DateTimeOffset.UtcNow.AddHours(6);

        newsletter.Reschedule(later, 11);

        Assert.Equal(NewsletterStatus.Scheduled, newsletter.Status);
        Assert.Equal(later, newsletter.ScheduledFor);
        Assert.Equal(11, newsletter.RecipientCount);
        Assert.Equal(approvedRound, newsletter.ReviewRound);
    }

    [Fact]
    public void Reschedule_RejectsInstantsInThePast()
    {
        var newsletter = Approved();
        newsletter.Schedule(DateTimeOffset.UtcNow.AddHours(1), 10);

        Assert.Throws<InvalidOperationException>(() => newsletter.Reschedule(DateTimeOffset.UtcNow.AddMinutes(-1), 10));
    }

    [Fact]
    public void SentNewsletter_CannotBeRescheduledOrCancelled()
    {
        var newsletter = Approved();
        newsletter.Schedule(DateTimeOffset.UtcNow.AddHours(1), 1);
        newsletter.StartSending(1);
        newsletter.Complete(1, 0);

        Assert.Throws<InvalidOperationException>(() => newsletter.Reschedule(DateTimeOffset.UtcNow.AddHours(2), 1));
        Assert.Throws<InvalidOperationException>(() => newsletter.Cancel());
    }

    [Fact]
    public void Schedule_RejectsInstantsInThePast()
    {
        var newsletter = Approved();

        Assert.Throws<InvalidOperationException>(() => newsletter.Schedule(DateTimeOffset.UtcNow.AddMinutes(-1), 5));
    }

    [Fact]
    public void UnreviewedNewsletter_CannotBeScheduled()
    {
        var newsletter = Create();

        Assert.Throws<InvalidOperationException>(() => newsletter.Schedule(DateTimeOffset.UtcNow.AddHours(1), 10));
    }

    [Fact]
    public void ApprovedNewsletter_CanBeScheduled()
    {
        var newsletter = Create();
        newsletter.SubmitForReview();
        newsletter.MarkApproved();

        newsletter.Schedule(DateTimeOffset.UtcNow.AddHours(1), 10);

        Assert.Equal(NewsletterStatus.Scheduled, newsletter.Status);
    }

    [Fact]
    public void RequestingChanges_AdvancesTheRoundSoApprovalsStopCounting()
    {
        var newsletter = Create();
        newsletter.SubmitForReview();
        newsletter.MarkApproved();
        var roundApprovalsWereGivenIn = newsletter.ReviewRound;

        newsletter.RequestChanges();

        Assert.Equal(NewsletterStatus.InReview, newsletter.Status);
        Assert.NotEqual(roundApprovalsWereGivenIn, newsletter.ReviewRound);
    }

    [Fact]
    public void EditingAnApprovedNewsletter_SendsItBackForReview()
    {
        var newsletter = Create();
        newsletter.SubmitForReview();
        newsletter.MarkApproved();
        var approvedRound = newsletter.ReviewRound;

        newsletter.Update("Changed", "Changed", "Changed", "<p>Changed</p>", []);

        Assert.Equal(NewsletterStatus.InReview, newsletter.Status);
        Assert.NotEqual(approvedRound, newsletter.ReviewRound);
    }

    [Fact]
    public void ScheduledNewsletter_CannotBeEditedWithoutCancellingFirst()
    {
        var newsletter = Create();
        newsletter.SubmitForReview();
        newsletter.MarkApproved();
        newsletter.Schedule(DateTimeOffset.UtcNow.AddHours(1), 10);

        Assert.Throws<InvalidOperationException>(() =>
            newsletter.Update("Changed", "Changed", "Changed", "<p>Changed</p>", []));
    }

    [Fact]
    public void Delivery_TracksProviderSubmissionOutcome()
    {
        var delivery = new NewsletterDelivery(Guid.NewGuid(), Guid.NewGuid(), "reader@example.com");

        delivery.MarkSent();

        Assert.Equal(NewsletterDeliveryStatus.Sent, delivery.Status);
        Assert.NotNull(delivery.SentAt);
    }

    private static Newsletter Create() => new(Guid.NewGuid(), "A title", "A subject", "An excerpt",
        "<p>A story</p>", ["https://images.example.com/cover.jpg"]);

    private static Newsletter Approved()
    {
        var newsletter = Create();
        newsletter.SubmitForReview();
        newsletter.MarkApproved();
        return newsletter;
    }
}
