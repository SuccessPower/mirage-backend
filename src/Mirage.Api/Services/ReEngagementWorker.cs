namespace Mirage.Api.Services;

public sealed class ReEngagementWorker(IServiceScopeFactory scopeFactory,
    ILogger<ReEngagementWorker> logger) : BackgroundService
{
    private const int BatchSize = 100;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ReEngagementService>()
                    .RunBatchAsync(BatchSize, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Re-engagement worker run failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
