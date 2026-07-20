using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Security;
using Mirage.Api.Services;
using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;
using Mirage.Infrastructure.Identity;

namespace Mirage.Api.Endpoints;

internal static class CommunityEndpoints
{
    private static readonly TimeSpan CommentEditWindow = TimeSpan.FromMinutes(15);

    public static RouteGroupBuilder MapCommunityEndpoints(this RouteGroupBuilder api)
    {
        var communities = api.MapGroup("/communities").WithTags("Communities").RequireAuthorization();
        communities.MapGet("/avatar-library", GetAvatarLibrary);
        communities.MapGet("/", List);
        communities.MapGet("/{id:guid}", GetById);
        communities.MapPost("/", Create);
        communities.MapPatch("/{id:guid}/avatar", UpdateAvatar);
        communities.MapGet("/{id:guid}/members", ListMembers);
        communities.MapPatch("/{id:guid}/members/{userId:guid}/role", UpdateMemberRole);
        communities.MapPost("/{id:guid}/join", Join);
        communities.MapDelete("/{id:guid}/membership", Leave);
        communities.MapPost("/{id:guid}/invites", InviteMember);
        communities.MapGet("/{id:guid}/posts", ListPosts);
        communities.MapPost("/{id:guid}/posts", CreatePost);
        communities.MapDelete("/posts/{postId:guid}", DeletePost);
        communities.MapPost("/posts/{postId:guid}/likes", LikePost);
        communities.MapDelete("/posts/{postId:guid}/likes", UnlikePost);
        communities.MapPost("/posts/{postId:guid}/votes", CastPostVote);
        communities.MapDelete("/posts/{postId:guid}/votes", RemovePostVote);
        communities.MapPatch("/posts/{postId:guid}/unhide", UnhidePost);
        communities.MapGet("/posts/{postId:guid}/comments", ListComments);
        communities.MapPost("/posts/{postId:guid}/comments", CreateComment);
        communities.MapPatch("/comments/{commentId:guid}", EditComment);
        communities.MapDelete("/comments/{commentId:guid}", DeleteComment);
        communities.MapPost("/comments/{commentId:guid}/likes", LikeComment);
        communities.MapDelete("/comments/{commentId:guid}/likes", UnlikeComment);
        communities.MapPost("/comments/{commentId:guid}/votes", CastCommentVote);
        communities.MapDelete("/comments/{commentId:guid}/votes", RemoveCommentVote);
        communities.MapPatch("/comments/{commentId:guid}/unhide", UnhideComment);
        communities.MapGet("/comments/{commentId:guid}/location", GetCommentLocation);
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
            .OrderByDescending(x => x.Members.Any(m => m.UserId == userId && m.LeftAt == null))
            .ThenByDescending(x => x.CreatedAt)
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
            new CommunityAvatarPresetResponse("faith", "Faith", AvatarDataUri("Faith", "#6D5DF7", "#25C2A0")),
            new CommunityAvatarPresetResponse("movies", "Movies", AvatarDataUri("Movies", "#111827", "#F59E0B")),
            new CommunityAvatarPresetResponse("music", "Music", AvatarDataUri("Music", "#7C3AED", "#EC4899")),
            new CommunityAvatarPresetResponse("fitness", "Fitness", AvatarDataUri("Fitness", "#059669", "#84CC16")),
            new CommunityAvatarPresetResponse("books", "Books", AvatarDataUri("Books", "#2563EB", "#06B6D4")),
            new CommunityAvatarPresetResponse("travel", "Travel", AvatarDataUri("Travel", "#EA580C", "#14B8A6"))
        };

