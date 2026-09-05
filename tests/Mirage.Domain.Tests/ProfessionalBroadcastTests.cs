using Mirage.Domain.Entities;
using Mirage.Domain.Enums;
using Xunit;

namespace Mirage.Domain.Tests;

public sealed class ProfessionalBroadcastTests
{
    private static ProfessionalBroadcast Message(DateTimeOffset? when = null) =>
        ProfessionalBroadcast.Message(Guid.NewGuid(), null, Guid.NewGuid(), "  Sunday reading  ",
            "  https://img/x.png  ", MentorAudience.PaidMentees, when ?? DateTimeOffset.UtcNow.AddDays(1));

    private static ProfessionalBroadcast Event() =>
        ProfessionalBroadcast.Event(null, Guid.NewGuid(), Guid.NewGuid(), " Marriage evening ", " Come along ",
            null, " The hall ", DateTimeOffset.UtcNow.AddDays(3), DateTimeOffset.UtcNow.AddDays(3).AddHours(2),
            20, MentorAudience.Everyone, DateTimeOffset.UtcNow.AddDays(1));

    [Fact]
    public void New_broadcast_is_scheduled_and_trimmed()
    {
        var broadcast = Message();

        Assert.Equal(BroadcastStatus.Scheduled, broadcast.Status);
        Assert.Equal(BroadcastKind.Message, broadcast.Kind);
        Assert.Equal("Sunday reading", broadcast.Content);
        Assert.Equal("https://img/x.png", broadcast.ImageUrl);
        Assert.Null(broadcast.SentAt);
        Assert.Null(broadcast.PublishedEntityId);
    }

    [Fact]
    public void Blank_image_url_is_stored_as_null_not_as_whitespace()
    {
        var broadcast = ProfessionalBroadcast.Message(Guid.NewGuid(), null, Guid.NewGuid(), "Hello", "   ",
            MentorAudience.Everyone, DateTimeOffset.UtcNow.AddHours(2));

        Assert.Null(broadcast.ImageUrl);
    }

    [Fact]
    public void Event_broadcast_keeps_its_event_fields_and_uses_content_as_the_description()
    {
        var broadcast = Event();

        Assert.Equal(BroadcastKind.Event, broadcast.Kind);
        Assert.Equal("Marriage evening", broadcast.Title);
        Assert.Equal("Come along", broadcast.Content);
        Assert.Equal("The hall", broadcast.Location);
        Assert.Equal(20, broadcast.Capacity);
    }

    [Fact]
    public void Send_now_brings_the_schedule_forward()
    {
        var broadcast = Message(DateTimeOffset.UtcNow.AddDays(5));

        broadcast.SendNow();

        Assert.True(broadcast.ScheduledFor <= DateTimeOffset.UtcNow);
        Assert.Equal(BroadcastStatus.Scheduled, broadcast.Status);
    }

    [Fact]
    public void Marking_sent_records_what_it_became_and_who_it_reached()
    {
        var broadcast = Message();
        var postId = Guid.NewGuid();

        broadcast.MarkSent(postId, 12);

        Assert.Equal(BroadcastStatus.Sent, broadcast.Status);
        Assert.Equal(postId, broadcast.PublishedEntityId);
        Assert.Equal(12, broadcast.RecipientCount);
        Assert.NotNull(broadcast.SentAt);
    }

    [Fact]
    public void A_sent_broadcast_can_no_longer_be_edited_cancelled_or_resent()
    {
        var broadcast = Message();
        broadcast.MarkSent(Guid.NewGuid(), 3);

        Assert.Throws<InvalidOperationException>(() =>
            broadcast.EditMessage("Changed", null, MentorAudience.Everyone, DateTimeOffset.UtcNow.AddDays(1)));
        Assert.Throws<InvalidOperationException>(broadcast.Cancel);
        Assert.Throws<InvalidOperationException>(broadcast.SendNow);
    }

    [Fact]
    public void Cancelling_twice_is_refused_rather_than_silently_accepted()
    {
        var broadcast = Message();

        broadcast.Cancel();

        Assert.Equal(BroadcastStatus.Cancelled, broadcast.Status);
        Assert.Throws<InvalidOperationException>(broadcast.Cancel);
    }

    [Fact]
    public void A_failure_reason_longer_than_the_column_is_truncated_rather_than_rejected()
    {
        var broadcast = Message();

        broadcast.MarkFailed(new string('x', 900));

        Assert.Equal(BroadcastStatus.Failed, broadcast.Status);
        Assert.Equal(500, broadcast.FailureReason!.Length);
    }

    [Fact]
    public void Marking_sent_after_a_failure_clears_the_stale_reason()
    {
        var broadcast = Message();
        broadcast.MarkFailed("network went away");

        broadcast.MarkSent(Guid.NewGuid(), 1);

        Assert.Equal(BroadcastStatus.Sent, broadcast.Status);
        Assert.Null(broadcast.FailureReason);
    }
}
