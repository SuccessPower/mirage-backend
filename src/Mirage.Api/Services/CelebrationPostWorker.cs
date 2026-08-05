using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Mirage.Api.Services;

// Periodically sweeps for members whose birthday/anniversary is today (in their local timezone)
// and publishes a celebration entry plus a one-per-recipient email — same shape as
// DobValidationBackfillWorker. A 10-minute interval keeps celebrations (and email retries)
// landing within ten minutes of 09:00 in each member's own timezone; the per-year dedup and
// send-then-stamp email tracking in CelebrationPostService make repeat runs on the same day a no-op.
public sealed class CelebrationPostWorker(IServiceScopeFactory scopeFactory,
    ILogger<CelebrationPostWorker> logger) : BackgroundService
{
    private const int BatchSize = 50;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<CelebrationPostService>();
                await service.RunBatchAsync(BatchSize, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Celebration post worker run failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
