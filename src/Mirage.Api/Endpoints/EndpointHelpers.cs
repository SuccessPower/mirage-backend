using System.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Middleware;
using Mirage.Application.Abstractions;
using Mirage.Application.Common;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;
using Mirage.Infrastructure.Identity;

namespace Mirage.Api.Endpoints;

internal static class EndpointHelpers
{
    public static Task<ApplicationUser?> FindByEmailOrUsernameAsync(
        this UserManager<ApplicationUser> userManager, string emailOrUsername)
    {
        var value = emailOrUsername.Trim();
        return value.Contains('@') ? userManager.FindByEmailAsync(value) : userManager.FindByNameAsync(value);
    }

    // Shared gate for user-to-user interactions (likes, chat requests, date requests) — accounts
    // must confirm their email before they can engage with other members. Returns a Forbidden
    // IResult when the user hasn't confirmed, or null when they're clear to proceed.
    public static async Task<IResult?> RequireEmailConfirmedAsync(HttpContext context, Guid userId,
        UserManager<ApplicationUser> userManager, string detail = "Confirm your email address before interacting with other members.")
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        return user?.EmailConfirmed == true ? null : Forbidden(context, detail);
    }

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

    public static ProfileResponse ToResponse(this UserProfile profile, bool isRecommended, string? email = null,
        OrgBadge? badge = null) =>
        new(profile.UserId, email, profile.DisplayName, Age(profile.DateOfBirth), profile.DateOfBirth, profile.City, profile.Country,
            profile.Denomination, profile.Intent, profile.Bio, profile.IsVerified, isRecommended,
            profile.SubscriptionTier, profile.AnonymityEnabled, profile.Interests, profile.AvatarUrl, profile.PhotoUrls, profile.Sex, profile.RelationshipStatus,
            profile.HeightInches, profile.SkinTone, profile.PreferredLanguage, profile.Occupation, profile.CreatedAt,
            OrganisationBadgeUrl: badge?.LogoUrl, OrganisationName: badge?.OrganisationName);

    // Badge eligibility: an approved member of an org (or the org's own owner/admin), where that
    // org has a logo uploaded and is itself approved. A user belongs to at most one org at a time
    // (enforced in OrganisationEndpoints.JoinOrganisation), so this dictionary has at most one
    // entry per userId.
    public static async Task<Dictionary<Guid, OrgBadge>> GetOrgBadgesAsync(this IMirageDbContext db,
        IEnumerable<Guid> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<Guid, OrgBadge>();

        var approvedOrgsWithLogo = db.Organisations.AsNoTracking()
            .Where(o => o.Status == OrganisationStatus.Approved && o.LogoUrl != null);

        var memberBadges = await db.OrganisationMembers.AsNoTracking()
            .Where(m => ids.Contains(m.UserId) && m.Status == OrganisationMemberStatus.Approved)
            .Join(approvedOrgsWithLogo, m => m.OrganisationId, o => o.Id,
                (m, o) => new { m.UserId, o.LogoUrl, o.Name })
            .ToListAsync(cancellationToken);

        var adminBadges = await approvedOrgsWithLogo
            .Where(o => ids.Contains(o.AdminUserId))
            .Select(o => new { UserId = o.AdminUserId, o.LogoUrl, o.Name })
            .ToListAsync(cancellationToken);

        var result = new Dictionary<Guid, OrgBadge>();
        foreach (var b in memberBadges.Concat(adminBadges))
            result[b.UserId] = new OrgBadge(b.LogoUrl!, b.Name);
        return result;
    }

    public static async Task<OrgBadge?> GetOrgBadgeAsync(this IMirageDbContext db, Guid userId,
        CancellationToken cancellationToken)
    {
        var badges = await db.GetOrgBadgesAsync([userId], cancellationToken);
        return badges.GetValueOrDefault(userId);
    }

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
