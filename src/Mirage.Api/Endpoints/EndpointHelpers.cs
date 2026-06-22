using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Middleware;
using Mirage.Application.Common;
using Mirage.Domain.Entities;

namespace Mirage.Api.Endpoints;

internal static class EndpointHelpers
{
    public static int Age(DateOnly birthDate)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var age = today.Year - birthDate.Year;
        if (birthDate > today.AddYears(-age)) age--;
        return age;
    }

    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query, int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PagedResult<T>(items, page, pageSize, total);
    }

    public static ProfileResponse ToResponse(this UserProfile profile, bool isRecommended) =>
        new(profile.UserId, profile.DisplayName, Age(profile.DateOfBirth), profile.City, profile.Country,
            profile.Denomination, profile.Intent, profile.Bio, profile.IsVerified, isRecommended,
            profile.SubscriptionTier, profile.Interests);

    public static IResult ValidationProblem(HttpContext context, params (string Field, string Error)[] errors) =>
        ValidationProblem(context,
            errors.GroupBy(x => x.Field).ToDictionary(
                group => group.Key,
                group => group.Select(x => x.Error).ToArray()));

    public static IResult ValidationProblem(HttpContext context, IDictionary<string, string[]> errors,
        string title = "One or more validation errors occurred.") =>
        Results.ValidationProblem(errors, title: title, extensions: Extensions(context));

    public static IResult NotFound(HttpContext context, string detail = "The requested resource was not found.") =>
        Problem(context, StatusCodes.Status404NotFound, "Resource not found", detail);

    public static IResult Conflict(HttpContext context, string detail) =>
        Problem(context, StatusCodes.Status409Conflict, "Request conflict", detail);

    public static IResult Forbidden(HttpContext context, string detail = "You are not permitted to perform this action.") =>
        Problem(context, StatusCodes.Status403Forbidden, "Forbidden", detail);

    public static IResult Problem(HttpContext context, int statusCode, string title, string detail) =>
        Results.Problem(new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Extensions = Extensions(context)
        });

    private static Dictionary<string, object?> Extensions(HttpContext context)
    {
        var responseTimeMs = context.Items[ResponseTimeMiddleware.StopwatchItemKey] is Stopwatch stopwatch
            ? Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3)
            : 0;

        return new Dictionary<string, object?>
        {
            ["traceId"] = context.TraceIdentifier,
            ["timestampUtc"] = DateTimeOffset.UtcNow,
            ["responseTimeMs"] = responseTimeMs
        };
    }
}
