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

internal static class DateRequestEndpoints
{
    public static RouteGroupBuilder MapDateRequestEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/date-requests").WithTags("Date Requests").RequireAuthorization();
        group.MapGet("/", List);
        group.MapGet("/mine", ListMine);
        group.MapGet("/{id:guid}", GetById);
        group.MapGet("/{id:guid}/share", GetShareInfo).AllowAnonymous();
        group.MapGet("/{id:guid}/acceptances", GetAcceptances);
        group.MapGet("/{id:guid}/comments", ListComments);
        group.MapPost("/{id:guid}/comments", CreateComment);
        group.MapDelete("/{id:guid}/comments/{commentId:guid}", DeleteComment);
        group.MapPost("/", Create);
        group.MapPost("/{id:guid}/accept", Accept);
        group.MapPost("/{id:guid}/invites", Invite);
        group.MapDelete("/{id:guid}/accept", WithdrawAcceptance);
        group.MapPost("/{id:guid}/select/{userId:guid}", Select);
        group.MapDelete("/{id:guid}", Cancel);
        group.MapPost("/{id:guid}/feedback", SubmitFeedback);
        return api;
    }

    private static async Task<IResult> List(HttpContext context, IMirageDbContext db,
        UserManager<ApplicationUser> userManager, string? location, SectionCategory? category,
        int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var userId = context.User.GetUserId();
        var query = db.DateRequests.AsNoTracking().Where(x => x.Status == DateRequestStatus.Open && x.EndsAt > DateTimeOffset.UtcNow
            && userManager.Users.Any(u => u.Id == x.RequestorUserId && u.IsActive));
        if (!string.IsNullOrWhiteSpace(location))
            query = query.Where(x => EF.Functions.ILike(x.LocationArea, $"%{location.Trim()}%"));
        if (category.HasValue)
            query = query.Where(x => x.Category == category.Value);
        var result = query.OrderBy(x => x.StartsAt)
            .Select(x => new
            {
                x.Id,
                x.RequestorUserId,
                x.Activity,
                x.StartsAt,
                x.EndsAt,
                x.LocationArea,
                x.Note,
                x.ImageUrl,
                x.Category,
                x.Capacity,
                x.ItemsToBring,
                x.Status,
                x.RequestorIsVerified,
                x.RequestorIsRecommended,
                x.SelectedUserId,
                AcceptanceCount = x.Acceptances.Count,
                SelectedCount = x.Acceptances.Count(a => a.Status == DateAcceptanceStatus.Selected),
                AcceptedByMe = x.Acceptances.Any(a => a.AcceptorUserId == userId && a.Status != DateAcceptanceStatus.Withdrawn),
                x.CreatedAt
            });
        return ApiResults.Ok(context,
            await result.ToPagedResultAsync(page, pageSize, cancellationToken),
            "Date requests retrieved successfully.");
    }

    private static async Task<IResult> Create(CreateDateRequestRequest request, HttpContext context,
        IMirageDbContext db, UserManager<ApplicationUser> userManager, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var emailForbidden = await EndpointHelpers.RequireEmailConfirmedAsync(context, userId, userManager,
            "Confirm your email address before posting date requests.");
        if (emailForbidden is not null) return emailForbidden;
        var photoForbidden = await EndpointHelpers.RequirePhotoAsync(context, userId, db, cancellationToken,
            "Add a profile photo of your face before posting date requests.");
        if (photoForbidden is not null) return photoForbidden;
        var profile = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => new { x.IsVerified, x.RelationshipStatus })
            .SingleOrDefaultAsync(cancellationToken);
        var isRecommended = await db.Recommendations.AnyAsync(x => x.RecommendedUserId == userId &&
            x.Status == RecommendationStatus.Active, cancellationToken);
        var eligible = profile?.IsVerified == true || isRecommended;
        if (request.Category != SectionCategory.Friendship && !eligible)
            return EndpointHelpers.Forbidden(context, "Only verified or recommended users can post date requests.");
        if (request.Category == SectionCategory.Dating && profile?.RelationshipStatus == RelationshipStatus.Married)
            return EndpointHelpers.Forbidden(context, "Married users can view and share dating profiles, but cannot create dating requests.");
        if (request.StartsAt <= DateTimeOffset.UtcNow || request.EndsAt <= request.StartsAt)
            return EndpointHelpers.ValidationProblem(context, ("schedule", "Provide a valid future time window."));
        if (request.Capacity < 1)
            return EndpointHelpers.ValidationProblem(context, ("capacity", "Capacity must be at least 1."));
        var entity = new DateRequest(userId, request.Activity, request.StartsAt, request.EndsAt,
            request.LocationArea, request.Note, request.Category, request.Capacity, request.ItemsToBring,
            request.ImageUrl, profile?.IsVerified == true, isRecommended);
        db.DateRequests.Add(entity);
        // No specific target yet — it's a broadcast request — so actor and target are the same user.
        await AnalyticsRecorder.RecordAsync(db, AnalyticsEventType.DateRequestCreated,
            userId, userId, entity.Id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Created(context, $"/api/v1/date-requests/{entity.Id}", entity,
            "Date request created successfully.");
    }

    private static async Task<IResult> ListMine(HttpContext context, IMirageDbContext db,
        int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var userId = context.User.GetUserId();
        var query = db.DateRequests.AsNoTracking()
            .Where(x => x.RequestorUserId == userId ||
                        x.Acceptances.Any(acceptance => acceptance.AcceptorUserId == userId))
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new
            {
                x.Id,
                x.RequestorUserId,
                x.Activity,
                x.StartsAt,
                x.EndsAt,
                x.LocationArea,
                x.Note,
                x.ImageUrl,
                x.Category,
                x.Capacity,
                x.ItemsToBring,
                x.Status,
                x.RequestorIsVerified,
                x.RequestorIsRecommended,
                x.SelectedUserId,
                AcceptanceCount = x.Acceptances.Count,
                SelectedCount = x.Acceptances.Count(a => a.Status == DateAcceptanceStatus.Selected),
                AcceptedByMe = x.Acceptances.Any(a => a.AcceptorUserId == userId && a.Status != DateAcceptanceStatus.Withdrawn),
                x.CreatedAt
            });
        return ApiResults.Ok(context,
            await query.ToPagedResultAsync(page, pageSize, cancellationToken),
            "Your date requests were retrieved successfully.");
    }

    private static async Task<IResult> GetById(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var request = await db.DateRequests.AsNoTracking()
            .Select(x => new
            {
                x.Id,
                x.RequestorUserId,
                x.Activity,
                x.StartsAt,
                x.EndsAt,
                x.LocationArea,
                x.Note,
                x.ImageUrl,
                x.Category,
                x.Capacity,
                x.ItemsToBring,
                x.Status,
                x.RequestorIsVerified,
                x.RequestorIsRecommended,
                x.SelectedUserId,
                AcceptanceCount = x.Acceptances.Count,
                SelectedCount = x.Acceptances.Count(a => a.Status == DateAcceptanceStatus.Selected),
                AcceptedByMe = x.Acceptances.Any(a => a.AcceptorUserId == userId && a.Status != DateAcceptanceStatus.Withdrawn),
                x.CreatedAt
            }).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return request is null
            ? EndpointHelpers.NotFound(context, "Date request was not found.")
            : ApiResults.Ok(context, request, "Date request retrieved successfully.");
    }

    // Unauthenticated by design — this is what link-preview crawlers (WhatsApp, etc.) hit when
    // someone shares a gathering link, so it must stay limited to already-public share content.
    private static async Task<IResult> GetShareInfo(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var share = await db.DateRequests.AsNoTracking()
            .Where(x => x.Id == id)
            .Join(db.Profiles.AsNoTracking(), request => request.RequestorUserId, profile => profile.UserId,
                (request, profile) => new DateRequestShareResponse(
                    request.Id, request.Activity, request.Note, request.ImageUrl, request.LocationArea,
                    request.StartsAt, request.Category, profile.DisplayName))
            .SingleOrDefaultAsync(cancellationToken);
        return share is null
            ? EndpointHelpers.NotFound(context, "Date request was not found.")
            : ApiResults.Ok(context, share, "Date request share info retrieved successfully.");
    }

    private static async Task<IResult> GetAcceptances(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var isOwner = await db.DateRequests.AnyAsync(x => x.Id == id && x.RequestorUserId == userId, cancellationToken);
        if (!isOwner) return EndpointHelpers.Forbidden(context);

        var acceptances = await db.DateRequestAcceptances.AsNoTracking()
            .Where(x => x.DateRequestId == id)
            .Join(db.Profiles.AsNoTracking(), acceptance => acceptance.AcceptorUserId, profile => profile.UserId,
                (acceptance, profile) => new
                {
                    acceptance.Id,
                    acceptance.AcceptorUserId,
                    acceptance.Status,
                    acceptance.CreatedAt,
                    Profile = new
                    {
                        profile.DisplayName,
                        Age = DateTime.UtcNow.Year - profile.DateOfBirth.Year,
                        profile.City,
                        profile.Denomination,
                        profile.IsVerified,
                        profile.RelationshipStatus
                    }
                })
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        return ApiResults.Ok(context, acceptances, "Date request acceptances retrieved successfully.");
    }

    // Open read, same as GetById — a shared gathering link (or a calendar item) must be viewable
    // by someone who has never interacted with it. Only posting/deleting a comment is restricted.
    private static async Task<IResult> ListComments(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var comments = await db.DateRequestComments.AsNoTracking()
            .Where(x => x.DateRequestId == id && !x.IsDeleted)
            .OrderBy(x => x.CreatedAt)
            .Join(db.Profiles.AsNoTracking(), comment => comment.AuthorUserId, profile => profile.UserId,
                (comment, profile) => new DateRequestCommentResponse(comment.Id, comment.DateRequestId,
                    comment.AuthorUserId, profile.DisplayName, profile.AvatarUrl, comment.Body, comment.CreatedAt))
            .ToListAsync(cancellationToken);
        return ApiResults.Ok(context, comments, "Comments retrieved successfully.");
    }

    // Commenting is limited to the host and anyone who has responded (and not withdrawn) —
    // the same rule AcceptedByMe already computes for the discovery feed's card state.
    private static async Task<IResult> CreateComment(Guid id, CreateDateRequestCommentRequest request,
        HttpContext context, IMirageDbContext db, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Body))
            return EndpointHelpers.ValidationProblem(context, ("body", "Comment text is required."));

        var userId = context.User.GetUserId();
        var dateRequest = await db.DateRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (dateRequest is null) return EndpointHelpers.NotFound(context, "Date request was not found.");

        var canComment = dateRequest.RequestorUserId == userId || await db.DateRequestAcceptances.AnyAsync(
            x => x.DateRequestId == id && x.AcceptorUserId == userId && x.Status != DateAcceptanceStatus.Withdrawn,
            cancellationToken);
        if (!canComment) return EndpointHelpers.Forbidden(context);

        var comment = new DateRequestComment(id, userId, request.Body);
        db.DateRequestComments.Add(comment);
        await db.SaveChangesAsync(cancellationToken);

        var author = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => new { x.DisplayName, x.AvatarUrl })
            .SingleOrDefaultAsync(cancellationToken);
        var response = new DateRequestCommentResponse(comment.Id, comment.DateRequestId, comment.AuthorUserId,
            author?.DisplayName ?? "Member", author?.AvatarUrl, comment.Body, comment.CreatedAt);
        return ApiResults.Created(context, $"/api/v1/date-requests/{id}/comments/{comment.Id}", response,
            "Comment posted successfully.");
    }

    private static async Task<IResult> DeleteComment(Guid id, Guid commentId, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var comment = await db.DateRequestComments.SingleOrDefaultAsync(
            x => x.Id == commentId && x.DateRequestId == id, cancellationToken);
        if (comment is null) return EndpointHelpers.NotFound(context, "Comment was not found.");
        if (comment.AuthorUserId != userId) return EndpointHelpers.Forbidden(context);
        if (comment.IsDeleted) return EndpointHelpers.Conflict(context, "This comment has already been deleted.");

        comment.SoftDelete();
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { comment.Id }, "Comment deleted successfully.");
    }

    private static async Task<IResult> Accept(Guid id, HttpContext context, IMirageDbContext db,
        NotificationService notifications, UserManager<ApplicationUser> userManager, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var emailForbidden = await EndpointHelpers.RequireEmailConfirmedAsync(context, userId, userManager,
            "Confirm your email address before accepting date requests.");
        if (emailForbidden is not null) return emailForbidden;
        var photoForbidden = await EndpointHelpers.RequirePhotoAsync(context, userId, db, cancellationToken,
            "Add a profile photo of your face before accepting date requests.");
        if (photoForbidden is not null) return photoForbidden;
        var request = await db.DateRequests.Include(x => x.Acceptances)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (request is null) return EndpointHelpers.NotFound(context, "Date request was not found.");
        if (request.RequestorUserId == userId || request.Status != DateRequestStatus.Open)
            return EndpointHelpers.Conflict(context, "The date request cannot be accepted.");
        if (request.Category == SectionCategory.Dating &&
            await db.Profiles.AnyAsync(x => x.UserId == userId && x.RelationshipStatus == RelationshipStatus.Married,
                cancellationToken))
            return EndpointHelpers.Forbidden(context, "Married users can view and share dating profiles, but cannot accept dating requests.");
        if (request.Acceptances.Any(x => x.AcceptorUserId == userId))
            return EndpointHelpers.Conflict(context, "Date request already accepted.");

        var acceptance = new DateRequestAcceptance(id, userId);
        request.Acceptances.Add(acceptance);
        // Friendship gatherings are open group RSVPs — accepting fills a seat immediately, no host
        // gatekeeping. Dating stays Pending: the host still reviews and picks a single match.
        if (request.Category == SectionCategory.Friendship)
        {
            try
            {
                request.Select(userId);
            }
            catch (InvalidOperationException ex)
            {
                return EndpointHelpers.Conflict(context, ex.Message);
            }
        }
        await AnalyticsRecorder.RecordAsync(db, AnalyticsEventType.DateRequestAccepted,
            userId, request.RequestorUserId, request.Id, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        var acceptorName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken);
        await notifications.NotifyAsync(request.RequestorUserId, NotificationType.DateRequestAccepted,
            "New date request response", $"{acceptorName} accepted your date request for \"{request.Activity}\".",
            request.Id, "DateRequest", cancellationToken);

        return ApiResults.Ok(context, new { dateRequestId = id }, "Date request accepted successfully.");
    }

    private static async Task<IResult> Invite(Guid id, InviteToGatheringRequest request, HttpContext context,
        IMirageDbContext db, UserManager<ApplicationUser> userManager, NotificationService notifications,
        IConfiguration configuration, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.EmailOrUsername))
            return EndpointHelpers.ValidationProblem(context, ("emailOrUsername", "Email or username is required."));

        var userId = context.User.GetUserId();
        var dateRequest = await db.DateRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (dateRequest is null) return EndpointHelpers.NotFound(context, "Date request was not found.");
        if (dateRequest.RequestorUserId != userId) return EndpointHelpers.Forbidden(context);
        if (dateRequest.Status != DateRequestStatus.Open)
            return EndpointHelpers.Conflict(context, "This gathering is no longer open for invites.");

        var invitee = await userManager.FindByEmailOrUsernameAsync(request.EmailOrUsername);
        if (invitee is null)
            return EndpointHelpers.NotFound(context, "No user was found with that email or username.");
        if (invitee.Id == userId)
            return EndpointHelpers.ValidationProblem(context, ("emailOrUsername", "You cannot invite yourself."));

        var alreadyResponded = await db.DateRequestAcceptances.AnyAsync(
            x => x.DateRequestId == id && x.AcceptorUserId == invitee.Id, cancellationToken);
        if (alreadyResponded)
            return EndpointHelpers.Conflict(context, "This user has already responded to this gathering.");

        var hasPendingInvite = await db.GatheringInvites.AnyAsync(
            x => x.Kind == GatheringInviteKind.DateRequest && x.TargetId == id && x.InviteeUserId == invitee.Id &&
                 x.Status == GatheringInviteStatus.Pending, cancellationToken);
        if (hasPendingInvite) return EndpointHelpers.Conflict(context, "An invite is already pending for this user.");

        var invite = new GatheringInvite(GatheringInviteKind.DateRequest, id, userId, invitee.Id);
        db.GatheringInvites.Add(invite);
        await db.SaveChangesAsync(cancellationToken);

        var inviterName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken);
        var appUrl = configuration["Frontend:BaseUrl"] ?? "https://mirage-ui-iota.vercel.app";
        await notifications.NotifyAsync(invitee.Id, NotificationType.GatheringInviteReceived,
            "Gathering invite", $"{inviterName ?? "Someone"} invited you to \"{dateRequest.Activity}\".",
            invite.Id, "GatheringInvite", cancellationToken, $"{appUrl}/inbox", "Review invite");

        return ApiResults.Created(context, $"/api/v1/invites/{invite.Id}", new { invite.Id },
            "Invite sent successfully.");
    }

    private static async Task<IResult> Select(Guid id, Guid userId, HttpContext context, IMirageDbContext db,
        NotificationService notifications, CancellationToken cancellationToken)
    {
        var actor = context.User.GetUserId();
        var request = await db.DateRequests.Include(x => x.Acceptances)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (request is null) return EndpointHelpers.NotFound(context, "Date request was not found.");
        if (request.RequestorUserId != actor) return EndpointHelpers.Forbidden(context);
        if (!request.Acceptances.Any(x => x.AcceptorUserId == userId))
            return EndpointHelpers.NotFound(context, "Date request acceptance was not found.");
        try
        {
            request.Select(userId);
        }
        catch (InvalidOperationException ex)
        {
            return EndpointHelpers.Conflict(context, ex.Message);
        }
        await db.SaveChangesAsync(cancellationToken);

        await notifications.NotifyAsync(userId, NotificationType.DateRequestSelected,
            "You've been selected!", $"You were selected for the date request \"{request.Activity}\".",
            request.Id, "DateRequest", cancellationToken);

        return ApiResults.Ok(context, new { dateRequestId = id, selectedUserId = userId },
            "Date request participant selected successfully.");
    }

    private static async Task<IResult> WithdrawAcceptance(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var acceptance = await db.DateRequestAcceptances.SingleOrDefaultAsync(
            x => x.DateRequestId == id && x.AcceptorUserId == userId, cancellationToken);
        if (acceptance is null) return EndpointHelpers.NotFound(context, "Date request acceptance was not found.");
        acceptance.Withdraw();
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { dateRequestId = id }, "Date request acceptance withdrawn successfully.");
    }

    private static async Task<IResult> Cancel(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var request = await db.DateRequests.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (request is null) return EndpointHelpers.NotFound(context, "Date request was not found.");
        if (request.RequestorUserId != context.User.GetUserId()) return EndpointHelpers.Forbidden(context);
        request.Cancel();
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { request.Id, request.Status }, "Date request cancelled successfully.");
    }

    private static async Task<IResult> SubmitFeedback(Guid id, SubmitDateFeedbackRequest request,
        HttpContext context, IMirageDbContext db, CancellationToken cancellationToken)
    {
        if (request.Rating is < 1 or > 5)
            return EndpointHelpers.ValidationProblem(context, ("rating", "Rating must be between 1 and 5."));

        var userId = context.User.GetUserId();
        var dateRequest = await db.DateRequests.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (dateRequest is null) return EndpointHelpers.NotFound(context, "Date request was not found.");
        if (dateRequest.Status != DateRequestStatus.Confirmed)
            return EndpointHelpers.Conflict(context, "Feedback can only be submitted for confirmed date requests.");

        var isParticipant = dateRequest.RequestorUserId == userId || dateRequest.SelectedUserId == userId;
        if (!isParticipant) return EndpointHelpers.Forbidden(context);

        if (await db.DateFeedbacks.AnyAsync(
                x => x.DateRequestId == id && x.ReviewerUserId == userId, cancellationToken))
            return EndpointHelpers.Conflict(context, "Feedback has already been submitted.");

        var reviewedUserId = dateRequest.RequestorUserId == userId
            ? dateRequest.SelectedUserId!.Value
            : dateRequest.RequestorUserId;

        if (request.ReviewedUserId != reviewedUserId)
            return EndpointHelpers.ValidationProblem(context,
                ("reviewedUserId", "The reviewed user does not match the date request participant."));

        var feedback = new DateFeedback(id, userId, reviewedUserId, request.Rating, request.Comment);
        db.DateFeedbacks.Add(feedback);
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Created(context, $"/api/v1/date-requests/{id}/feedback",
            new { feedback.Id }, "Date feedback submitted successfully.");
    }
}
