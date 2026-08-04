using Microsoft.EntityFrameworkCore;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;
using Mirage.Infrastructure.Persistence;

namespace Mirage.Api.Services;

// Sweeps CompanionSubscriptions whose NextDueAt has elapsed, assigns a fresh random prompt
// (unless the current one is still unanswered — then it keeps nudging that one), pushes a
// Notification over NotificationHub, and advances the schedule. Driven by CompanionReminderWorker.
public sealed class CompanionReminderService(MirageDbContext db, NotificationService notifications,
    ILogger<CompanionReminderService> logger)
{
    public async Task<int> RunBatchAsync(int batchSize, CancellationToken cancellationToken)
    {
        var due = await db.CompanionSubscriptions
            .Where(s => s.NextDueAt <= DateTimeOffset.UtcNow)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
        if (due.Count == 0) return 0;

        var sent = 0;
        foreach (var subscription in due)
        {
            try
            {
                var needsNewPrompt = subscription.CurrentPromptId is null || await db.CompanionEntries.AsNoTracking()
                    .AnyAsync(e => e.AuthorUserId == subscription.UserId && e.PromptId == subscription.CurrentPromptId, cancellationToken);
                if (needsNewPrompt)
                {
                    var prompt = await PickRandomPromptAsync(subscription.Cadence, cancellationToken);
                    if (prompt is null)
                    {
                        logger.LogWarning("Companion reminder skipped for {UserId}: no active prompts available.", subscription.UserId);
                        continue;
                    }
                    subscription.AssignPrompt(prompt.Id);
                }

                var promptText = await db.CompanionPrompts.AsNoTracking()
                    .Where(p => p.Id == subscription.CurrentPromptId)
                    .Select(p => p.Text).SingleAsync(cancellationToken);
                subscription.Advance();

                await notifications.NotifyAsync(subscription.UserId, NotificationType.CompanionPromptDue,
                    "Your Companion prompt is ready", promptText, subscription.Id, "CompanionSubscription", cancellationToken);
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Companion reminder failed for subscription {SubscriptionId}.", subscription.Id);
            }
        }
        return sent;
    }

    private async Task<CompanionPrompt?> PickRandomPromptAsync(CompanionCadence cadence, CancellationToken cancellationToken)
    {
        var matching = await db.CompanionPrompts.AsNoTracking()
            .Where(p => p.IsActive && p.Cadence == cadence).ToListAsync(cancellationToken);
        if (matching.Count == 0)
            matching = await db.CompanionPrompts.AsNoTracking().Where(p => p.IsActive).ToListAsync(cancellationToken);
        return matching.Count == 0 ? null : matching[Random.Shared.Next(matching.Count)];
    }
}
