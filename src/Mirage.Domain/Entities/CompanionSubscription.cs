using Mirage.Domain.Common;
using Mirage.Domain.Enums;

namespace Mirage.Domain.Entities;

// One per user: how often Companion should surface a new prompt, and which prompt is currently
// "the one to answer". CompanionReminderService advances NextDueAt and pushes a Notification
// when it elapses; CompanionEndpoints.GetToday assigns CurrentPromptId lazily on first request.
// The schedule anchors on LastPromptAt (when the current prompt was delivered), so switching
// cadence back and forth never restarts the clock from "now".
public sealed class CompanionSubscription : Entity
{
    private CompanionSubscription() { }

    public CompanionSubscription(Guid userId, CompanionCadence cadence = CompanionCadence.Weekly)
    {
        UserId = userId;
        Cadence = cadence;
        NextDueAt = DateTimeOffset.UtcNow;
    }

    public Guid UserId { get; private set; }
    public CompanionCadence Cadence { get; private set; }
    public DateTimeOffset NextDueAt { get; private set; }
    public DateTimeOffset? LastPromptAt { get; private set; }
    public Guid? CurrentPromptId { get; private set; }

    public void SetCadence(CompanionCadence cadence)
    {
        Cadence = cadence;
        var now = DateTimeOffset.UtcNow;
        var next = ComputeNextDueAt(LastPromptAt ?? now, cadence);
        NextDueAt = next < now ? now : next;
        Touch();
    }

    public void AssignPrompt(Guid promptId)
    {
        CurrentPromptId = promptId;
        LastPromptAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public void Advance()
    {
        LastPromptAt = DateTimeOffset.UtcNow;
        NextDueAt = ComputeNextDueAt(LastPromptAt.Value, Cadence);
        Touch();
    }

    private static DateTimeOffset ComputeNextDueAt(DateTimeOffset from, CompanionCadence cadence) => cadence switch
    {
        CompanionCadence.Daily => from.AddDays(1),
        CompanionCadence.Weekly => from.AddDays(7),
        CompanionCadence.BiWeekly => from.AddDays(14),
        CompanionCadence.Monthly => from.AddMonths(1),
        _ => from.AddDays(7)
    };
}
