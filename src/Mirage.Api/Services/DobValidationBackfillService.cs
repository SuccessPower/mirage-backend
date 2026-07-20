using Microsoft.EntityFrameworkCore;
using Mirage.Domain.Enums;
using Mirage.Infrastructure.Persistence;

namespace Mirage.Api.Services;

// Registration now rejects an implausible date of birth (see EndpointHelpers.IsAtLeast18 /
// IsPlausibleBirthDate), so this only ever finds legacy profiles created before that guard
// existed. Sweeps for those, notifies each one exactly once, and stamps DobFlaggedAt so repeat
// runs don't re-notify. Driven by DobValidationBackfillWorker on a timer.
public sealed class DobValidationBackfillService(MirageDbContext db, NotificationService notifications,
    ILogger<DobValidationBackfillService> logger)
{
    private const string Title = "Please update your date of birth";
    private const string Body = "The date of birth on your profile doesn't look right. Please update it so your profile stays accurate.";

    public async Task<int> RunBatchAsync(int batchSize, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var youngestValidDob = today.AddYears(-18);
        var oldestValidDob = today.AddYears(-100);

        var profiles = await db.Profiles
            .Where(p => p.DobFlaggedAt == null && p.DateOfBirth != default &&
                        (p.DateOfBirth > youngestValidDob || p.DateOfBirth < oldestValidDob))
            .OrderBy(p => p.CreatedAt)
            .Take(batchSize)
            .Select(p => new { p.UserId, p.DateOfBirth })
            .ToListAsync(cancellationToken);

        if (profiles.Count == 0) return 0;

        var flaggedCount = 0;
        foreach (var profile in profiles)
        {
            try
            {
                await notifications.NotifyAsync(profile.UserId, NotificationType.DateOfBirthInvalid, Title, Body,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "DOB validation backfill: notify failed for UserId {UserId} — will retry next run.", profile.UserId);
                continue;
            }

            await db.Profiles.Where(p => p.UserId == profile.UserId)
                .ExecuteUpdateAsync(p => p.SetProperty(x => x.DobFlaggedAt, DateTimeOffset.UtcNow), cancellationToken);
            flaggedCount++;
        }

        logger.LogInformation("DOB validation backfill: flagged {FlaggedCount}/{TotalCount} in this batch.", flaggedCount, profiles.Count);
        return flaggedCount;
    }
}
