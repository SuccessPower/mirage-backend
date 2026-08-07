using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Domain.Enums;
using Mirage.Infrastructure.Persistence;

namespace Mirage.Api.Endpoints;

internal static class PublicEndpoints
{
    public static RouteGroupBuilder MapPublicEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/public").WithTags("Public").AllowAnonymous();
        group.MapGet("/landing-stats", GetLandingStats);
        return api;
    }

    private static async Task<IResult> GetLandingStats(HttpContext context, MirageDbContext db,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        // Queries remain sequential because the request's EF Core DbContext does not support
        // concurrent operations.
        var profileCount = await db.Profiles.AsNoTracking()
            .CountAsync(profile => db.Users.Any(user => user.Id == profile.UserId && user.IsActive), cancellationToken);
        var openDateCount = await db.DateRequests.AsNoTracking()
            .CountAsync(request => request.Status == DateRequestStatus.Open && request.EndsAt > now
                && db.Users.Any(user => user.Id == request.RequestorUserId && user.IsActive), cancellationToken);
        var counsellorCount = await db.Counsellors.AsNoTracking()
            .CountAsync(counsellor => counsellor.IsApproved
                && db.Users.Any(user => user.Id == counsellor.UserId && user.IsActive), cancellationToken);
        var organisationCount = await db.Organisations.AsNoTracking()
            .CountAsync(organisation => organisation.Status == OrganisationStatus.Approved, cancellationToken);

        var response = new LandingPageStatsResponse(
            Profiles: profileCount,
            OpenDates: openDateCount,
            Counsellors: counsellorCount,
            Organisations: organisationCount);

        return ApiResults.Ok(context, response, "Landing page statistics retrieved successfully.");
    }
}
