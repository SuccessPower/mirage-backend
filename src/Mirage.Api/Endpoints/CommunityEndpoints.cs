using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Security;
using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;

namespace Mirage.Api.Endpoints;

internal static class CommunityEndpoints
{
    public static RouteGroupBuilder MapCommunityEndpoints(this RouteGroupBuilder api)
    {
        var communities = api.MapGroup("/communities").WithTags("Communities").RequireAuthorization();
        communities.MapGet("/", List);
        communities.MapGet("/{id:guid}", GetById);
        communities.MapPost("/", Create);
        communities.MapPost("/{id:guid}/join", Join);
        communities.MapDelete("/{id:guid}/membership", Leave);
        communities.MapGet("/{id:guid}/posts", ListPosts);
        communities.MapPost("/{id:guid}/posts", CreatePost);
        return api;
    }

    private static async Task<IResult> List(HttpContext context, IMirageDbContext db, string? category,
        string? search, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var userId = context.User.GetUserId();
        var query = db.Communities.AsNoTracking().Where(x => x.Status == CommunityStatus.Active);

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(x => EF.Functions.ILike(x.Category, category.Trim()));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = $"%{search.Trim()}%";
            query = query.Where(x => EF.Functions.ILike(x.Name, value) ||
                                     EF.Functions.ILike(x.Description, value) ||
                                     EF.Functions.ILike(x.Category, value));
        }

        var result = await query
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new CommunityResponse(
                x.Id,
                x.Name,
                x.Category,
                x.Description,
                x.CreatedByUserId,
                x.Status,
                x.Members.Count(m => m.LeftAt == null),
                x.Posts.Count,
                x.Members.Any(m => m.UserId == userId && m.LeftAt == null),
                x.CreatedAt))
            .ToPagedResultAsync(page, pageSize, cancellationToken);

        return ApiResults.Ok(context, result, "Communities retrieved successfully.");
    }

    private static async Task<IResult> GetById(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var community = await db.Communities.AsNoTracking()
            .Where(x => x.Id == id && x.Status == CommunityStatus.Active)
            .Select(x => new CommunityResponse(
                x.Id,
                x.Name,
                x.Category,
                x.Description,
                x.CreatedByUserId,
                x.Status,
                x.Members.Count(m => m.LeftAt == null),
                x.Posts.Count,
                x.Members.Any(m => m.UserId == userId && m.LeftAt == null),
                x.CreatedAt))
            .SingleOrDefaultAsync(cancellationToken);

        return community is null
            ? EndpointHelpers.NotFound(context, "Community was not found.")
            : ApiResults.Ok(context, community, "Community retrieved successfully.");
    }

    private static async Task<IResult> Create(CreateCommunityRequest request, HttpContext context,
        IMirageDbContext db, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return EndpointHelpers.ValidationProblem(context, ("name", "Community name is required."));
        if (string.IsNullOrWhiteSpace(request.Category))
            return EndpointHelpers.ValidationProblem(context, ("category", "Community category is required."));
        if (string.IsNullOrWhiteSpace(request.Description))
            return EndpointHelpers.ValidationProblem(context, ("description", "Community description is required."));

        var userId = context.User.GetUserId();
        var community = new Community(userId, request.Name, request.Category, request.Description);
        community.Members.Add(new CommunityMember(community.Id, userId, CommunityMemberRole.Owner));

        db.Communities.Add(community);
        await db.SaveChangesAsync(cancellationToken);

        var response = new CommunityResponse(
            community.Id,
            community.Name,
            community.Category,
            community.Description,
            community.CreatedByUserId,
            community.Status,
            1,
            0,
            true,
            community.CreatedAt);

        return ApiResults.Created(context, $"/api/v1/communities/{community.Id}", response,
            "Community created successfully.");
    }

    private static async Task<IResult> Join(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var communityExists = await db.Communities.AsNoTracking()
            .AnyAsync(x => x.Id == id && x.Status == CommunityStatus.Active, cancellationToken);
        if (!communityExists) return EndpointHelpers.NotFound(context, "Community was not found.");

        var member = await db.CommunityMembers
            .SingleOrDefaultAsync(x => x.CommunityId == id && x.UserId == userId, cancellationToken);

        if (member is null)
            db.CommunityMembers.Add(new CommunityMember(id, userId));
        else if (member.LeftAt is not null)
            member.Rejoin();

        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { communityId = id }, "Community joined successfully.");
    }

    private static async Task<IResult> Leave(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var member = await db.CommunityMembers
            .SingleOrDefaultAsync(x => x.CommunityId == id && x.UserId == userId && x.LeftAt == null,
                cancellationToken);
        if (member is null) return EndpointHelpers.NotFound(context, "Community membership was not found.");
        if (member.Role == CommunityMemberRole.Owner)
            return EndpointHelpers.Conflict(context, "Community owners cannot leave their own community.");

        member.Leave();
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { communityId = id }, "Community membership ended successfully.");
    }

    private static async Task<IResult> ListPosts(Guid id, HttpContext context, IMirageDbContext db,
        int page = 1, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        var userId = context.User.GetUserId();
        var isMember = await IsActiveMemberAsync(id, userId, db, cancellationToken);
        if (!isMember) return EndpointHelpers.Forbidden(context);

        var posts = await db.CommunityPosts.AsNoTracking()
            .Where(x => x.CommunityId == id)
            .Join(db.Profiles.AsNoTracking(), post => post.AuthorUserId, profile => profile.UserId,
                (post, profile) => new CommunityPostResponse(
                    post.Id,
                    post.CommunityId,
                    post.AuthorUserId,
                    profile.DisplayName,
                    post.Body,
                    post.CreatedAt))
            .OrderByDescending(x => x.CreatedAt)
            .ToPagedResultAsync(page, pageSize, cancellationToken);

        return ApiResults.Ok(context, posts, "Community posts retrieved successfully.");
    }

    private static async Task<IResult> CreatePost(Guid id, CreateCommunityPostRequest request,
        HttpContext context, IMirageDbContext db, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Body))
            return EndpointHelpers.ValidationProblem(context, ("body", "Post body is required."));

        var userId = context.User.GetUserId();
        var isMember = await IsActiveMemberAsync(id, userId, db, cancellationToken);
        if (!isMember) return EndpointHelpers.Forbidden(context);

        var post = new CommunityPost(id, userId, request.Body);
        db.CommunityPosts.Add(post);
        await db.SaveChangesAsync(cancellationToken);

        var authorName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => x.DisplayName)
            .SingleOrDefaultAsync(cancellationToken) ?? "Member";

        var response = new CommunityPostResponse(post.Id, post.CommunityId, post.AuthorUserId, authorName,
            post.Body, post.CreatedAt);
        return ApiResults.Created(context, $"/api/v1/communities/{id}/posts/{post.Id}", response,
            "Community post created successfully.");
    }

    private static Task<bool> IsActiveMemberAsync(Guid communityId, Guid userId, IMirageDbContext db,
        CancellationToken cancellationToken) =>
        db.CommunityMembers.AsNoTracking().AnyAsync(
            x => x.CommunityId == communityId && x.UserId == userId && x.LeftAt == null &&
                 x.Community.Status == CommunityStatus.Active,
            cancellationToken);
}
