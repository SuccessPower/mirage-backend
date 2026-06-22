using System.Diagnostics;
using Mirage.Api.Middleware;

namespace Mirage.Api.Contracts;

public static class ApiResults
{
    public static IResult Ok<T>(HttpContext context, T data, string message = "Request completed successfully.") =>
        Results.Ok(Create(context, data, message));

    public static IResult Created<T>(HttpContext context, string location, T data,
        string message = "Resource created successfully.") =>
        Results.Created(location, Create(context, data, message));

    private static ApiResponse<T> Create<T>(HttpContext context, T data, string message)
    {
        var elapsedMilliseconds = context.Items[ResponseTimeMiddleware.StopwatchItemKey] is Stopwatch stopwatch
            ? stopwatch.Elapsed.TotalMilliseconds
            : 0;

        return new ApiResponse<T>(
            true,
            message,
            data,
            new ApiResponseMetadata(
                context.TraceIdentifier,
                DateTimeOffset.UtcNow,
                Math.Round(elapsedMilliseconds, 3)));
    }
}
