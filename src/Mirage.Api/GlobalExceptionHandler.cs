using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mirage.Api.Middleware;
using System.Diagnostics;

namespace Mirage.Api;

public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Unauthorized"),
            ArgumentException => (StatusCodes.Status400BadRequest, "Invalid request"),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Concurrent update conflict"),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred")
        };
        logger.LogError(
            exception,
            "Unhandled exception processing {RequestMethod} {RequestPath}. ExceptionType: {ExceptionType}; " +
            "MappedStatusCode: {StatusCode}; UserId: {UserId}; CorrelationId: {CorrelationId}; TraceId: {TraceId}",
            context.Request.Method,
            context.Request.Path,
            exception.GetType().FullName,
            status,
            context.User.FindFirst("sub")?.Value,
            context.Items[CorrelationIdMiddleware.ItemKey],
            context.TraceIdentifier);
        var responseTimeMs = context.Items[ResponseTimeMiddleware.StopwatchItemKey] is Stopwatch stopwatch
            ? Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3)
            : 0;
        await Results.Problem(new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = status < 500 ? exception.Message : null,
            Extensions =
            {
                ["traceId"] = context.TraceIdentifier,
                ["timestampUtc"] = DateTimeOffset.UtcNow,
                ["responseTimeMs"] = responseTimeMs
            }
        }).ExecuteAsync(context);
        return true;
    }
}
