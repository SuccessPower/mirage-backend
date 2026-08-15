using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Security;
using Mirage.Api.Services;
using Mirage.Application.Abstractions;
using Mirage.Domain.Enums;
using Mirage.Infrastructure.Persistence;

namespace Mirage.Api.Endpoints;

internal static class SearchEndpoints
{
    public static RouteGroupBuilder MapSearchEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/search", Search).WithTags("Search").RequireAuthorization();
        return api;
    }

    private static async Task<IResult> Search(string? q, HttpContext context, MirageDbContext db,
        IConfiguration configuration, int limit = 5, CancellationToken cancellationToken = default)
    {
        var query = q?.Trim();
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            return ApiResults.Ok(context, new GlobalSearchResponse([]), "Enter at least two characters.");

        query = query[..Math.Min(query.Length, 100)];
        limit = Math.Clamp(limit, 1, 10);
        var pattern = $"%{query}%";
        var userId = context.User.GetUserId();
        var results = new List<GlobalSearchItemResponse>(limit * 6);

        results.AddRange(await db.Profiles.AsNoTracking()
            .Where(profile => profile.IsProfileComplete
                && EF.Functions.ILike(profile.DisplayName, pattern))
            .Where(profile => db.Users.Any(user => user.Id == profile.UserId && user.IsActive && !user.IsHidden))
            // Search uses the same visibility rule as Discovery, or it would surface profiles whose
            // deep link then 404s at GetById.
            .Where(ProfilePhotoVisibility.IsVisible(ProfilePhotoVisibility.Cutoff(configuration)))
            .OrderBy(profile => profile.DisplayName)
            .Take(limit)
            .Select(profile => new GlobalSearchItemResponse("Profile", profile.UserId, profile.DisplayName,
                profile.Occupation != null && profile.Occupation != ""
                    ? $"{profile.Occupation} · {profile.City}, {profile.Country}"
                    : $"{profile.City}, {profile.Country}",
                profile.AvatarUrl, $"/profiles/{profile.UserId}"))
            .ToListAsync(cancellationToken));

        results.AddRange(await db.Counsellors.AsNoTracking()
            .Where(counsellor => counsellor.IsApproved
                && ((!counsellor.IsAnonymous && EF.Functions.ILike(counsellor.UserProfile.DisplayName, pattern))
                    || EF.Functions.ILike(counsellor.UserProfile.Bio, pattern)))
            .OrderBy(counsellor => counsellor.UserProfile.DisplayName)
            .Take(limit)
            .Select(counsellor => new GlobalSearchItemResponse("Counsellor", counsellor.Id,
                counsellor.IsAnonymous ? "Mirage counsellor" : counsellor.UserProfile.DisplayName,
                counsellor.YearsExperience + " years experience",
                counsellor.IsAnonymous ? null : counsellor.UserProfile.AvatarUrl,
                $"/counsellors/{counsellor.Id}"))
            .ToListAsync(cancellationToken));

        results.AddRange(await db.Mentors.AsNoTracking()
            .Where(mentor => mentor.IsApproved
                && (EF.Functions.ILike(mentor.UserProfile.DisplayName, pattern)
                    || EF.Functions.ILike(mentor.Testimony, pattern)))
            .OrderBy(mentor => mentor.UserProfile.DisplayName)
            .Take(limit)
            .Select(mentor => new GlobalSearchItemResponse("Mentor", mentor.Id,
                mentor.UserProfile.DisplayName, mentor.YearsMarried + " years married",
                mentor.UserProfile.AvatarUrl, $"/mentors/{mentor.Id}"))
            .ToListAsync(cancellationToken));

        results.AddRange(await db.Communities.AsNoTracking()
            .Where(community => community.Status == CommunityStatus.Active
                && (EF.Functions.ILike(community.Name, pattern)
                    || EF.Functions.ILike(community.Category, pattern)
                    || EF.Functions.ILike(community.Description, pattern)))
            .OrderBy(community => community.Name)
            .Take(limit)
            .Select(community => new GlobalSearchItemResponse("Community", community.Id, community.Name,
                community.Category, community.AvatarUrl, $"/communities/{community.Id}"))
            .ToListAsync(cancellationToken));

        results.AddRange(await db.CommunityPosts.AsNoTracking()
            .Where(post => !post.IsHidden && EF.Functions.ILike(post.Body, pattern)
                && db.CommunityMembers.Any(member => member.CommunityId == post.CommunityId
                    && member.UserId == userId && member.Status == CommunityMemberStatus.Approved))
            .OrderByDescending(post => post.CreatedAt)
            .Take(limit)
            .Select(post => new GlobalSearchItemResponse("Article", post.Id,
                post.Body.Length > 90 ? post.Body.Substring(0, 90) + "…" : post.Body,
                post.Community.Name, post.ImageUrl, $"/communities/{post.CommunityId}?post={post.Id}"))
            .ToListAsync(cancellationToken));

        results.AddRange(await db.Testimonials.AsNoTracking()
            .Where(story => EF.Functions.ILike(story.Title, pattern) || EF.Functions.ILike(story.Body, pattern))
            .OrderByDescending(story => story.CreatedAt)
            .Take(limit)
            .Select(story => new GlobalSearchItemResponse("Story", story.Id, story.Title,
                db.Profiles.Where(profile => profile.UserId == story.AuthorUserId)
                    .Select(profile => profile.DisplayName).FirstOrDefault() ?? "Mirage member",
                story.ImageUrl, $"/testimonials/{story.Id}"))
            .ToListAsync(cancellationToken));

        return ApiResults.Ok(context, new GlobalSearchResponse(results), "Search results retrieved successfully.");
    }
}
