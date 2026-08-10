using Mirage.Domain.Entities;
using Mirage.Domain.Enums;
using Xunit;

namespace Mirage.Domain.Tests;

public sealed class NewsletterTests
{
    [Fact]
    public void Schedule_RecordsAudienceSnapshotAndUtcInstant()
    {
        var newsletter = Create();
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
    public void CancelledNewsletter_CanBeRescheduled()
    {
        var newsletter = Create();
        newsletter.Schedule(DateTimeOffset.UtcNow.AddHours(1), 10);
        newsletter.Cancel();

        newsletter.Schedule(DateTimeOffset.UtcNow.AddHours(3), 12);

        Assert.Equal(NewsletterStatus.Scheduled, newsletter.Status);
        Assert.Equal(12, newsletter.RecipientCount);
    }

    [Fact]
    public void Schedule_RejectsInstantsInThePast()
    {
        var newsletter = Create();

        Assert.Throws<InvalidOperationException>(() => newsletter.Schedule(DateTimeOffset.UtcNow.AddMinutes(-1), 5));
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
}
