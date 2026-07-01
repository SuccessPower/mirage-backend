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
        group.MapGet("/{userId:guid}", GetById);
        group.MapGet("/me", GetMine).RequireAuthorization();
        group.MapPut("/me", UpdateMine).RequireAuthorization();
        return api;
    }

    private static async Task<IResult> Discover(HttpContext context, IMirageDbContext db,
        RelationshipIntent? intent, string? city,
        string? denomination, int? minAge, int? maxAge, int page = 1, int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (minAge is < 18 || maxAge is > 100 || minAge > maxAge)
            return EndpointHelpers.ValidationProblem(context,
                ("age", "Age filters must be between 18 and 100, with minAge not exceeding maxAge."));

        var query = db.Profiles.AsNoTracking().AsQueryable();
        var currentUserId = context.User.TryGetUserId();
        if (currentUserId.HasValue) query = query.Where(x => x.UserId != currentUserId.Value);
        if (intent.HasValue) query = query.Where(x => x.Intent == intent);
        if (!string.IsNullOrWhiteSpace(city)) query = query.Where(x => EF.Functions.ILike(x.City, $"%{city.Trim()}%"));
        if (!string.IsNullOrWhiteSpace(denomination))
            query = query.Where(x => EF.Functions.ILike(x.Denomination, $"%{denomination.Trim()}%"));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (minAge.HasValue)
        {
            var latestBirthDate = today.AddYears(-minAge.Value);
            query = query.Where(x => x.DateOfBirth <= latestBirthDate);
        }
        if (maxAge.HasValue)
        {
            var earliestBirthDate = today.AddYears(-(maxAge.Value + 1)).AddDays(1);
            query = query.Where(x => x.DateOfBirth >= earliestBirthDate);
        }
        var recommendedIds = db.Recommendations.AsNoTracking()
            .Where(x => x.Status == RecommendationStatus.Active).Select(x => x.RecommendedUserId);
        var pagedProfiles = await query.OrderByDescending(x => x.IsVerified).ThenByDescending(x => x.CreatedAt)
            .ToPagedResultAsync(page, pageSize, cancellationToken);
        var recommendedUserIds = await recommendedIds
            .Where(userId => pagedProfiles.Items.Select(profile => profile.UserId).Contains(userId))
            .ToListAsync(cancellationToken);
        var response = new Mirage.Application.Common.PagedResult<ProfileResponse>(
            pagedProfiles.Items.Select(profile => profile.ToResponse(recommendedUserIds.Contains(profile.UserId))).ToList(),
            pagedProfiles.Page,
            pagedProfiles.PageSize,
            pagedProfiles.TotalCount);
        return ApiResults.Ok(context, response,
            "Profiles retrieved successfully.");
    }

    private static async Task<IResult> GetById(Guid userId, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var profile = await db.Profiles.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (profile is null) return EndpointHelpers.NotFound(context, "Profile was not found.");
        var recommended = await db.Recommendations.AnyAsync(
            x => x.RecommendedUserId == userId && x.Status == RecommendationStatus.Active, cancellationToken);
        return ApiResults.Ok(context, profile.ToResponse(recommended), "Profile retrieved successfully.");
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
            request.Bio, request.AnonymityEnabled, request.Interests, request.AvatarUrl);
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { profile.UserId }, "Profile updated successfully.");
    }
}
