using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Mirage.Api.Services;

// Periodically sweeps for due Companion subscriptions and pushes the next journal prompt — same
// shape as CelebrationPostWorker. An hourly interval is frequent enough that a Daily cadence still
// lands within the same day it comes due.
public sealed class CompanionReminderWorker(IServiceScopeFactory scopeFactory,
    ILogger<CompanionReminderWorker> logger) : BackgroundService
{
    private const int BatchSize = 100;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<CompanionReminderService>();
                await service.RunBatchAsync(BatchSize, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Companion reminder worker run failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
