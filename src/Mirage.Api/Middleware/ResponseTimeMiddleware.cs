using System.Diagnostics;
using System.Globalization;

namespace Mirage.Api.Middleware;

public sealed class ResponseTimeMiddleware(RequestDelegate next)
{
    public const string StopwatchItemKey = "Mirage.ResponseStopwatch";
    public const string ServerTimingItemKey = "Mirage.ServerTiming";

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        context.Items[StopwatchItemKey] = stopwatch;
        context.Response.OnStarting(() =>
        {
            var elapsed = stopwatch.Elapsed.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture);
            context.Response.Headers["X-Response-Time-Ms"] = elapsed;
            var metrics = new List<string> { $"app;dur={elapsed}" };
            if (context.Items[ServerTimingItemKey] is Dictionary<string, double> timings)
            {
                metrics.AddRange(timings.Select(metric =>
                    $"{metric.Key};dur={metric.Value.ToString("0.###", CultureInfo.InvariantCulture)}"));
            }
            context.Response.Headers["Server-Timing"] = string.Join(", ", metrics);
            return Task.CompletedTask;
        });

        try
        {
            await next(context);
        }
        finally
        {
            stopwatch.Stop();
        }
    }

    public static void SetServerTiming(HttpContext context, string metric, double durationMilliseconds)
    {
        if (context.Items[ServerTimingItemKey] is not Dictionary<string, double> timings)
        {
            timings = new Dictionary<string, double>(StringComparer.Ordinal);
            context.Items[ServerTimingItemKey] = timings;
        }
        timings[metric] = durationMilliseconds;
    }
}
