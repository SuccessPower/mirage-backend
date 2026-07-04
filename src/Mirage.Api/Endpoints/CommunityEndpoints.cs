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
        communities.MapGet("/avatar-library", GetAvatarLibrary);
        communities.MapGet("/", List);
        communities.MapGet("/{id:guid}", GetById);
        communities.MapPost("/", Create);
        communities.MapPatch("/{id:guid}/avatar", UpdateAvatar);
        communities.MapGet("/{id:guid}/members", ListMembers);
        communities.MapPost("/{id:guid}/join", Join);
        communities.MapDelete("/{id:guid}/membership", Leave);
        communities.MapGet("/{id:guid}/posts", ListPosts);
        communities.MapPost("/{id:guid}/posts", CreatePost);
        communities.MapPost("/posts/{postId:guid}/likes", LikePost);
        communities.MapDelete("/posts/{postId:guid}/likes", UnlikePost);
        communities.MapGet("/posts/{postId:guid}/comments", ListComments);
        communities.MapPost("/posts/{postId:guid}/comments", CreateComment);
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
                x.AvatarUrl,
                x.AvatarKey,
                x.CreatedByUserId,
                x.Status,
                x.Members.Count(m => m.LeftAt == null),
                x.Posts.Count,
                x.Members.Any(m => m.UserId == userId && m.LeftAt == null),
                x.Members
                    .Where(m => m.UserId == userId && m.LeftAt == null)
                    .Select(m => (CommunityMemberRole?)m.Role)
                    .FirstOrDefault(),
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
                x.AvatarUrl,
                x.AvatarKey,
                x.CreatedByUserId,
                x.Status,
                x.Members.Count(m => m.LeftAt == null),
                x.Posts.Count,
                x.Members.Any(m => m.UserId == userId && m.LeftAt == null),
                x.Members
                    .Where(m => m.UserId == userId && m.LeftAt == null)
                    .Select(m => (CommunityMemberRole?)m.Role)
                    .FirstOrDefault(),
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
        var community = new Community(userId, request.Name, request.Category, request.Description,
            request.AvatarUrl, request.AvatarKey);
        community.Members.Add(new CommunityMember(community.Id, userId, CommunityMemberRole.Owner));

        db.Communities.Add(community);
        await db.SaveChangesAsync(cancellationToken);

        var response = new CommunityResponse(
            community.Id,
            community.Name,
            community.Category,
            community.Description,
            community.AvatarUrl,
            community.AvatarKey,
            community.CreatedByUserId,
            community.Status,
            1,
            0,
            true,
            CommunityMemberRole.Owner,
            community.CreatedAt);

        return ApiResults.Created(context, $"/api/v1/communities/{community.Id}", response,
            "Community created successfully.");
    }

    private static IResult GetAvatarLibrary(HttpContext context)
    {
        var avatars = new[]
        {
            new CommunityAvatarPresetResponse("faith", "Faith", "/assets/community-avatars/faith.png"),
            new CommunityAvatarPresetResponse("movies", "Movies", "/assets/community-avatars/movies.png"),
            new CommunityAvatarPresetResponse("music", "Music", "/assets/community-avatars/music.png"),
            new CommunityAvatarPresetResponse("fitness", "Fitness", "/assets/community-avatars/fitness.png"),
            new CommunityAvatarPresetResponse("books", "Books", "/assets/community-avatars/books.png"),
            new CommunityAvatarPresetResponse("travel", "Travel", "/assets/community-avatars/travel.png")
        };

        return ApiResults.Ok(context, avatars, "Community avatar library retrieved successfully.");
    }

    private static async Task<IResult> UpdateAvatar(Guid id, UpdateCommunityAvatarRequest request,
        HttpContext context, IMirageDbContext db, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AvatarUrl) && string.IsNullOrWhiteSpace(request.AvatarKey))
            return EndpointHelpers.ValidationProblem(context,
                ("avatar", "AvatarUrl or AvatarKey is required."));

        var userId = context.User.GetUserId();
        var role = await GetActiveMemberRoleAsync(id, userId, db, cancellationToken);
        if (role is null) return EndpointHelpers.Forbidden(context);
        if (role is not (CommunityMemberRole.Owner or CommunityMemberRole.Moderator))
            return EndpointHelpers.Forbidden(context);

        var community = await db.Communities
            .SingleOrDefaultAsync(x => x.Id == id && x.Status == CommunityStatus.Active, cancellationToken);
        if (community is null) return EndpointHelpers.NotFound(context, "Community was not found.");

        community.UpdateAvatar(request.AvatarUrl, request.AvatarKey);
        await db.SaveChangesAsync(cancellationToken);

        return ApiResults.Ok(context, new { community.Id, community.AvatarUrl, community.AvatarKey },
            "Community avatar updated successfully.");
    }

    private static async Task<IResult> ListMembers(Guid id, HttpContext context, IMirageDbContext db,
        int page = 1, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        var userId = context.User.GetUserId();
        var isMember = await IsActiveMemberAsync(id, userId, db, cancellationToken);
        if (!isMember) return EndpointHelpers.Forbidden(context);

        var members = await db.CommunityMembers.AsNoTracking()
            .Where(x => x.CommunityId == id && x.LeftAt == null)
            .Join(db.Profiles.AsNoTracking(), member => member.UserId, profile => profile.UserId,
                (member, profile) => new CommunityMemberResponse(
                    member.Id,
                    member.UserId,
                    profile.DisplayName,
                    profile.AvatarUrl,
                    member.Role,
                    member.CreatedAt))
            .OrderBy(x => x.Role)
            .ThenBy(x => x.DisplayName)
            .ToPagedResultAsync(page, pageSize, cancellationToken);

        return ApiResults.Ok(context, members, "Community members retrieved successfully.");
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
            .Select(post => new CommunityPostResponse(
                post.Id,
                post.CommunityId,
                post.AuthorUserId,
                db.Profiles
                    .Where(profile => profile.UserId == post.AuthorUserId)
                    .Select(profile => profile.DisplayName)
                    .SingleOrDefault() ?? "Member",
                db.Profiles
                    .Where(profile => profile.UserId == post.AuthorUserId)
                    .Select(profile => profile.AvatarUrl)
                    .SingleOrDefault(),
                post.Body,
                post.ImageUrl,
                post.Likes.Count,
                post.Comments.Count,
                post.Likes.Any(like => like.UserId == userId),
                post.CreatedAt))
            .OrderByDescending(x => x.CreatedAt)
            .ToPagedResultAsync(page, pageSize, cancellationToken);

        return ApiResults.Ok(context, posts, "Community posts retrieved successfully.");
    }

    private static async Task<IResult> CreatePost(Guid id, CreateCommunityPostRequest request,
        HttpContext context, IMirageDbContext db, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Body) && string.IsNullOrWhiteSpace(request.ImageUrl))
            return EndpointHelpers.ValidationProblem(context,
                ("post", "Post body or imageUrl is required."));

        var userId = context.User.GetUserId();
        var isMember = await IsActiveMemberAsync(id, userId, db, cancellationToken);
        if (!isMember) return EndpointHelpers.Forbidden(context);

        var post = new CommunityPost(id, userId, request.Body, request.ImageUrl);
        db.CommunityPosts.Add(post);
        await db.SaveChangesAsync(cancellationToken);

        var author = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => new { x.DisplayName, x.AvatarUrl })
            .SingleOrDefaultAsync(cancellationToken);

        var response = new CommunityPostResponse(post.Id, post.CommunityId, post.AuthorUserId,
            author?.DisplayName ?? "Member", author?.AvatarUrl, post.Body, post.ImageUrl, 0, 0, false,
            post.CreatedAt);
        return ApiResults.Created(context, $"/api/v1/communities/{id}/posts/{post.Id}", response,
            "Community post created successfully.");
    }

    private static async Task<IResult> LikePost(Guid postId, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var communityId = await db.CommunityPosts.AsNoTracking()
            .Where(x => x.Id == postId)
            .Select(x => (Guid?)x.CommunityId)
            .SingleOrDefaultAsync(cancellationToken);
        if (communityId is null) return EndpointHelpers.NotFound(context, "Community post was not found.");

        var isMember = await IsActiveMemberAsync(communityId.Value, userId, db, cancellationToken);
        if (!isMember) return EndpointHelpers.Forbidden(context);

        var exists = await db.CommunityPostLikes
            .AnyAsync(x => x.PostId == postId && x.UserId == userId, cancellationToken);
        if (!exists) db.CommunityPostLikes.Add(new CommunityPostLike(postId, userId));

        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { postId }, "Community post liked successfully.");
    }

    private static async Task<IResult> UnlikePost(Guid postId, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var like = await db.CommunityPostLikes
            .SingleOrDefaultAsync(x => x.PostId == postId && x.UserId == userId, cancellationToken);
        if (like is null) return EndpointHelpers.NotFound(context, "Community post like was not found.");

        var communityId = await db.CommunityPosts.AsNoTracking()
            .Where(x => x.Id == postId)
            .Select(x => x.CommunityId)
            .SingleAsync(cancellationToken);
        var isMember = await IsActiveMemberAsync(communityId, userId, db, cancellationToken);
        if (!isMember) return EndpointHelpers.Forbidden(context);

        db.CommunityPostLikes.Remove(like);
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { postId }, "Community post unliked successfully.");
    }

    private static async Task<IResult> ListComments(Guid postId, HttpContext context, IMirageDbContext db,
        int page = 1, int pageSize = 100, CancellationToken cancellationToken = default)
    {
        var userId = context.User.GetUserId();
        var communityId = await db.CommunityPosts.AsNoTracking()
            .Where(x => x.Id == postId)
            .Select(x => (Guid?)x.CommunityId)
            .SingleOrDefaultAsync(cancellationToken);
        if (communityId is null) return EndpointHelpers.NotFound(context, "Community post was not found.");

        var isMember = await IsActiveMemberAsync(communityId.Value, userId, db, cancellationToken);
        if (!isMember) return EndpointHelpers.Forbidden(context);

        var comments = await db.CommunityPostComments.AsNoTracking()
            .Where(x => x.PostId == postId)
            .Select(comment => new CommunityPostCommentResponse(
                comment.Id,
                comment.PostId,
                comment.AuthorUserId,
                db.Profiles
                    .Where(profile => profile.UserId == comment.AuthorUserId)
                    .Select(profile => profile.DisplayName)
                    .SingleOrDefault() ?? "Member",
                db.Profiles
                    .Where(profile => profile.UserId == comment.AuthorUserId)
                    .Select(profile => profile.AvatarUrl)
                    .SingleOrDefault(),
                comment.ParentCommentId,
                comment.Body,
                comment.CreatedAt))
            .OrderBy(x => x.CreatedAt)
            .ToPagedResultAsync(page, pageSize, cancellationToken);

        return ApiResults.Ok(context, comments, "Community post comments retrieved successfully.");
    }

    private static async Task<IResult> CreateComment(Guid postId, CreateCommunityPostCommentRequest request,
        HttpContext context, IMirageDbContext db, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Body))
            return EndpointHelpers.ValidationProblem(context, ("body", "Comment body is required."));

        var userId = context.User.GetUserId();
        var communityId = await db.CommunityPosts.AsNoTracking()
            .Where(x => x.Id == postId)
            .Select(x => (Guid?)x.CommunityId)
            .SingleOrDefaultAsync(cancellationToken);
        if (communityId is null) return EndpointHelpers.NotFound(context, "Community post was not found.");

        var isMember = await IsActiveMemberAsync(communityId.Value, userId, db, cancellationToken);
        if (!isMember) return EndpointHelpers.Forbidden(context);

        if (request.ParentCommentId is not null)
        {
            var parentExists = await db.CommunityPostComments.AsNoTracking()
                .AnyAsync(x => x.Id == request.ParentCommentId && x.PostId == postId, cancellationToken);
            if (!parentExists) return EndpointHelpers.NotFound(context, "Parent comment was not found.");
        }

        var comment = new CommunityPostComment(postId, userId, request.Body, request.ParentCommentId);
        db.CommunityPostComments.Add(comment);
        await db.SaveChangesAsync(cancellationToken);

        var author = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => new { x.DisplayName, x.AvatarUrl })
            .SingleOrDefaultAsync(cancellationToken);

        var response = new CommunityPostCommentResponse(comment.Id, comment.PostId, comment.AuthorUserId,
            author?.DisplayName ?? "Member", author?.AvatarUrl, comment.ParentCommentId, comment.Body,
            comment.CreatedAt);
        return ApiResults.Created(context, $"/api/v1/communities/posts/{postId}/comments/{comment.Id}",
            response, "Community post comment created successfully.");
    }

    private static Task<bool> IsActiveMemberAsync(Guid communityId, Guid userId, IMirageDbContext db,
        CancellationToken cancellationToken) =>
        db.CommunityMembers.AsNoTracking().AnyAsync(
            x => x.CommunityId == communityId && x.UserId == userId && x.LeftAt == null &&
                 x.Community.Status == CommunityStatus.Active,
            cancellationToken);

    private static Task<CommunityMemberRole?> GetActiveMemberRoleAsync(Guid communityId, Guid userId,
        IMirageDbContext db, CancellationToken cancellationToken) =>
        db.CommunityMembers.AsNoTracking()
            .Where(x => x.CommunityId == communityId && x.UserId == userId && x.LeftAt == null &&
                        x.Community.Status == CommunityStatus.Active)
            .Select(x => (CommunityMemberRole?)x.Role)
            .SingleOrDefaultAsync(cancellationToken);
}
