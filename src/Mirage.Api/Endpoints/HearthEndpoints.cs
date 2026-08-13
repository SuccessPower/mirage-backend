using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Security;
using Mirage.Api.Services;
using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;
using Mirage.Infrastructure.Identity;

namespace Mirage.Api.Endpoints;

// The married feed. Posts live in the community stack (see HearthService for why), so likes,
// threaded comments, votes and moderation are the existing community endpoints — what lives here
// is the cross-community feed, the couple-shaped author, Love/Amen reactions and mention search.
internal static class HearthEndpoints
{
    private const int MaxImages = 3;

    public static RouteGroupBuilder MapHearthEndpoints(this RouteGroupBuilder api)
    {
        var hearth = api.MapGroup("/hearth").WithTags("Hearth").RequireAuthorization();
        hearth.MapGet("/me", GetMe);
        hearth.MapGet("/feed", GetFeed);
        hearth.MapGet("/posts/{postId:guid}", GetPost);
        hearth.MapPost("/posts", CreatePost);
        hearth.MapDelete("/posts/{postId:guid}", DeletePost);
        hearth.MapPost("/posts/{postId:guid}/reactions", AddReaction);
        hearth.MapDelete("/posts/{postId:guid}/reactions", RemoveReaction);
        hearth.MapGet("/mentionable", ListMentionable);
        return api;
    }

    private static IResult NotMarried(HttpContext context) =>
        EndpointHelpers.Forbidden(context,
            "Hearth is for married members. Set your relationship status to married to join.");

    private static async Task<IResult> GetMe(HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var hearthId = await HearthService.EnsureMembershipAsync(db, userId, cancellationToken);
        if (hearthId is null) return NotMarried(context);

        var identity = await HearthService.ResolveIdentityAsync(db, userId, cancellationToken);
        if (identity is null) return EndpointHelpers.NotFound(context, "Your profile was not found.");

        var badge = await db.GetOrgBadgeAsync(userId, cancellationToken);
        var feedCommunityIds = await HearthService.FeedCommunityIdsAsync(db, userId, hearthId.Value,
            cancellationToken);

        var postCount = await db.CommunityPosts.AsNoTracking()
            .CountAsync(x => x.AuthorUserId == userId && feedCommunityIds.Contains(x.CommunityId),
                cancellationToken);

        var circles = await db.Communities.AsNoTracking()
            .Where(c => feedCommunityIds.Contains(c.Id))
            .Select(c => new HearthCircleResponse(c.Id, c.Name, c.AvatarUrl,
                c.Members.Count(m => m.LeftAt == null && m.Status == CommunityMemberStatus.Approved),
                c.Category == Community.HearthCategory))
            .ToListAsync(cancellationToken);
        // Hearth itself first — it is the default audience in the composer.
        circles = circles.OrderByDescending(x => x.IsHearthWide).ThenBy(x => x.Name).ToList();

        var response = new HearthMeResponse(
            ToAuthor(identity, badge),
            identity.SpouseUserId is not null,
            identity.WeddingAnniversaryDate,
            HearthService.DaysToAnniversary(identity.WeddingAnniversaryDate),
            postCount,
            circles.Count(x => !x.IsHearthWide),
            circles);

        return ApiResults.Ok(context, response, "Hearth profile retrieved successfully.");
    }

