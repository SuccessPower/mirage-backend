using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Hubs;
using Mirage.Api.Security;
using Mirage.Api.Services;
using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;
using Mirage.Infrastructure.Persistence;

namespace Mirage.Api.Endpoints;

// A counsellor's group: posts, group chat and scheduled meetings shared with the clients they are
// working with, and with the spouses who accepted those sessions — so a counsellor can run a
// marriage course for several couples at once rather than repeating it session by session.
//
// This mirrors the mentorship group (MentorEndpoints) deliberately: the two practices should not
// behave differently for what is the same job. It stays separate from CounsellingEndpoints, whose
// every route is scoped to one session.
internal static class CounsellorGroupEndpoints
{
    public static RouteGroupBuilder MapCounsellorGroupEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/counsellors/{id:guid}").WithTags("Counselling").RequireAuthorization();
        group.MapGet("/clients", ListGroupClients);
        group.MapGet("/posts", ListPosts);
        group.MapPost("/posts", CreatePost).RequireAuthorization(MiragePolicy.Counsellor);
        group.MapGet("/group-messages", ListGroupMessages);
        group.MapPost("/group-messages", SendGroupMessage);
        group.MapGet("/group-meetings", ListMeetings);
        group.MapPost("/group-meetings", ScheduleMeeting).RequireAuthorization(MiragePolicy.Counsellor);
        group.MapGet("/group-meetings/{meetingId:guid}/video-token", GetMeetingVideoToken);
        return api;
    }

    // A session that was declined or cancelled is not a working relationship, so it does not put
    // anyone in the group. Everything else — requested, scheduled, in progress, completed — does:
    // a counsellor's group is the people they are walking with, not only today's bookings.
    private static readonly SessionStatus[] LiveStatuses =
    [
        SessionStatus.Requested, SessionStatus.Scheduled, SessionStatus.InProgress,
        SessionStatus.Completed, SessionStatus.AwaitingPayment,
    ];

    /// <summary>
    /// Everyone in the counsellor's group: each client with a live session, plus the partner who
    /// accepted it. The couple is what is being counselled, so both spouses belong here.
    /// </summary>
    private static async Task<List<Guid>> GroupMemberIdsAsync(Guid counsellorProfileId, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var rows = await db.CounsellingSessions.AsNoTracking()
            .Where(x => x.CounsellorId == counsellorProfileId && LiveStatuses.Contains(x.Status))
            .Select(x => new { x.ClientUserId, x.PartnerUserId, x.PartnerAccepted })
            .ToListAsync(cancellationToken);

        return rows
            .SelectMany(x => x.PartnerAccepted && x.PartnerUserId is { } partner
                ? new[] { x.ClientUserId, partner }
                : [x.ClientUserId])
            .Distinct()
            .ToList();
    }

    private static async Task<bool> IsGroupMemberAsync(Guid counsellorProfileId, Guid userId, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var isCounsellor = await db.Counsellors.AsNoTracking()
            .AnyAsync(x => x.Id == counsellorProfileId && x.UserId == userId, cancellationToken);
        if (isCounsellor) return true;
        return (await GroupMemberIdsAsync(counsellorProfileId, db, cancellationToken)).Contains(userId);
    }

    private static Task<bool> IsOwnerAsync(Guid counsellorProfileId, Guid userId, IMirageDbContext db,
        CancellationToken cancellationToken) =>
        db.Counsellors.AsNoTracking()
            .AnyAsync(x => x.Id == counsellorProfileId && x.UserId == userId, cancellationToken);

    private static async Task<IResult> ListGroupClients(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        if (!await IsGroupMemberAsync(id, userId, db, cancellationToken)) return EndpointHelpers.Forbidden(context);
        var isCounsellor = await IsOwnerAsync(id, userId, db, cancellationToken);

        var memberIds = await GroupMemberIdsAsync(id, db, cancellationToken);
        var profiles = await db.Profiles.AsNoTracking()
            .Where(x => memberIds.Contains(x.UserId))
            .Select(x => new { x.UserId, x.DisplayName, x.AvatarUrl })
            .ToListAsync(cancellationToken);

        // Counselling is confidential in a way mentorship is not: a client must never learn who
        // else their counsellor is seeing. Only the counsellor gets real names.
        var result = profiles.Select(x => isCounsellor || x.UserId == userId
            ? new CounsellorGroupMemberResponse(x.UserId, x.DisplayName, x.AvatarUrl)
            : new CounsellorGroupMemberResponse(x.UserId, "Fellow member", null));

        return ApiResults.Ok(context, result, "Group members retrieved successfully.");
    }

    private static async Task<IResult> ListPosts(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        if (!await IsGroupMemberAsync(id, userId, db, cancellationToken)) return EndpointHelpers.Forbidden(context);

        var posts = await db.CounsellorPosts.AsNoTracking()
            .Where(x => x.CounsellorProfileId == id)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new CounsellorPostResponse(x.Id, x.CounsellorProfileId, x.Content, x.ImageUrl, x.CreatedAt))
            .ToListAsync(cancellationToken);
        return ApiResults.Ok(context, posts, "Posts retrieved successfully.");
    }

    private static async Task<IResult> CreatePost(Guid id, CreateMentorPostRequest request, HttpContext context,
        IMirageDbContext db, NotificationService notifications, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        if (!await IsOwnerAsync(id, userId, db, cancellationToken)) return EndpointHelpers.Forbidden(context);
        if (string.IsNullOrWhiteSpace(request.Content))
            return EndpointHelpers.ValidationProblem(context, ("content", "Post content is required."));

        var post = new CounsellorPost(id, request.Content, request.ImageUrl);
        db.CounsellorPosts.Add(post);
        await db.SaveChangesAsync(cancellationToken);

        var counsellorName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken)
            ?? "Your counsellor";
        var preview = request.Content.Length > 120 ? request.Content[..120].TrimEnd() + "…" : request.Content;
        foreach (var memberId in (await GroupMemberIdsAsync(id, db, cancellationToken)).Where(x => x != userId))
            await notifications.NotifyAsync(memberId, NotificationType.MentorGroupPost,
                $"{counsellorName} posted to your group", preview, id, "CounsellorProfile", cancellationToken);

        return ApiResults.Created(context, $"/api/v1/counsellors/{id}/posts/{post.Id}", new { post.Id },
            "Post published successfully.");
    }

    private static async Task<IResult> ListGroupMessages(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        if (!await IsGroupMemberAsync(id, userId, db, cancellationToken)) return EndpointHelpers.Forbidden(context);

        var counsellorUserId = await db.Counsellors.AsNoTracking()
            .Where(x => x.Id == id).Select(x => x.UserId).SingleAsync(cancellationToken);
        var isCounsellor = counsellorUserId == userId;

        var visibility = await ChatVisibility.ForAsync(db, userId,
            new ChatSurface(ChatSurfaceKind.CounsellorGroup, id).Key, cancellationToken);
        var messages = await visibility
            .Apply(db.CounsellorGroupMessages.AsNoTracking().Where(x => x.CounsellorProfileId == id))
            .OrderBy(x => x.CreatedAt)
            // Left join, not inner: a sender whose profile row is missing must not silently erase
            // their message from the conversation.
            .Select(m => new CounsellorGroupMessageResponse(
                m.Id, m.CounsellorProfileId, m.SenderId,
                db.Profiles.Where(p => p.UserId == m.SenderId).Select(p => p.DisplayName).FirstOrDefault()
                    ?? "Member",
                m.Content, m.Type, m.AttachmentUrl, m.CreatedAt))
            .ToListAsync(cancellationToken);

        // Names are masked for everyone but the counsellor and the reader themselves — see
        // ListGroupClients for why counselling never reveals who else is in the room.
        if (!isCounsellor)
            messages = messages
                .Select(m => m.SenderId == userId || m.SenderId == counsellorUserId
                    ? m
                    : m with { SenderName = "Fellow member" })
                .ToList();

        return ApiResults.Ok(context, messages, "Group messages retrieved successfully.");
    }

    private static async Task<IResult> SendGroupMessage(Guid id, SendMentorGroupMessageRequest request,
        HttpContext context, IMirageDbContext db, IHubContext<ChatHub> hub, NotificationService notifications,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        if (!await IsGroupMemberAsync(id, userId, db, cancellationToken)) return EndpointHelpers.Forbidden(context);
        if (string.IsNullOrWhiteSpace(request.Content))
            return EndpointHelpers.ValidationProblem(context, ("content", "Message content is required."));

        var message = new CounsellorGroupMessage(id, userId, request.Content, request.Type, request.AttachmentUrl);
        db.CounsellorGroupMessages.Add(message);
        await db.SaveChangesAsync(cancellationToken);

        var counsellorUserId = await db.Counsellors.AsNoTracking()
            .Where(x => x.Id == id).Select(x => x.UserId).SingleAsync(cancellationToken);
        var senderName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken);
        var broadcastName = userId == counsellorUserId ? senderName : "Fellow member";

        await hub.Clients.Group($"counsellorgroup:{id}").SendAsync("ReceiveCounsellorGroupMessage", new
        {
            message.Id,
            CounsellorProfileId = id,
            message.SenderId,
            SenderName = broadcastName,
            message.Content,
            message.Type,
            message.AttachmentUrl,
            SentAt = message.CreatedAt
        }, cancellationToken);

        var preview = request.Content.Length > 120 ? request.Content[..120].TrimEnd() + "…" : request.Content;
        foreach (var memberId in (await GroupMemberIdsAsync(id, db, cancellationToken))
                     .Append(counsellorUserId).Distinct().Where(x => x != userId))
            await notifications.NotifyAsync(memberId, NotificationType.MentorGroupMessage,
                $"{broadcastName ?? "Someone"} messaged your group", preview, id, "CounsellorProfile",
                cancellationToken);

        return ApiResults.Created(context, $"/api/v1/counsellors/{id}/group-messages/{message.Id}",
            new { message.Id }, "Message sent successfully.");
    }

    private static async Task<IResult> ListMeetings(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        if (!await IsGroupMemberAsync(id, userId, db, cancellationToken)) return EndpointHelpers.Forbidden(context);

        var meetings = await db.CounsellorGroupMeetings.AsNoTracking()
            .Where(x => x.CounsellorProfileId == id)
            .OrderBy(x => x.ScheduledAt)
            .Select(x => new CounsellorGroupMeetingResponse(x.Id, x.CounsellorProfileId, x.ScheduledByUserId,
                x.Title, x.MeetingLink, x.ScheduledAt, x.DurationMinutes))
            .ToListAsync(cancellationToken);
        return ApiResults.Ok(context, meetings, "Meetings retrieved successfully.");
    }

    private static async Task<IResult> ScheduleMeeting(Guid id, ScheduleMentorMeetingRequest request,
        HttpContext context, IMirageDbContext db, NotificationService notifications,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        if (!await IsOwnerAsync(id, userId, db, cancellationToken)) return EndpointHelpers.Forbidden(context);
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.MeetingLink))
            return EndpointHelpers.ValidationProblem(context, ("meeting", "Title and meeting link are required."));

        var meeting = new CounsellorGroupMeeting(id, userId, request.Title, request.MeetingLink,
            request.ScheduledAt, request.DurationMinutes);
        db.CounsellorGroupMeetings.Add(meeting);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var memberId in (await GroupMemberIdsAsync(id, db, cancellationToken)).Where(x => x != userId))
            await notifications.NotifyAsync(memberId, NotificationType.SessionBooked, "New group meeting scheduled",
                $"{request.Title} was scheduled for {request.ScheduledAt:MMM d, h:mm tt}.",
                meeting.Id, "CounsellorGroupMeeting", cancellationToken);

        return ApiResults.Created(context, $"/api/v1/counsellors/{id}/group-meetings/{meeting.Id}",
            new { meeting.Id }, "Meeting scheduled successfully.");
    }

    private static async Task<IResult> GetMeetingVideoToken(Guid id, Guid meetingId, HttpContext context,
        MirageDbContext db, JitsiService jitsi, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        if (!await IsGroupMemberAsync(id, userId, db, cancellationToken)) return EndpointHelpers.Forbidden(context);
        if (!await db.CounsellorGroupMeetings.AsNoTracking()
                .AnyAsync(x => x.Id == meetingId && x.CounsellorProfileId == id, cancellationToken))
            return EndpointHelpers.NotFound(context, "Meeting was not found.");

        var counsellorUserId = await db.Counsellors.AsNoTracking()
            .Where(x => x.Id == id).Select(x => x.UserId).SingleAsync(cancellationToken);
        var displayName = await db.Profiles.AsNoTracking().Where(x => x.UserId == userId)
            .Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken) ?? "Mirage member";
        var email = await db.Users.AsNoTracking().Where(x => x.Id == userId)
            .Select(x => x.Email).SingleOrDefaultAsync(cancellationToken);

        var room = $"mirage-counsellor-{id:N}-{meetingId:N}";
        var token = jitsi.CreateToken(userId, displayName, email, room, counsellorUserId == userId);
        return ApiResults.Ok(context, new { AppId = jitsi.AppId, Room = room, Token = token },
            "Video token issued successfully.");
    }
}
