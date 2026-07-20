using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Mirage.Api.Services;

// Periodically sweeps for profiles with an implausible date of birth that haven't been flagged
// yet and notifies the backlog in small batches — same shape as WelcomeEmailBackfillWorker.
public sealed class DobValidationBackfillWorker(IServiceScopeFactory scopeFactory,
    ILogger<DobValidationBackfillWorker> logger) : BackgroundService
{
    private const int BatchSize = 25;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<DobValidationBackfillService>();
                await service.RunBatchAsync(BatchSize, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "DOB validation backfill worker run failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
