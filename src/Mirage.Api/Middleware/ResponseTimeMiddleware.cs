using System.Diagnostics;
using System.Globalization;

namespace Mirage.Api.Middleware;

public sealed class ResponseTimeMiddleware(RequestDelegate next)
{
    public const string StopwatchItemKey = "Mirage.ResponseStopwatch";

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        context.Items[StopwatchItemKey] = stopwatch;
        context.Response.OnStarting(() =>
        {
            var elapsed = stopwatch.Elapsed.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture);
            context.Response.Headers["X-Response-Time-Ms"] = elapsed;
            context.Response.Headers["Server-Timing"] = $"app;dur={elapsed}";
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
}
