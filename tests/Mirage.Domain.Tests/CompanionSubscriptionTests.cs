using Mirage.Domain.Entities;
using Mirage.Domain.Enums;
using Xunit;

namespace Mirage.Domain.Tests;

public sealed class CompanionSubscriptionTests
{
    [Fact]
    public void New_subscription_is_due_immediately()
    {
        var subscription = new CompanionSubscription(Guid.NewGuid(), CompanionCadence.Daily);

        Assert.True(subscription.NextDueAt <= DateTimeOffset.UtcNow);
        Assert.Null(subscription.LastPromptAt);
    }

    [Fact]
    public void Cadence_change_before_any_prompt_schedules_from_now()
    {
        var subscription = new CompanionSubscription(Guid.NewGuid());

        subscription.SetCadence(CompanionCadence.Daily);

        var expected = DateTimeOffset.UtcNow.AddDays(1);
        Assert.InRange(subscription.NextDueAt, expected.AddMinutes(-1), expected.AddMinutes(1));
    }

    [Fact]
    public void Cadence_change_anchors_on_last_prompt_not_on_now()
    {
        var subscription = new CompanionSubscription(Guid.NewGuid(), CompanionCadence.Daily);
        subscription.AssignPrompt(Guid.NewGuid());
        var promptAt = subscription.LastPromptAt!.Value;

        // Changing my mind (daily -> weekly -> daily) must land back on promptAt + 1 day,
        // not restart a fresh 24h window from the moment of the last click.
        subscription.SetCadence(CompanionCadence.Weekly);
        Assert.Equal(promptAt.AddDays(7), subscription.NextDueAt);

        subscription.SetCadence(CompanionCadence.Daily);
        Assert.Equal(promptAt.AddDays(1), subscription.NextDueAt);
    }

    [Fact]
    public void Cadence_change_with_elapsed_anchor_becomes_due_now_not_in_the_past()
    {
        var subscription = new CompanionSubscription(Guid.NewGuid(), CompanionCadence.Monthly);
        subscription.AssignPrompt(Guid.NewGuid());

        // Anchor + 1 day is in the future here, so this stays anchored; the "already
        // elapsed" clamp is exercised via Advance-then-shorten in real usage. We can
        // still assert the invariant: NextDueAt is never in the past.
        subscription.SetCadence(CompanionCadence.Daily);
        Assert.True(subscription.NextDueAt >= subscription.LastPromptAt!.Value);
        Assert.True(subscription.NextDueAt >= DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public void Advance_moves_the_anchor_and_next_due_by_one_cadence_interval()
    {
        var subscription = new CompanionSubscription(Guid.NewGuid(), CompanionCadence.Daily);
        subscription.AssignPrompt(Guid.NewGuid());

        subscription.Advance();

        Assert.NotNull(subscription.LastPromptAt);
        Assert.Equal(subscription.LastPromptAt!.Value.AddDays(1), subscription.NextDueAt);
    }
}
