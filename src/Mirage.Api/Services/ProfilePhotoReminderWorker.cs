namespace Mirage.Api.Services;

public sealed class ProfilePhotoReminderWorker(IServiceScopeFactory scopeFactory,
    ILogger<ProfilePhotoReminderWorker> logger) : BackgroundService
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
                await scope.ServiceProvider.GetRequiredService<ProfilePhotoReminderService>()
                    .RunBatchAsync(BatchSize, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Profile photo reminder worker run failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
