using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Security;
using Mirage.Application.Abstractions;
using Mirage.Domain.Enums;

namespace Mirage.Api.Endpoints;

internal static class ProfileEndpoints
{
    public static RouteGroupBuilder MapProfileEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/profiles").WithTags("Profiles");
        group.MapGet("/", Discover);
        group.MapGet("/me", GetMine).RequireAuthorization();
        group.MapPut("/me", UpdateMine).RequireAuthorization();
        return api;
    }

    private static async Task<IResult> Discover(HttpContext context, IMirageDbContext db,
        RelationshipIntent? intent, string? city,
        string? denomination, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var query = db.Profiles.AsNoTracking().AsQueryable();
        if (intent.HasValue) query = query.Where(x => x.Intent == intent);
        if (!string.IsNullOrWhiteSpace(city)) query = query.Where(x => EF.Functions.ILike(x.City, $"%{city.Trim()}%"));
        if (!string.IsNullOrWhiteSpace(denomination))
            query = query.Where(x => EF.Functions.ILike(x.Denomination, $"%{denomination.Trim()}%"));
        var recommendedIds = db.Recommendations.AsNoTracking()
            .Where(x => x.Status == RecommendationStatus.Active).Select(x => x.RecommendedUserId);
        var projected = query.OrderByDescending(x => x.IsVerified).ThenByDescending(x => x.CreatedAt)
            .Select(x => new ProfileResponse(x.UserId, x.DisplayName,
                DateTime.UtcNow.Year - x.DateOfBirth.Year, x.City, x.Country, x.Denomination, x.Intent, x.Bio,
                x.IsVerified, recommendedIds.Contains(x.UserId), x.SubscriptionTier, x.Interests));
        return ApiResults.Ok(context,
            await projected.ToPagedResultAsync(page, pageSize, cancellationToken),
            "Profiles retrieved successfully.");
    }

    private static async Task<IResult> GetMine(HttpContext context, IMirageDbContext db, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var profile = await db.Profiles.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (profile is null) return EndpointHelpers.NotFound(context, "Profile was not found.");
        var recommended = await db.Recommendations.AnyAsync(
            x => x.RecommendedUserId == userId && x.Status == RecommendationStatus.Active, cancellationToken);
        return ApiResults.Ok(context, profile.ToResponse(recommended), "Profile retrieved successfully.");
    }

    private static async Task<IResult> UpdateMine(UpdateProfileRequest request, HttpContext context,
        IMirageDbContext db, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName) || string.IsNullOrWhiteSpace(request.City))
            return EndpointHelpers.ValidationProblem(context, ("profile", "Display name and city are required."));
        var profile = await db.Profiles.SingleOrDefaultAsync(x => x.UserId == context.User.GetUserId(), cancellationToken);
        if (profile is null) return EndpointHelpers.NotFound(context, "Profile was not found.");
        profile.Update(request.DisplayName, request.City, request.Country, request.Denomination, request.Intent,
            request.Bio, request.AnonymityEnabled, request.Interests);
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { profile.UserId }, "Profile updated successfully.");
    }
}