        return ApiResults.Ok(context, avatars, "Community avatar library retrieved successfully.");
    }

    private static string AvatarDataUri(string label, string color1, string color2)
    {
        var initials = string.Concat(label.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(2)
            .Select(part => char.ToUpperInvariant(part[0])));
        var svg = $$"""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 160 160">
              <defs>
                <linearGradient id="g" x1="0" y1="0" x2="1" y2="1">
                  <stop offset="0" stop-color="{{color1}}"/>
                  <stop offset="1" stop-color="{{color2}}"/>
                </linearGradient>
              </defs>
              <rect width="160" height="160" rx="32" fill="url(#g)"/>
              <circle cx="122" cy="34" r="24" fill="rgba(255,255,255,.18)"/>
              <text x="80" y="96" text-anchor="middle" font-family="Inter, Arial, sans-serif"
                    font-size="46" font-weight="800" fill="white">{{initials}}</text>
            </svg>
            """;
        return $"data:image/svg+xml;utf8,{Uri.EscapeDataString(svg)}";
    }

    private static async Task<IResult> UpdateAvatar(Guid id, UpdateCommunityAvatarRequest request,
        HttpContext context, IMirageDbContext db, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AvatarUrl) && string.IsNullOrWhiteSpace(request.AvatarKey))
            return EndpointHelpers.ValidationProblem(context,
                ("avatar", "AvatarUrl or AvatarKey is required."));

        var userId = context.User.GetUserId();
        var role = await GetActiveMemberRoleAsync(id, userId, db, cancellationToken);
        var isCommunityAdmin = role is CommunityMemberRole.Owner or CommunityMemberRole.Moderator;
        if (!isCommunityAdmin && !context.User.IsInRole(MirageRoles.PlatformAdmin))
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
        UserManager<ApplicationUser> userManager, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        var userId = context.User.GetUserId();
        var isMember = await IsActiveMemberAsync(id, userId, db, cancellationToken);
        if (!isMember) return EndpointHelpers.Forbidden(context);

        var paged = await db.CommunityMembers.AsNoTracking()
            .Where(x => x.CommunityId == id && x.LeftAt == null && userManager.Users.Any(u => u.Id == x.UserId && u.IsActive))
            .Join(db.Profiles.AsNoTracking(), member => member.UserId, profile => profile.UserId,
                (member, profile) => new
                {
                    member.Id,
                    member.UserId,
                    profile.DisplayName,
                    profile.AvatarUrl,
                    member.Role,
                    member.CreatedAt
                })
            .OrderBy(x => x.Role)
            .ThenBy(x => x.DisplayName)
            .ToPagedResultAsync(page, pageSize, cancellationToken);

        var badges = await db.GetOrgBadgesAsync(paged.Items.Select(x => x.UserId), cancellationToken);
        var members = new Mirage.Application.Common.PagedResult<CommunityMemberResponse>(
            paged.Items.Select(x => new CommunityMemberResponse(x.Id, x.UserId, x.DisplayName, x.AvatarUrl, x.Role,
                x.CreatedAt, badges.GetValueOrDefault(x.UserId)?.LogoUrl, badges.GetValueOrDefault(x.UserId)?.OrganisationName)).ToList(),
            paged.Page, paged.PageSize, paged.TotalCount);

        return ApiResults.Ok(context, members, "Community members retrieved successfully.");
    }

    // Owners/Moderators can promote another active member to Moderator, or an Owner can demote a
    // Moderator back to Member. Nobody can reassign the Owner role itself — ownership transfer isn't
    // supported yet, and there's always exactly one Owner (seeded at community creation).
    private static async Task<IResult> UpdateMemberRole(Guid id, Guid userId, UpdateCommunityMemberRoleRequest request,
        HttpContext context, IMirageDbContext db, CancellationToken cancellationToken)
    {
        var actorId = context.User.GetUserId();
        var actorRole = await GetActiveMemberRoleAsync(id, actorId, db, cancellationToken);
        if (actorRole is not (CommunityMemberRole.Owner or CommunityMemberRole.Moderator))
            return EndpointHelpers.Forbidden(context);
        if (request.Role == CommunityMemberRole.Owner)
            return EndpointHelpers.ValidationProblem(context, ("role", "Ownership cannot be reassigned this way."));

        var member = await db.CommunityMembers.SingleOrDefaultAsync(
            x => x.CommunityId == id && x.UserId == userId && x.LeftAt == null, cancellationToken);
        if (member is null) return EndpointHelpers.NotFound(context, "Community member was not found.");
        if (member.Role == CommunityMemberRole.Owner)
            return EndpointHelpers.Conflict(context, "The community owner's role cannot be changed.");
        if (actorRole == CommunityMemberRole.Moderator && request.Role == CommunityMemberRole.Moderator)
            return EndpointHelpers.Forbidden(context);

        member.ChangeRole(request.Role);
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { member.Id, member.UserId, member.Role },
            "Community member role updated successfully.");
    }

    private static async Task<IResult> Join(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var community = await db.Communities.AsNoTracking()
            .Where(x => x.Id == id && x.Status == CommunityStatus.Active)
            .Select(x => new { x.OrganisationId, x.Category })
            .SingleOrDefaultAsync(cancellationToken);
        if (community is null) return EndpointHelpers.NotFound(context, "Community was not found.");

        // Church-auto-managed communities aren't open to self-service joining the way regular
        // communities are: General requires an approved OrganisationMember row for that same
        // church, and Married additionally requires the joiner's own RelationshipStatus to be
        // Married (the "click Join" path here, not the automatic add in
        // ChurchCommunityService.JoinChurchCommunityAsync triggered by org approval/couple approval).
        if (community.OrganisationId is Guid organisationId)
        {
            var isApprovedChurchMember = await db.OrganisationMembers.AsNoTracking().AnyAsync(
                x => x.OrganisationId == organisationId && x.UserId == userId &&
                     x.Status == OrganisationMemberStatus.Approved, cancellationToken);
            if (!isApprovedChurchMember)
                return EndpointHelpers.Forbidden(context,
                    "You can only join this church's community once your membership with that church is approved.");

            if (community.Category == Community.ChurchMarriedCategory)
            {
                var relationshipStatus = await db.Profiles.AsNoTracking()
                    .Where(x => x.UserId == userId)
                    .Select(x => x.RelationshipStatus)
                    .SingleOrDefaultAsync(cancellationToken);
                if (relationshipStatus != RelationshipStatus.Married)
                    return EndpointHelpers.Forbidden(context,
                        "Only married members of this church can join its married community.");
            }
        }

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

    private static async Task<IResult> InviteMember(Guid id, InviteToGatheringRequest request, HttpContext context,
        IMirageDbContext db, UserManager<ApplicationUser> userManager, NotificationService notifications,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.EmailOrUsername))
            return EndpointHelpers.ValidationProblem(context, ("emailOrUsername", "Email or username is required."));

        var userId = context.User.GetUserId();
        var isMember = await IsActiveMemberAsync(id, userId, db, cancellationToken);
        if (!isMember) return EndpointHelpers.Forbidden(context);

        var community = await db.Communities.AsNoTracking()
            .Where(x => x.Id == id && x.Status == CommunityStatus.Active)
            .Select(x => new { x.Name })
            .SingleOrDefaultAsync(cancellationToken);
        if (community is null) return EndpointHelpers.NotFound(context, "Community was not found.");

        var invitee = await userManager.FindByEmailOrUsernameAsync(request.EmailOrUsername);
        if (invitee is null)
            return EndpointHelpers.NotFound(context, "No user was found with that email or username.");
        if (invitee.Id == userId)
            return EndpointHelpers.ValidationProblem(context, ("emailOrUsername", "You cannot invite yourself."));

        var alreadyMember = await db.CommunityMembers.AnyAsync(
            x => x.CommunityId == id && x.UserId == invitee.Id && x.LeftAt == null, cancellationToken);
        if (alreadyMember) return EndpointHelpers.Conflict(context, "This user is already a member of the community.");

        var hasPendingInvite = await db.GatheringInvites.AnyAsync(
            x => x.Kind == GatheringInviteKind.Community && x.TargetId == id && x.InviteeUserId == invitee.Id &&
                 x.Status == GatheringInviteStatus.Pending, cancellationToken);
        if (hasPendingInvite) return EndpointHelpers.Conflict(context, "An invite is already pending for this user.");

        var invite = new GatheringInvite(GatheringInviteKind.Community, id, userId, invitee.Id);
        db.GatheringInvites.Add(invite);
        await db.SaveChangesAsync(cancellationToken);

        var inviterName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken);
        await notifications.NotifyAsync(invitee.Id, NotificationType.GatheringInviteReceived,
            "Community invite", $"{inviterName ?? "Someone"} invited you to join \"{community.Name}\".",
            invite.Id, "GatheringInvite", cancellationToken);

        return ApiResults.Created(context, $"/api/v1/invites/{invite.Id}", new { invite.Id },
            "Invite sent successfully.");
    }

    private static async Task<IResult> ListPosts(Guid id, HttpContext context, IMirageDbContext db,
        int page = 1, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        var userId = context.User.GetUserId();
        var role = await GetActiveMemberRoleAsync(id, userId, db, cancellationToken);
        if (role is null) return EndpointHelpers.Forbidden(context);
        var canSeeHidden = role is CommunityMemberRole.Owner or CommunityMemberRole.Moderator ||
            context.User.IsInRole(MirageRoles.PlatformAdmin);

        var query = db.CommunityPosts.AsNoTracking().Where(x => x.CommunityId == id);
        if (!canSeeHidden) query = query.Where(x => !x.IsHidden);

        var paged = await query
            .OrderByDescending(post => post.CreatedAt)
            .Select(post => new
            {
                post.Id,
                post.CommunityId,
                post.AuthorUserId,
                AuthorName = db.Profiles
                    .Where(profile => profile.UserId == post.AuthorUserId)
                    .Select(profile => profile.DisplayName)
                    .SingleOrDefault() ?? "Member",
                AuthorAvatarUrl = db.Profiles
                    .Where(profile => profile.UserId == post.AuthorUserId)
                    .Select(profile => profile.AvatarUrl)
                    .SingleOrDefault(),
                post.Body,
                post.ImageUrl,
                LikeCount = post.Likes.Count,
                CommentCount = post.Comments.Count,
                LikedByMe = post.Likes.Any(like => like.UserId == userId),
                UpvoteCount = post.Votes.Count(v => v.Value > 0),
                DownvoteCount = post.Votes.Count(v => v.Value < 0),
                MyVote = post.Votes.Where(v => v.UserId == userId).Select(v => (sbyte?)v.Value).FirstOrDefault(),
                post.IsHidden,
                post.CreatedAt
            })
            .ToPagedResultAsync(page, pageSize, cancellationToken);

        var badges = await db.GetOrgBadgesAsync(paged.Items.Select(x => x.AuthorUserId), cancellationToken);
        var posts = new Mirage.Application.Common.PagedResult<CommunityPostResponse>(
            paged.Items.Select(x => new CommunityPostResponse(x.Id, x.CommunityId, x.AuthorUserId, x.AuthorName,
                x.AuthorAvatarUrl, x.Body, x.ImageUrl, x.LikeCount, x.CommentCount, x.LikedByMe, x.CreatedAt,
                badges.GetValueOrDefault(x.AuthorUserId)?.LogoUrl, badges.GetValueOrDefault(x.AuthorUserId)?.OrganisationName,
                x.UpvoteCount, x.DownvoteCount, x.MyVote,
                CommunityVoteScoring.ColorFor(x.UpvoteCount, x.DownvoteCount), x.IsHidden)).ToList(),
            paged.Page, paged.PageSize, paged.TotalCount);

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

        var authorBadge = await db.GetOrgBadgeAsync(userId, cancellationToken);
        var response = new CommunityPostResponse(post.Id, post.CommunityId, post.AuthorUserId,
            author?.DisplayName ?? "Member", author?.AvatarUrl, post.Body, post.ImageUrl, 0, 0, false,
            post.CreatedAt, authorBadge?.LogoUrl, authorBadge?.OrganisationName);
        return ApiResults.Created(context, $"/api/v1/communities/{id}/posts/{post.Id}", response,
            "Community post created successfully.");
    }

    private static async Task<IResult> DeletePost(Guid postId, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var post = await db.CommunityPosts.SingleOrDefaultAsync(x => x.Id == postId, cancellationToken);
        if (post is null) return EndpointHelpers.NotFound(context, "Community post was not found.");
        if (post.AuthorUserId != userId) return EndpointHelpers.Forbidden(context);

        db.CommunityPosts.Remove(post);
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { postId }, "Community post deleted successfully.");
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

    private static async Task<IResult> CastPostVote(Guid postId, CastVoteRequest request, HttpContext context,
        IMirageDbContext db, CancellationToken cancellationToken)
    {
        if (request.Value != 1 && request.Value != -1)
            return EndpointHelpers.ValidationProblem(context, ("value", "Vote value must be 1 (up) or -1 (down)."));

        var userId = context.User.GetUserId();
        var post = await db.CommunityPosts.SingleOrDefaultAsync(x => x.Id == postId, cancellationToken);
        if (post is null) return EndpointHelpers.NotFound(context, "Community post was not found.");

        var isMember = await IsActiveMemberAsync(post.CommunityId, userId, db, cancellationToken);
        if (!isMember) return EndpointHelpers.Forbidden(context);

        var vote = await db.CommunityPostVotes.SingleOrDefaultAsync(
            x => x.PostId == postId && x.UserId == userId, cancellationToken);
        if (vote is null) db.CommunityPostVotes.Add(new CommunityPostVote(postId, userId, request.Value));
        else vote.ChangeValue(request.Value);
        await db.SaveChangesAsync(cancellationToken);

        var downvotes = await db.CommunityPostVotes.CountAsync(x => x.PostId == postId && x.Value < 0, cancellationToken);
        if (CommunityVoteScoring.ShouldHide(downvotes))
        {
            post.Hide();
            await db.SaveChangesAsync(cancellationToken);
        }

        var upvotes = await db.CommunityPostVotes.CountAsync(x => x.PostId == postId && x.Value > 0, cancellationToken);
        return ApiResults.Ok(context,
            new { postId, upvoteCount = upvotes, downvoteCount = downvotes, myVote = request.Value,
                voteColor = CommunityVoteScoring.ColorFor(upvotes, downvotes), isHidden = post.IsHidden },
            "Vote recorded successfully.");
    }

    private static async Task<IResult> RemovePostVote(Guid postId, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var vote = await db.CommunityPostVotes.SingleOrDefaultAsync(
            x => x.PostId == postId && x.UserId == userId, cancellationToken);
        if (vote is null) return EndpointHelpers.NotFound(context, "Vote was not found.");

        var isMember = await IsActiveMemberAsync(
            await db.CommunityPosts.AsNoTracking().Where(x => x.Id == postId).Select(x => x.CommunityId).SingleAsync(cancellationToken),
            userId, db, cancellationToken);
        if (!isMember) return EndpointHelpers.Forbidden(context);

        db.CommunityPostVotes.Remove(vote);
        await db.SaveChangesAsync(cancellationToken);

        var upvotes = await db.CommunityPostVotes.CountAsync(x => x.PostId == postId && x.Value > 0, cancellationToken);
        var downvotes = await db.CommunityPostVotes.CountAsync(x => x.PostId == postId && x.Value < 0, cancellationToken);
        return ApiResults.Ok(context,
            new { postId, upvoteCount = upvotes, downvoteCount = downvotes, voteColor = CommunityVoteScoring.ColorFor(upvotes, downvotes) },
            "Vote removed successfully.");
    }

    private static async Task<IResult> UnhidePost(Guid postId, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var post = await db.CommunityPosts.SingleOrDefaultAsync(x => x.Id == postId, cancellationToken);
        if (post is null) return EndpointHelpers.NotFound(context, "Community post was not found.");

        var canModerate = await CanModerateCommunityAsync(post.CommunityId, context, db, cancellationToken);
        if (!canModerate) return EndpointHelpers.Forbidden(context);
        if (!post.IsHidden) return EndpointHelpers.Conflict(context, "This post is not hidden.");

        post.Unhide();
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { postId, post.IsHidden }, "Post restored successfully.");
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

        var role = await GetActiveMemberRoleAsync(communityId.Value, userId, db, cancellationToken);
        if (role is null) return EndpointHelpers.Forbidden(context);
        var canSeeHidden = role is CommunityMemberRole.Owner or CommunityMemberRole.Moderator ||
            context.User.IsInRole(MirageRoles.PlatformAdmin);

        var query = db.CommunityPostComments.AsNoTracking().Where(x => x.PostId == postId);
        if (!canSeeHidden) query = query.Where(x => !x.IsHidden);

        var paged = await query
            .OrderBy(comment => comment.CreatedAt)
            .Select(comment => new
            {
                comment.Id,
                comment.PostId,
                comment.AuthorUserId,
                AuthorName = db.Profiles
                    .Where(profile => profile.UserId == comment.AuthorUserId)
                    .Select(profile => profile.DisplayName)
                    .SingleOrDefault() ?? "Member",
                AuthorAvatarUrl = db.Profiles
                    .Where(profile => profile.UserId == comment.AuthorUserId)
                    .Select(profile => profile.AvatarUrl)
                    .SingleOrDefault(),
                comment.ParentCommentId,
                comment.Body,
                comment.MentionedUserIds,
                LikeCount = comment.Likes.Count,
                LikedByMe = comment.Likes.Any(like => like.UserId == userId),
                comment.IsEdited,
                comment.IsDeleted,
                UpvoteCount = comment.Votes.Count(v => v.Value > 0),
                DownvoteCount = comment.Votes.Count(v => v.Value < 0),
                MyVote = comment.Votes.Where(v => v.UserId == userId).Select(v => (sbyte?)v.Value).FirstOrDefault(),
                comment.IsHidden,
                comment.CreatedAt
            })
            .ToPagedResultAsync(page, pageSize, cancellationToken);

        var badges = await db.GetOrgBadgesAsync(paged.Items.Select(x => x.AuthorUserId), cancellationToken);
        var comments = new Mirage.Application.Common.PagedResult<CommunityPostCommentResponse>(
            paged.Items.Select(x => new CommunityPostCommentResponse(x.Id, x.PostId, x.AuthorUserId, x.AuthorName,
                x.AuthorAvatarUrl, x.ParentCommentId, x.Body, x.MentionedUserIds, x.LikeCount, x.LikedByMe, x.IsEdited,
                x.IsDeleted, x.CreatedAt, badges.GetValueOrDefault(x.AuthorUserId)?.LogoUrl,
                badges.GetValueOrDefault(x.AuthorUserId)?.OrganisationName,
                x.UpvoteCount, x.DownvoteCount, x.MyVote,
                CommunityVoteScoring.ColorFor(x.UpvoteCount, x.DownvoteCount), x.IsHidden)).ToList(),
            paged.Page, paged.PageSize, paged.TotalCount);

        return ApiResults.Ok(context, comments, "Community post comments retrieved successfully.");
    }

    private static async Task<IResult> CreateComment(Guid postId, CreateCommunityPostCommentRequest request,
        HttpContext context, IMirageDbContext db, NotificationService notifications, CancellationToken cancellationToken)
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

        var mentionedUserIds = Array.Empty<Guid>();
        if (request.MentionedUserIds is { Length: > 0 })
        {
            mentionedUserIds = await db.CommunityMembers.AsNoTracking()
                .Where(x => x.CommunityId == communityId.Value && x.LeftAt == null &&
                            request.MentionedUserIds.Contains(x.UserId))
                .Select(x => x.UserId)
                .ToArrayAsync(cancellationToken);
        }

        var comment = new CommunityPostComment(postId, userId, request.Body, request.ParentCommentId, mentionedUserIds);
        db.CommunityPostComments.Add(comment);
        await db.SaveChangesAsync(cancellationToken);

        var author = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => new { x.DisplayName, x.AvatarUrl })
            .SingleOrDefaultAsync(cancellationToken);

        foreach (var mentionedUserId in mentionedUserIds.Where(x => x != userId))
        {
            await notifications.NotifyAsync(mentionedUserId, NotificationType.Mention, "You were mentioned",
                $"{author?.DisplayName ?? "Someone"} mentioned you in a comment.", comment.Id,
                "CommunityPostComment", cancellationToken);
        }

        var authorBadge = await db.GetOrgBadgeAsync(userId, cancellationToken);
        var response = new CommunityPostCommentResponse(comment.Id, comment.PostId, comment.AuthorUserId,
            author?.DisplayName ?? "Member", author?.AvatarUrl, comment.ParentCommentId, comment.Body,
            comment.MentionedUserIds, 0, false, false, false, comment.CreatedAt,
            authorBadge?.LogoUrl, authorBadge?.OrganisationName);
        return ApiResults.Created(context, $"/api/v1/communities/posts/{postId}/comments/{comment.Id}",
            response, "Community post comment created successfully.");
    }

    private static async Task<IResult> EditComment(Guid commentId, UpdateCommunityPostCommentRequest request,
        HttpContext context, IMirageDbContext db, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Body))
            return EndpointHelpers.ValidationProblem(context, ("body", "Comment body is required."));

        var userId = context.User.GetUserId();
        var comment = await db.CommunityPostComments.SingleOrDefaultAsync(x => x.Id == commentId, cancellationToken);
        if (comment is null) return EndpointHelpers.NotFound(context, "Comment was not found.");
        if (comment.AuthorUserId != userId) return EndpointHelpers.Forbidden(context);
        if (comment.IsDeleted) return EndpointHelpers.Conflict(context, "This comment has been deleted.");
        if (DateTimeOffset.UtcNow - comment.CreatedAt > CommentEditWindow)
            return EndpointHelpers.Conflict(context, "Comments can only be edited within 15 minutes of posting.");

        comment.Edit(request.Body);
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { comment.Id, comment.Body, comment.IsEdited },
            "Comment updated successfully.");
    }

    private static async Task<IResult> DeleteComment(Guid commentId, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var comment = await db.CommunityPostComments.SingleOrDefaultAsync(x => x.Id == commentId, cancellationToken);
        if (comment is null) return EndpointHelpers.NotFound(context, "Comment was not found.");
        if (comment.AuthorUserId != userId) return EndpointHelpers.Forbidden(context);
        if (comment.IsDeleted) return EndpointHelpers.Conflict(context, "This comment has already been deleted.");

        comment.SoftDelete();
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { comment.Id }, "Comment deleted successfully.");
    }

    private static async Task<IResult> LikeComment(Guid commentId, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var communityId = await db.CommunityPostComments.AsNoTracking()
            .Where(x => x.Id == commentId)
            .Select(x => (Guid?)x.Post.CommunityId)
            .SingleOrDefaultAsync(cancellationToken);
        if (communityId is null) return EndpointHelpers.NotFound(context, "Comment was not found.");

        var isMember = await IsActiveMemberAsync(communityId.Value, userId, db, cancellationToken);
        if (!isMember) return EndpointHelpers.Forbidden(context);

        var exists = await db.CommunityPostCommentLikes
            .AnyAsync(x => x.CommentId == commentId && x.UserId == userId, cancellationToken);
        if (!exists) db.CommunityPostCommentLikes.Add(new CommunityPostCommentLike(commentId, userId));

        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { commentId }, "Comment liked successfully.");
    }

    private static async Task<IResult> UnlikeComment(Guid commentId, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var like = await db.CommunityPostCommentLikes
            .SingleOrDefaultAsync(x => x.CommentId == commentId && x.UserId == userId, cancellationToken);
        if (like is null) return EndpointHelpers.NotFound(context, "Comment like was not found.");

        var communityId = await db.CommunityPostComments.AsNoTracking()
            .Where(x => x.Id == commentId)
            .Select(x => x.Post.CommunityId)
            .SingleAsync(cancellationToken);
        var isMember = await IsActiveMemberAsync(communityId, userId, db, cancellationToken);
        if (!isMember) return EndpointHelpers.Forbidden(context);

        db.CommunityPostCommentLikes.Remove(like);
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { commentId }, "Comment unliked successfully.");
    }

    private static async Task<IResult> CastCommentVote(Guid commentId, CastVoteRequest request, HttpContext context,
        IMirageDbContext db, CancellationToken cancellationToken)
    {
        if (request.Value != 1 && request.Value != -1)
            return EndpointHelpers.ValidationProblem(context, ("value", "Vote value must be 1 (up) or -1 (down)."));

        var userId = context.User.GetUserId();
        var comment = await db.CommunityPostComments.SingleOrDefaultAsync(x => x.Id == commentId, cancellationToken);
        if (comment is null) return EndpointHelpers.NotFound(context, "Comment was not found.");

        var communityId = await db.CommunityPostComments.AsNoTracking()
            .Where(x => x.Id == commentId).Select(x => x.Post.CommunityId).SingleAsync(cancellationToken);
        var isMember = await IsActiveMemberAsync(communityId, userId, db, cancellationToken);
        if (!isMember) return EndpointHelpers.Forbidden(context);

        var vote = await db.CommunityPostCommentVotes.SingleOrDefaultAsync(
            x => x.CommentId == commentId && x.UserId == userId, cancellationToken);
        if (vote is null) db.CommunityPostCommentVotes.Add(new CommunityPostCommentVote(commentId, userId, request.Value));
        else vote.ChangeValue(request.Value);
        await db.SaveChangesAsync(cancellationToken);

        var downvotes = await db.CommunityPostCommentVotes.CountAsync(x => x.CommentId == commentId && x.Value < 0, cancellationToken);
        if (CommunityVoteScoring.ShouldHide(downvotes))
        {
            comment.Hide();
            await db.SaveChangesAsync(cancellationToken);
        }

        var upvotes = await db.CommunityPostCommentVotes.CountAsync(x => x.CommentId == commentId && x.Value > 0, cancellationToken);
        return ApiResults.Ok(context,
            new { commentId, upvoteCount = upvotes, downvoteCount = downvotes, myVote = request.Value,
                voteColor = CommunityVoteScoring.ColorFor(upvotes, downvotes), isHidden = comment.IsHidden },
            "Vote recorded successfully.");
    }

    private static async Task<IResult> RemoveCommentVote(Guid commentId, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var vote = await db.CommunityPostCommentVotes.SingleOrDefaultAsync(
            x => x.CommentId == commentId && x.UserId == userId, cancellationToken);
        if (vote is null) return EndpointHelpers.NotFound(context, "Vote was not found.");

        var communityId = await db.CommunityPostComments.AsNoTracking()
            .Where(x => x.Id == commentId).Select(x => x.Post.CommunityId).SingleAsync(cancellationToken);
        var isMember = await IsActiveMemberAsync(communityId, userId, db, cancellationToken);
        if (!isMember) return EndpointHelpers.Forbidden(context);

        db.CommunityPostCommentVotes.Remove(vote);
        await db.SaveChangesAsync(cancellationToken);

        var upvotes = await db.CommunityPostCommentVotes.CountAsync(x => x.CommentId == commentId && x.Value > 0, cancellationToken);
        var downvotes = await db.CommunityPostCommentVotes.CountAsync(x => x.CommentId == commentId && x.Value < 0, cancellationToken);
        return ApiResults.Ok(context,
            new { commentId, upvoteCount = upvotes, downvoteCount = downvotes, voteColor = CommunityVoteScoring.ColorFor(upvotes, downvotes) },
            "Vote removed successfully.");
    }

    private static async Task<IResult> UnhideComment(Guid commentId, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var comment = await db.CommunityPostComments.SingleOrDefaultAsync(x => x.Id == commentId, cancellationToken);
        if (comment is null) return EndpointHelpers.NotFound(context, "Comment was not found.");

        var communityId = await db.CommunityPostComments.AsNoTracking()
            .Where(x => x.Id == commentId).Select(x => x.Post.CommunityId).SingleAsync(cancellationToken);
        var canModerate = await CanModerateCommunityAsync(communityId, context, db, cancellationToken);
        if (!canModerate) return EndpointHelpers.Forbidden(context);
        if (!comment.IsHidden) return EndpointHelpers.Conflict(context, "This comment is not hidden.");

        comment.Unhide();
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { commentId, comment.IsHidden }, "Comment restored successfully.");
    }

    private static async Task<IResult> GetCommentLocation(Guid commentId, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var location = await db.CommunityPostComments.AsNoTracking()
            .Where(x => x.Id == commentId)
            .Select(x => new { x.Post.CommunityId, x.PostId })
            .SingleOrDefaultAsync(cancellationToken);
        if (location is null) return EndpointHelpers.NotFound(context, "Comment was not found.");

        var isMember = await IsActiveMemberAsync(location.CommunityId, userId, db, cancellationToken);
        if (!isMember) return EndpointHelpers.Forbidden(context);

        var response = new CommunityCommentLocationResponse(location.CommunityId, location.PostId, commentId);
        return ApiResults.Ok(context, response, "Comment location retrieved successfully.");
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

    // Who may unhide vote-hidden content: that community's own Owner/Moderator, or PlatformAdmin
    // anywhere — matches the review-scope decided for the downvote auto-hide feature.
    private static async Task<bool> CanModerateCommunityAsync(Guid communityId, HttpContext context,
        IMirageDbContext db, CancellationToken cancellationToken)
    {
        if (context.User.IsInRole(MirageRoles.PlatformAdmin)) return true;
        var role = await GetActiveMemberRoleAsync(communityId, context.User.GetUserId(), db, cancellationToken);
        return role is CommunityMemberRole.Owner or CommunityMemberRole.Moderator;
    }
}