    private static async Task<IResult> GetFeed(HttpContext context, IMirageDbContext db,
        PostKind? kind = null, Guid? circleId = null, Guid? authorUserId = null,
        int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var userId = context.User.GetUserId();
        var hearthId = await HearthService.EnsureMembershipAsync(db, userId, cancellationToken);
        if (hearthId is null) return NotMarried(context);

        var feedCommunityIds = await HearthService.FeedCommunityIdsAsync(db, userId, hearthId.Value,
            cancellationToken);
        if (circleId is { } requested)
        {
            if (!feedCommunityIds.Contains(requested)) return EndpointHelpers.Forbidden(context);
            feedCommunityIds = [requested];
        }

        var query = db.CommunityPosts.AsNoTracking()
            .Where(x => feedCommunityIds.Contains(x.CommunityId) && !x.IsHidden);
        if (kind is { } postKind) query = query.Where(x => x.Kind == postKind);
        if (authorUserId is { } author) query = query.Where(x => x.AuthorUserId == author);

        var paged = await query
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new FeedRow(
                x.Id, x.CommunityId,
                db.Communities.Where(c => c.Id == x.CommunityId).Select(c => c.Name).SingleOrDefault() ?? "Hearth",
                x.AuthorUserId, x.Kind, x.Body, x.Place,
                x.ImageUrl, x.ImageUrl2, x.ImageUrl3,
                x.Likes.Count(l => l.Reaction == PostReactionKind.Love),
                x.Likes.Count(l => l.Reaction == PostReactionKind.Amen),
                x.Comments.Count(c => !c.IsDeleted && !c.IsHidden),
                x.Likes.Any(l => l.UserId == userId && l.Reaction == PostReactionKind.Love),
                x.Likes.Any(l => l.UserId == userId && l.Reaction == PostReactionKind.Amen),
                x.MentionedUserIds, x.CreatedAt))
            .ToPagedResultAsync(page, pageSize, cancellationToken);

        var posts = await ToResponsesAsync(db, paged.Items, userId, hearthId.Value, cancellationToken);
        var result = new Mirage.Application.Common.PagedResult<HearthPostResponse>(posts,
            paged.Page, paged.PageSize, paged.TotalCount);

