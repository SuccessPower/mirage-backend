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

        // Approved couples are off the market entirely, for everyone.
        query = query.Where(x => !db.Couples.Any(c => c.Status == CoupleStatus.Approved
            && (c.User1Id == x.UserId || c.User2Id == x.UserId)));

        var currentUserId = context.User.TryGetUserId();
        string? myCity = null;
        string? myCountry = null;
        if (currentUserId.HasValue)
        {
            var me = currentUserId.Value;
            query = query.Where(x => x.UserId != me);

            // Once you've liked or matched someone, they drop out of discovery for good —
            // no re-surfacing people you've already acted on.
            var likedIds = db.Likes.Where(x => x.SourceUserId == me).Select(x => x.TargetUserId);
            var matchedIds = db.Matches.Where(x => x.User1Id == me || x.User2Id == me)
                .Select(x => x.User1Id == me ? x.User2Id : x.User1Id);
            query = query.Where(x => !likedIds.Contains(x.UserId) && !matchedIds.Contains(x.UserId));

            var mine = await db.Profiles.AsNoTracking().Where(x => x.UserId == me)
                .Select(x => new { x.City, x.Country }).SingleOrDefaultAsync(cancellationToken);
            myCity = mine?.City;
            myCountry = mine?.Country;
        }
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
        // Nearest-first: same city, then same country, before falling back to verified/recency.
        var pagedProfiles = await query
            .OrderByDescending(x => myCity != null && x.City == myCity)
            .ThenByDescending(x => myCountry != null && x.Country == myCountry)
            .ThenByDescending(x => x.IsVerified)
            .ThenByDescending(x => x.CreatedAt)
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
            request.Bio, request.AnonymityEnabled, request.Interests, request.AvatarUrl, request.Sex,
            request.RelationshipStatus, request.HeightInches, request.SkinTone, request.PreferredLanguage);
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { profile.UserId }, "Profile updated successfully.");
    }
}
