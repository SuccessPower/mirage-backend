using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Security;
using Mirage.Application.Abstractions;
using Mirage.Domain.Enums;

namespace Mirage.Api.Endpoints;

// Powers the admin dashboard's "who's engaging" widgets — counts of likes, chat requests/approvals,
// conversation close/block, and date request create/accept, broken down by the gender of the two
// people involved. Reads only from the append-only AnalyticsEvent log (see AnalyticsRecorder), so
// counts never reveal conversation content and stay accurate even after a profile's gender changes.
internal static class AdminAnalyticsEndpoints
{
    public static RouteGroupBuilder MapAdminAnalyticsEndpoints(this RouteGroupBuilder api)
    {
        var admin = api.MapGroup("/admin/analytics").WithTags("Admin")
            .RequireAuthorization(MiragePolicy.PlatformAdmin);
        admin.MapGet("/summary", GetSummary);
        admin.MapGet("/timeseries", GetTimeseries);
        return api;
    }

    private static async Task<IResult> GetSummary(HttpContext context, IMirageDbContext db,
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken)
    {
        var rangeFrom = from ?? DateTimeOffset.UtcNow.AddDays(-30);
        var rangeTo = to ?? DateTimeOffset.UtcNow;

        var raw = await db.AnalyticsEvents.AsNoTracking()
            .Where(x => x.CreatedAt >= rangeFrom && x.CreatedAt <= rangeTo)
            .GroupBy(x => new { x.EventType, x.ActorSex, x.TargetSex })
            .Select(g => new { g.Key.EventType, g.Key.ActorSex, g.Key.TargetSex, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var events = raw
            .GroupBy(x => x.EventType)
            .Select(g => new AdminAnalyticsEventSummary(
                g.Key,
                g.Sum(x => x.Count),
                g.GroupBy(x => GenderPairOf(x.ActorSex, x.TargetSex))
                    .Select(gg => new AdminAnalyticsGenderBucket(gg.Key, gg.Sum(x => x.Count)))
                    .OrderByDescending(x => x.Count)
                    .ToList()))
            .OrderBy(x => x.EventType)
            .ToList();

        return ApiResults.Ok(context,
            new AdminAnalyticsSummaryResponse(rangeFrom, rangeTo, events),
            "Analytics summary retrieved successfully.");
    }

    private static async Task<IResult> GetTimeseries(AnalyticsEventType type, HttpContext context,
        IMirageDbContext db, string bucket = "day", DateTimeOffset? from = null, DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        if (bucket is not ("day" or "week" or "month"))
            return EndpointHelpers.ValidationProblem(context, ("bucket", "Bucket must be day, week, or month."));

        var rangeFrom = from ?? DateTimeOffset.UtcNow.AddDays(-90);
        var rangeTo = to ?? DateTimeOffset.UtcNow;

        var timestamps = await db.AnalyticsEvents.AsNoTracking()
            .Where(x => x.EventType == type && x.CreatedAt >= rangeFrom && x.CreatedAt <= rangeTo)
            .Select(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        var points = timestamps
            .GroupBy(t => BucketStartOf(DateOnly.FromDateTime(t.UtcDateTime), bucket))
            .Select(g => new AdminAnalyticsTimeseriesPoint(g.Key, g.Count()))
            .OrderBy(x => x.BucketStart)
            .ToList();

        return ApiResults.Ok(context,
            new AdminAnalyticsTimeseriesResponse(type, bucket, points),
            "Analytics timeseries retrieved successfully.");
    }

    private static string GenderPairOf(Sex? actor, Sex? target)
    {
        if (actor is null || target is null) return "Unknown";
        if (actor == Sex.Male && target == Sex.Male) return "Male-Male";
        if (actor == Sex.Female && target == Sex.Female) return "Female-Female";
        return "Mixed";
    }

    private static DateOnly BucketStartOf(DateOnly date, string bucket) => bucket switch
    {
        "week" => date.AddDays(-(int)date.DayOfWeek),
        "month" => new DateOnly(date.Year, date.Month, 1),
        _ => date
    };
}
