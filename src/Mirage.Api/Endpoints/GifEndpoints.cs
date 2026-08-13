using Mirage.Api.Contracts;
using Mirage.Api.Services;

namespace Mirage.Api.Endpoints;

// Read-only proxy over Klipy. Authenticated because there is no reason for an anonymous caller to
// burn our GIF quota, and rate limited because search fires on a debounced keystroke.
internal static class GifEndpoints
{
    public static RouteGroupBuilder MapGifEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/gifs").WithTags("Gifs").RequireAuthorization()
            .RequireRateLimiting("gifs");
        group.MapGet("/search", Search);
        group.MapGet("/trending", Trending);
        return api;
    }

    private static async Task<IResult> Search(HttpContext context, KlipyService klipy,
        ILoggerFactory loggers, string? q, int limit = 24, int page = 1,
        CancellationToken cancellationToken = default)
    {
        if (!klipy.IsConfigured) return NotConfigured(context);
        // An empty query is the picker's initial state rather than a client error — serve trending
        // so the grid is never blank.
        if (string.IsNullOrWhiteSpace(q))
            return await Trending(context, klipy, loggers, limit, page, cancellationToken);
        if (q.Length > 100)
            return EndpointHelpers.ValidationProblem(context, ("q", "Search terms must be 100 characters or fewer."));

        try
        {
            var result = await klipy.SearchAsync(q.Trim(), ClampLimit(limit), ClampPage(page), cancellationToken);
            return ApiResults.Ok(context, result, "GIFs retrieved successfully.");
        }
        catch (HttpRequestException exception)
        {
            return Unavailable(context, loggers, exception);
        }
    }

    private static async Task<IResult> Trending(HttpContext context, KlipyService klipy,
        ILoggerFactory loggers, int limit = 24, int page = 1, CancellationToken cancellationToken = default)
    {
        if (!klipy.IsConfigured) return NotConfigured(context);
        try
        {
            var result = await klipy.TrendingAsync(ClampLimit(limit), ClampPage(page), cancellationToken);
            return ApiResults.Ok(context, result, "GIFs retrieved successfully.");
        }
        catch (HttpRequestException exception)
        {
            return Unavailable(context, loggers, exception);
        }
    }

    // Klipy accepts 8-50 per page and rejects anything outside it, so clamp to its band rather than
    // forwarding a value it will refuse.
    private static int ClampLimit(int limit) => Math.Clamp(limit, 8, 50);

    private static int ClampPage(int page) => Math.Max(1, page);

    // 501 rather than 500: the deployment simply has no Klipy key, which the client uses as the
    // signal to explain itself instead of showing a broken panel.
    private static IResult NotConfigured(HttpContext context) =>
        EndpointHelpers.Problem(context, StatusCodes.Status501NotImplemented,
            "GIFs unavailable", "GIF search is not configured on this environment.");

    // Logged, not just returned: the caller only ever sees "try again", so without this the reason
    // the provider refused us is lost entirely.
    private static IResult Unavailable(HttpContext context, ILoggerFactory loggers,
        HttpRequestException exception)
    {
        loggers.CreateLogger("Mirage.Gifs").LogError(exception, "Klipy request failed.");
        return EndpointHelpers.Problem(context, StatusCodes.Status503ServiceUnavailable,
            "GIFs unavailable", "GIF search is temporarily unavailable. Please try again.");
    }
}
