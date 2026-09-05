using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Mirage.Api.Services;

// Sweeps for scheduled broadcasts whose moment has arrived — same shape as NewsletterDispatchWorker.
// A one-minute interval is the resolution the composer offers (the picker is minute-granular), so
// a broadcast lands within a minute of the time its author chose. "Send now" on the page does not
// wait for this tick; it dispatches inline.
public sealed class BroadcastDispatchWorker(IServiceScopeFactory scopeFactory,
    ILogger<BroadcastDispatchWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<BroadcastDispatchService>();
                await service.RunDueAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Broadcast dispatch worker run failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
