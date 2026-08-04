using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Mirage.Api.Services;

// Periodically sweeps for members whose birthday/anniversary is today and publishes a celebration
// post — same shape as DobValidationBackfillWorker. An hourly interval keeps a new day's
// celebrations reasonably prompt without being wasteful; the per-year dedup in
// CelebrationPostService makes repeat runs on the same day a no-op.
public sealed class CelebrationPostWorker(IServiceScopeFactory scopeFactory,
    ILogger<CelebrationPostWorker> logger) : BackgroundService
{
    private const int BatchSize = 50;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

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
