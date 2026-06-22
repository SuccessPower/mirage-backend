using System.Diagnostics;

namespace Mirage.Api.Middleware;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ItemKey = "Mirage.CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context.Request.Headers[HeaderName]);
        context.Items[ItemKey] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
        using (Serilog.Context.LogContext.PushProperty(
                   "TraceId",
                   Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier))
        {
            await next(context);
        }
    }

    private static string ResolveCorrelationId(string? suppliedValue)
    {
        var value = suppliedValue?.Trim();
        return !string.IsNullOrWhiteSpace(value) && value.Length <= 128
            ? value
            : Guid.NewGuid().ToString("N");
    }
}
