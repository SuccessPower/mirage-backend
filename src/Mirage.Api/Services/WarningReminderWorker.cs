namespace Mirage.Api.Services;

public sealed class WarningReminderWorker(IServiceScopeFactory scopeFactory,
    ILogger<WarningReminderWorker> logger) : BackgroundService
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
                await scope.ServiceProvider.GetRequiredService<WarningReminderService>()
                    .RunBatchAsync(BatchSize, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Warning reminder worker run failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
