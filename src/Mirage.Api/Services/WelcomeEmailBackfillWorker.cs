using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Mirage.Api.Services;

// Periodically sweeps for users with no WelcomeEmailSentAt and sends the backlog in small
// batches — small batches keep us under Mailjet's daily/rate limits rather than trying to
// blast the whole backlog in one run.
public sealed class WelcomeEmailBackfillWorker(IServiceScopeFactory scopeFactory,
    ILogger<WelcomeEmailBackfillWorker> logger) : BackgroundService
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
                var service = scope.ServiceProvider.GetRequiredService<WelcomeEmailBackfillService>();
                await service.RunBatchAsync(BatchSize, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Welcome email backfill worker run failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