        return ApiResults.Ok(context, result, "Hearth feed retrieved successfully.");
    }

    private static async Task<IResult> GetPost(Guid postId, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var hearthId = await HearthService.EnsureMembershipAsync(db, userId, cancellationToken);
        if (hearthId is null) return NotMarried(context);

        var feedCommunityIds = await HearthService.FeedCommunityIdsAsync(db, userId, hearthId.Value,
            cancellationToken);

        var row = await db.CommunityPosts.AsNoTracking()
            .Where(x => x.Id == postId && feedCommunityIds.Contains(x.CommunityId) && !x.IsHidden)
            .Select(x => new FeedRow(
                x.Id, x.CommunityId,
                db.Communities.Where(c => c.Id == x.CommunityId).Select(c => c.Name).SingleOrDefault() ?? "Hearth",
                x.AuthorUserId, x.Kind, x.Body, x.Place,
                x.ImageUrl, x.ImageUrl2, x.ImageUrl3,
                x.Likes.Count(l => l.Reaction == PostReactionKind.Love),
                x.Likes.Count(l => l.Reaction == PostReactionKind.Amen),
                x.Comments.Count(c => !c.IsDeleted && !c.IsHidden),
                x.Likes.Any(l => l.UserId == userId && l.Reaction == PostReactionKind.Love),
                x.Likes.Any(l => l.UserId == userId && l.Reaction == PostReactionKind.Amen),
                x.MentionedUserIds, x.CreatedAt))
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null) return EndpointHelpers.NotFound(context, "Post was not found.");

        var posts = await ToResponsesAsync(db, [row], userId, hearthId.Value, cancellationToken);
        return ApiResults.Ok(context, posts[0], "Post retrieved successfully.");
    }

    private static async Task<IResult> CreatePost(CreateHearthPostRequest request, HttpContext context,
        IMirageDbContext db, NotificationService notifications, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var hearthId = await HearthService.EnsureMembershipAsync(db, userId, cancellationToken);
        if (hearthId is null) return NotMarried(context);

        var images = request.ImageUrls?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim())
            .Distinct(StringComparer.Ordinal).ToList() ?? [];
        if (images.Count > MaxImages)
            return EndpointHelpers.ValidationProblem(context,
                ("imageUrls", $"A post can contain at most {MaxImages} images."));
        if (string.IsNullOrWhiteSpace(request.Body) && images.Count == 0)
            return EndpointHelpers.ValidationProblem(context,
                ("body", "Write something or add a photo before sharing."));

        // Audience: Hearth-wide by default, or one of the user's own married circles.
        var communityId = hearthId.Value;
        if (request.CircleId is { } circleId && circleId != hearthId.Value)
        {
            var feedCommunityIds = await HearthService.FeedCommunityIdsAsync(db, userId, hearthId.Value,
                cancellationToken);
            if (!feedCommunityIds.Contains(circleId))
                return EndpointHelpers.ValidationProblem(context,
                    ("circleId", "You can only share to a circle you belong to."));
            communityId = circleId;
        }

        // Only married members who can see this post's audience may be tagged in it — a mention is
        // a notification, so it must not be usable to reach someone outside the circle.
        var mentionedUserIds = await FilterMentionableAsync(db, communityId, request.MentionedUserIds,
            cancellationToken);

        var post = new CommunityPost(communityId, userId, request.Body, images.ElementAtOrDefault(0),
            images.ElementAtOrDefault(1), images.ElementAtOrDefault(2), mentionedUserIds,
            request.Kind, request.Place);
        db.CommunityPosts.Add(post);
        await db.SaveChangesAsync(cancellationToken);

        var identity = await HearthService.ResolveIdentityAsync(db, userId, cancellationToken);
        var authorName = identity?.CoupleName ?? "Someone";
        foreach (var mentionedUserId in mentionedUserIds.Where(x => x != userId))
        {
            await notifications.NotifyAsync(mentionedUserId, NotificationType.Mention, "You were mentioned",
                $"{authorName} mentioned you in a post.", post.Id, "HearthPost", cancellationToken);
        }

        var row = new FeedRow(post.Id, communityId,
            await db.Communities.AsNoTracking().Where(c => c.Id == communityId).Select(c => c.Name)
                .SingleOrDefaultAsync(cancellationToken) ?? HearthService.HearthName,
            userId, post.Kind, post.Body, post.Place, post.ImageUrl, post.ImageUrl2, post.ImageUrl3,
            0, 0, 0, false, false, post.MentionedUserIds, post.CreatedAt);

        var posts = await ToResponsesAsync(db, [row], userId, hearthId.Value, cancellationToken);
        return ApiResults.Created(context, $"/api/v1/hearth/posts/{post.Id}", posts[0],
            "Post shared successfully.");
    }

    private static async Task<IResult> DeletePost(Guid postId, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var post = await db.CommunityPosts.SingleOrDefaultAsync(x => x.Id == postId, cancellationToken);
        if (post is null) return EndpointHelpers.NotFound(context, "Post was not found.");
        if (post.AuthorUserId != userId && !context.User.IsInRole(MirageRoles.PlatformAdmin))
            return EndpointHelpers.Forbidden(context);

        db.CommunityPosts.Remove(post);
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { postId }, "Post deleted successfully.");
    }

    private static async Task<IResult> AddReaction(Guid postId, HearthReactionRequest request,
        HttpContext context, IMirageDbContext db, NotificationService notifications,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var post = await ReadablePostAsync(db, postId, userId, cancellationToken);
        if (post is null) return EndpointHelpers.NotFound(context, "Post was not found.");

        var exists = await db.CommunityPostLikes.AnyAsync(
            x => x.PostId == postId && x.UserId == userId && x.Reaction == request.Reaction,
            cancellationToken);
        if (!exists)
        {
            db.CommunityPostLikes.Add(new CommunityPostLike(postId, userId, request.Reaction));
            await db.SaveChangesAsync(cancellationToken);

            if (post.AuthorUserId != userId)
            {
                var identity = await HearthService.ResolveIdentityAsync(db, userId, cancellationToken);
                var verb = request.Reaction == PostReactionKind.Amen ? "said amen to" : "loved";
                await notifications.NotifyAsync(post.AuthorUserId, NotificationType.PostReaction,
                    "New reaction", $"{identity?.CoupleName ?? "Someone"} {verb} your post.", postId,
                    "HearthPost", cancellationToken);
            }
        }

        return ApiResults.Ok(context, await CountsAsync(db, postId, userId, cancellationToken),
            "Reaction added successfully.");
    }

    private static async Task<IResult> RemoveReaction(Guid postId, HttpContext context, IMirageDbContext db,
        PostReactionKind reaction = PostReactionKind.Love, CancellationToken cancellationToken = default)
    {
        var userId = context.User.GetUserId();
        var post = await ReadablePostAsync(db, postId, userId, cancellationToken);
        if (post is null) return EndpointHelpers.NotFound(context, "Post was not found.");

        var existing = await db.CommunityPostLikes.SingleOrDefaultAsync(
            x => x.PostId == postId && x.UserId == userId && x.Reaction == reaction, cancellationToken);
        if (existing is not null)
        {
            db.CommunityPostLikes.Remove(existing);
            await db.SaveChangesAsync(cancellationToken);
        }

        return ApiResults.Ok(context, await CountsAsync(db, postId, userId, cancellationToken),
            "Reaction removed successfully.");
    }

    // Who the composer can offer for @: married members of the audience this post is going to.
    private static async Task<IResult> ListMentionable(HttpContext context, IMirageDbContext db,
        string? q = null, Guid? circleId = null, int limit = 8, CancellationToken cancellationToken = default)
    {
        var userId = context.User.GetUserId();
        var hearthId = await HearthService.EnsureMembershipAsync(db, userId, cancellationToken);
        if (hearthId is null) return NotMarried(context);

        var communityId = hearthId.Value;
        if (circleId is { } requested && requested != hearthId.Value)
        {
            var feedCommunityIds = await HearthService.FeedCommunityIdsAsync(db, userId, hearthId.Value,
                cancellationToken);
            if (!feedCommunityIds.Contains(requested)) return EndpointHelpers.Forbidden(context);
            communityId = requested;
        }

        var memberIds = db.CommunityMembers.AsNoTracking()
            .Where(m => m.CommunityId == communityId && m.LeftAt == null &&
                        m.Status == CommunityMemberStatus.Approved && m.UserId != userId)
            .Select(m => m.UserId);

        var query = db.Profiles.AsNoTracking()
            .Where(p => memberIds.Contains(p.UserId) && p.RelationshipStatus == RelationshipStatus.Married);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(p => EF.Functions.ILike(p.DisplayName, $"%{term}%"));
        }

        var matches = await query
            .OrderBy(p => p.DisplayName)
            .Take(Math.Clamp(limit, 1, 20))
            .Select(p => new { p.UserId, p.DisplayName, p.AvatarUrl, p.City })
            .ToListAsync(cancellationToken);

        var identities = await HearthService.ResolveIdentitiesAsync(db,
            matches.Select(x => x.UserId), cancellationToken);

        var response = matches.Select(x => new HearthMentionableResponse(x.UserId, x.DisplayName,
            x.AvatarUrl, identities.GetValueOrDefault(x.UserId)?.CoupleName ?? x.DisplayName, x.City))
            .ToList();

        return ApiResults.Ok(context, response, "Mentionable members retrieved successfully.");
    }

    // ---- helpers -------------------------------------------------------------------------

    // A post the caller is allowed to read and react to: it must sit in one of their feed's
    // communities. Returns null otherwise so callers answer 404 rather than confirming it exists.
    private static async Task<CommunityPost?> ReadablePostAsync(IMirageDbContext db, Guid postId,
        Guid userId, CancellationToken cancellationToken)
    {
        var hearthId = await HearthService.EnsureMembershipAsync(db, userId, cancellationToken);
        if (hearthId is null) return null;

        var feedCommunityIds = await HearthService.FeedCommunityIdsAsync(db, userId, hearthId.Value,
            cancellationToken);
        return await db.CommunityPosts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == postId && !x.IsHidden &&
                                       feedCommunityIds.Contains(x.CommunityId), cancellationToken);
    }

    private static async Task<Guid[]> FilterMentionableAsync(IMirageDbContext db, Guid communityId,
        Guid[]? requestedUserIds, CancellationToken cancellationToken)
    {
        if (requestedUserIds is not { Length: > 0 }) return [];

        var memberIds = db.CommunityMembers.AsNoTracking()
            .Where(m => m.CommunityId == communityId && m.LeftAt == null &&
                        m.Status == CommunityMemberStatus.Approved &&
                        requestedUserIds.Contains(m.UserId))
            .Select(m => m.UserId);

        return await db.Profiles.AsNoTracking()
            .Where(p => memberIds.Contains(p.UserId) && p.RelationshipStatus == RelationshipStatus.Married)
            .Select(p => p.UserId)
            .ToArrayAsync(cancellationToken);
    }

    private static async Task<object> CountsAsync(IMirageDbContext db, Guid postId, Guid userId,
        CancellationToken cancellationToken)
    {
        var reactions = await db.CommunityPostLikes.AsNoTracking()
            .Where(x => x.PostId == postId)
            .Select(x => new { x.UserId, x.Reaction })
            .ToListAsync(cancellationToken);

        return new
        {
            postId,
            loveCount = reactions.Count(x => x.Reaction == PostReactionKind.Love),
            amenCount = reactions.Count(x => x.Reaction == PostReactionKind.Amen),
            lovedByMe = reactions.Any(x => x.UserId == userId && x.Reaction == PostReactionKind.Love),
            amenedByMe = reactions.Any(x => x.UserId == userId && x.Reaction == PostReactionKind.Amen)
        };
    }

    private static async Task<List<HearthPostResponse>> ToResponsesAsync(IMirageDbContext db,
        IReadOnlyList<FeedRow> rows, Guid viewerId, Guid hearthId, CancellationToken cancellationToken)
    {
        if (rows.Count == 0) return [];

        var authorIds = rows.Select(x => x.AuthorUserId).Distinct().ToArray();
        var identities = await HearthService.ResolveIdentitiesAsync(db, authorIds, cancellationToken);
        var badges = await db.GetOrgBadgesAsync(authorIds, cancellationToken);
        var mentionNames = await db.ResolveMentionsAsync(rows.SelectMany(x => x.MentionedUserIds),
            cancellationToken);

        return rows.Select(row =>
        {
            var identity = identities.GetValueOrDefault(row.AuthorUserId);
            var images = new[] { row.ImageUrl, row.ImageUrl2, row.ImageUrl3 }
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .ToList();

            return new HearthPostResponse(
                row.Id, row.CommunityId, row.CommunityName, row.CommunityId == hearthId,
                ToAuthor(identity, badges.GetValueOrDefault(row.AuthorUserId), row.AuthorUserId),
                row.Kind, row.Body, row.Place, images,
                row.LoveCount, row.AmenCount, row.CommentCount,
                row.LovedByMe, row.AmenedByMe, row.AuthorUserId == viewerId, row.CreatedAt,
                mentionNames.For(row.MentionedUserIds));
        }).ToList();
    }

    private static HearthAuthorResponse ToAuthor(HearthIdentity? identity,
        OrgBadge? badge, Guid? fallbackUserId = null) =>
        identity is null
            ? new HearthAuthorResponse(fallbackUserId ?? Guid.Empty, "Member", null, null, null, null,
                "Member", string.Empty, null, badge?.LogoUrl, badge?.OrganisationName)
            : new HearthAuthorResponse(identity.UserId, identity.DisplayName, identity.AvatarUrl,
                identity.SpouseUserId, identity.SpouseName, identity.SpouseAvatarUrl,
                identity.CoupleName, identity.City, identity.YearsMarried,
                badge?.LogoUrl, badge?.OrganisationName);

    private sealed record FeedRow(
        Guid Id,
        Guid CommunityId,
        string CommunityName,
        Guid AuthorUserId,
        PostKind Kind,
        string Body,
        string? Place,
        string? ImageUrl,
        string? ImageUrl2,
        string? ImageUrl3,
        int LoveCount,
        int AmenCount,
        int CommentCount,
        bool LovedByMe,
        bool AmenedByMe,
        Guid[] MentionedUserIds,
        DateTimeOffset CreatedAt);
}
