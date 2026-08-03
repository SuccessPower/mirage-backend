using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Security;
using Mirage.Api.Services;
using Mirage.Application.Abstractions;
using Mirage.Domain.Enums;
using Mirage.Infrastructure.Persistence;

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
        admin.MapGet("/overview", GetOverview);
        admin.MapGet("/export/pdf", ExportPdf);
        return api;
    }

    private static async Task<IResult> GetOverview(HttpContext context, MirageDbContext db,
        DateTimeOffset? from, DateTimeOffset? to, string? country,
        CancellationToken cancellationToken)
    {
        var validation = ValidateRange(context, from, to);
        if (validation.Error is not null) return validation.Error;
        var report = await BuildOverview(db, validation.From, validation.To, country, cancellationToken);
        return ApiResults.Ok(context, report, "Comprehensive analytics retrieved successfully.");
    }

    private static async Task<IResult> ExportPdf(HttpContext context, MirageDbContext db,
        DateTimeOffset? from, DateTimeOffset? to, string? country,
        CancellationToken cancellationToken)
    {
        var validation = ValidateRange(context, from, to);
        if (validation.Error is not null) return validation.Error;
        var report = await BuildOverview(db, validation.From, validation.To, country, cancellationToken);
        var bytes = AdminAnalyticsPdf.Generate(report);
        var suffix = string.IsNullOrWhiteSpace(country) ? "all-countries" : Slug(country);
        return Results.File(bytes, "application/pdf",
            $"mirage-analytics-{validation.From:yyyyMMdd}-{validation.To:yyyyMMdd}-{suffix}.pdf");
    }

    private static async Task<AdminComprehensiveAnalyticsResponse> BuildOverview(MirageDbContext db,
        DateTimeOffset from, DateTimeOffset to, string? country, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var inactivityCutoff = now.AddDays(-30);
        var normalizedCountry = string.IsNullOrWhiteSpace(country) ? null : country.Trim();

        var users = db.Users.AsNoTracking().Where(x => !x.IsDeleted);
        if (normalizedCountry is not null)
            users = users.Where(u => db.Profiles.Any(p => p.UserId == u.Id && p.Country == normalizedCountry));

        var registered = await users.CountAsync(cancellationToken);
        var enabled = await users.CountAsync(x => x.IsActive, cancellationToken);
        var active = await users.CountAsync(x => x.IsActive && x.LastLoginAt >= inactivityCutoff, cancellationToken);
        var neverLoggedIn = await users.CountAsync(x => x.LastLoginAt == null, cancellationToken);
        var newRegistrations = await users.CountAsync(x => x.CreatedAt >= from && x.CreatedAt <= to, cancellationToken);

        var tierData = await db.Profiles.AsNoTracking()
            .Where(p => normalizedCountry == null || p.Country == normalizedCountry)
            .Join(users, p => p.UserId, u => u.Id, (p, u) => new { p.SubscriptionTier, u.IsActive, u.LastLoginAt })
            .GroupBy(x => x.SubscriptionTier)
            .Select(g => new
            {
                Tier = g.Key,
                Users = g.Count(),
                ActiveUsers = g.Count(x => x.IsActive && x.LastLoginAt >= inactivityCutoff),
                InactiveUsers = g.Count(x => !x.IsActive || x.LastLoginAt < inactivityCutoff || x.LastLoginAt == null)
            })
            .ToListAsync(cancellationToken);
        var tierRows = tierData.Select(x =>
            new AdminTierSummary(x.Tier, x.Users, x.ActiveUsers, x.InactiveUsers)).ToList();
        var tiers = Enum.GetValues<SubscriptionTier>()
            .Select(t => tierRows.SingleOrDefault(x => x.Tier == t) ?? new AdminTierSummary(t, 0, 0, 0)).ToList();

        var countryData = await db.Profiles.AsNoTracking()
            .Where(p => normalizedCountry == null || p.Country == normalizedCountry)
            .Join(db.Users.AsNoTracking().Where(u => !u.IsDeleted), p => p.UserId, u => u.Id,
                (p, u) => new { p.Country, u.IsActive, u.LastLoginAt, u.CreatedAt })
            .GroupBy(x => x.Country)
            .Select(g => new
            {
                Country = g.Key,
                Users = g.Count(),
                ActiveUsers = g.Count(x => x.IsActive && x.LastLoginAt >= inactivityCutoff),
                RegistrationsInPeriod = g.Count(x => x.CreatedAt >= from && x.CreatedAt <= to)
            })
            .OrderByDescending(x => x.Users).ToListAsync(cancellationToken);
        var countries = countryData.Select(x =>
            new AdminCountrySummary(x.Country, x.Users, x.ActiveUsers, x.RegistrationsInPeriod)).ToList();

        var paymentQuery = db.Payments.AsNoTracking().Where(p => p.Status == PaymentStatus.Successful
            && p.PaidAt >= from && p.PaidAt <= to);
        if (normalizedCountry is not null)
            paymentQuery = paymentQuery.Where(p => db.Profiles.Any(x => x.UserId == p.PayerUserId && x.Country == normalizedCountry));
        var revenueData = await paymentQuery.GroupBy(p => p.Currency)
            .Select(g => new
            {
                Currency = g.Key,
                GrossAmount = g.Sum(x => x.Amount),
                PlatformRevenue = g.Sum(x => x.PlatformFeeAmount),
                ProviderPayable = g.Sum(x => x.CounsellorAmount),
                TransactionCount = g.Count(),
                PaidOut = g.Sum(x => x.PayoutStatus == PayoutStatus.Paid ? x.CounsellorAmount : 0m),
                OutstandingPayout = g.Sum(x => x.PayoutStatus != PayoutStatus.Paid ? x.CounsellorAmount : 0m)
            })
            .OrderBy(x => x.Currency).ToListAsync(cancellationToken);
        var revenue = revenueData.Select(x => new AdminRevenueSummary("Counselling session charges", x.Currency,
            x.GrossAmount, x.PlatformRevenue, x.ProviderPayable, x.TransactionCount, x.PaidOut,
            x.OutstandingPayout)).ToList();

        var countryUserIds = normalizedCountry is null
            ? null
            : db.Profiles.Where(p => p.Country == normalizedCountry).Select(p => p.UserId);
        var sessionQuery = db.CounsellingSessions.AsNoTracking().Where(x => x.Status == SessionStatus.Completed
            && x.UpdatedAt >= from && x.UpdatedAt <= to);
        if (countryUserIds is not null) sessionQuery = sessionQuery.Where(x => countryUserIds.Contains(x.ClientUserId));

        return new AdminComprehensiveAnalyticsResponse(from, to, normalizedCountry, now,
            new AdminUserActivitySummary(registered, enabled, registered - enabled, active, registered - active,
                neverLoggedIn, inactivityCutoff), tiers, countries, revenue, newRegistrations,
            await sessionQuery.CountAsync(cancellationToken),
            await db.Couples.CountAsync(x => x.Status == CoupleStatus.Approved, cancellationToken),
            await db.Organisations.CountAsync(x => x.Status == OrganisationStatus.Approved, cancellationToken),
            await db.Counsellors.CountAsync(x => x.IsApproved, cancellationToken),
            await db.Mentors.CountAsync(x => x.IsApproved, cancellationToken),
            await db.ContentReports.CountAsync(x => x.Status == ContentReportStatus.Pending || x.Status == ContentReportStatus.UnderReview, cancellationToken));
    }

    private static (DateTimeOffset From, DateTimeOffset To, IResult? Error) ValidateRange(
        HttpContext context, DateTimeOffset? from, DateTimeOffset? to)
    {
        var rangeTo = to ?? DateTimeOffset.UtcNow;
        var rangeFrom = from ?? rangeTo.AddDays(-30);
        if (rangeFrom > rangeTo)
            return (rangeFrom, rangeTo, EndpointHelpers.ValidationProblem(context, ("from", "From must be before or equal to to.")));
        if (rangeTo - rangeFrom > TimeSpan.FromDays(3660))
            return (rangeFrom, rangeTo, EndpointHelpers.ValidationProblem(context, ("from", "The report range cannot exceed 10 years.")));
        return (rangeFrom, rangeTo, null);
    }

    private static string Slug(string value) => new(value.Trim().ToLowerInvariant()
        .Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());

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
