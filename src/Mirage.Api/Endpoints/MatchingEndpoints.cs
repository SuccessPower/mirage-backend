using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Hubs;
using Mirage.Api.Security;
using Mirage.Api.Services;
using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;

namespace Mirage.Api.Endpoints;

internal static class MatchingEndpoints
{
    public static RouteGroupBuilder MapMatchingEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/matching").WithTags("Matching").RequireAuthorization();
        group.MapPost("/likes", Like);
        group.MapGet("/likes/mine", GetMyLikes);
        group.MapGet("/matches", GetMatches);
        group.MapGet("/matches/{id:guid}", GetMatch);
        group.MapDelete("/matches/{id:guid}", CloseMatch);
        group.MapPost("/matches/{id:guid}/block", BlockMatch);
        group.MapPost("/matches/{id:guid}/chat-request", RequestChat);
        group.MapPost("/matches/{id:guid}/chat-request/approve", ApproveChatRequest);

        // Chat — REST fallback alongside SignalR hub
        group.MapGet("/matches/{id:guid}/messages", GetMessages);
        group.MapPost("/matches/{id:guid}/messages", SendMessage);
        group.MapPatch("/matches/{id:guid}/messages/read", MarkRead);
        return api;
    }

    private static async Task<IResult> Like(LikeProfileRequest request, HttpContext context,
        IMirageDbContext db, NotificationService notifications, CancellationToken cancellationToken)
    {
        var sourceUserId = context.User.GetUserId();
        if (sourceUserId == request.TargetUserId)
            return EndpointHelpers.ValidationProblem(context, ("targetUserId", "A user cannot like themselves."));
        if (!await db.Profiles.AnyAsync(x => x.UserId == request.TargetUserId, cancellationToken))
            return EndpointHelpers.NotFound(context, "Target profile was not found.");
        if (await db.Likes.AnyAsync(x => x.SourceUserId == sourceUserId && x.TargetUserId == request.TargetUserId,
                cancellationToken))
            return EndpointHelpers.Conflict(context, "Like already recorded.");

        db.Likes.Add(new UserLike(sourceUserId, request.TargetUserId, request.Type));
        var mutual = await db.Likes.AnyAsync(x => x.SourceUserId == request.TargetUserId && x.TargetUserId == sourceUserId,
            cancellationToken);
        Match? match = null;
        if (mutual)
        {
            match = new Match(sourceUserId, request.TargetUserId);
            db.Matches.Add(match);
        }
        await db.SaveChangesAsync(cancellationToken);

        var sourceName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == sourceUserId).Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken);
        if (match is not null)
        {
            var targetName = await db.Profiles.AsNoTracking()
                .Where(x => x.UserId == request.TargetUserId).Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken);
            await notifications.NotifyAsync(sourceUserId, NotificationType.NewMatch, "It's a match!",
                $"You matched with {targetName}. Send a chat request to start talking.", match.Id, "Match", cancellationToken);
            await notifications.NotifyAsync(request.TargetUserId, NotificationType.NewMatch, "It's a match!",
                $"You matched with {sourceName}. Send a chat request to start talking.", match.Id, "Match", cancellationToken);
        }
        else
        {
            await notifications.NotifyAsync(request.TargetUserId, NotificationType.NewLike, "You have a new like!",
                $"{sourceName} liked your profile.", sourceUserId, "Profile", cancellationToken);
        }

        return ApiResults.Ok(context, new { isMatch = match is not null, matchId = match?.Id },
            match is null ? "Like recorded successfully." : "It is a match.");
    }

    private static async Task<IResult> GetMyLikes(HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var targetUserIds = await db.Likes.AsNoTracking()
            .Where(x => x.SourceUserId == userId)
            .Select(x => x.TargetUserId)
            .ToListAsync(cancellationToken);
        return ApiResults.Ok(context, targetUserIds, "Your likes were retrieved successfully.");
    }

    private static async Task<IResult> GetMatches(HttpContext context, IMirageDbContext db, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var matches = await db.Matches.AsNoTracking()
            .Where(x => x.User1Id == userId || x.User2Id == userId)
            .OrderByDescending(x => x.MatchedAt).ToListAsync(cancellationToken);
        return ApiResults.Ok(context, matches, "Matches retrieved successfully.");
    }

    private static async Task<IResult> GetMatch(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var match = await db.Matches.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == id && (x.User1Id == userId || x.User2Id == userId), cancellationToken);
        return match is null
            ? EndpointHelpers.NotFound(context, "Match was not found.")
            : ApiResults.Ok(context, match, "Match retrieved successfully.");
    }

    private static async Task<IResult> CloseMatch(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var match = await db.Matches.SingleOrDefaultAsync(
            x => x.Id == id && (x.User1Id == userId || x.User2Id == userId), cancellationToken);
        if (match is null) return EndpointHelpers.NotFound(context, "Match was not found.");
        if (match.Status is MatchStatus.Closed or MatchStatus.Blocked)
            return EndpointHelpers.Conflict(context, "Match is already closed.");
        match.Close();
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { match.Id, match.Status }, "Match closed successfully.");
    }

    // Either party can send the first chat request; the other party then approves
    // (opens the thread) or declines by closing the match via the existing Close endpoint.
    private static async Task<IResult> RequestChat(Guid id, HttpContext context, IMirageDbContext db,
        NotificationService notifications, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var match = await db.Matches.SingleOrDefaultAsync(
            x => x.Id == id && (x.User1Id == userId || x.User2Id == userId), cancellationToken);
        if (match is null) return EndpointHelpers.NotFound(context, "Match was not found.");

        try
        {
            match.RequestChat(userId);
        }
        catch (InvalidOperationException ex)
        {
            return EndpointHelpers.Conflict(context, ex.Message);
        }
        await db.SaveChangesAsync(cancellationToken);

        var otherUserId = match.User1Id == userId ? match.User2Id : match.User1Id;
        var requesterName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken);
        await notifications.NotifyAsync(otherUserId, NotificationType.ChatRequestReceived, "New chat request",
            $"{requesterName} wants to start chatting with you.", match.Id, "Match", cancellationToken);

        return ApiResults.Ok(context, new { match.Id, match.Status, match.ChatRequestedByUserId },
            "Chat request sent successfully.");
    }

    private static async Task<IResult> ApproveChatRequest(Guid id, HttpContext context, IMirageDbContext db,
        NotificationService notifications, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var match = await db.Matches.SingleOrDefaultAsync(
            x => x.Id == id && (x.User1Id == userId || x.User2Id == userId), cancellationToken);
        if (match is null) return EndpointHelpers.NotFound(context, "Match was not found.");

        try
        {
            match.ApproveChat(userId);
        }
        catch (InvalidOperationException ex)
        {
            return EndpointHelpers.Conflict(context, ex.Message);
        }
        await db.SaveChangesAsync(cancellationToken);

        var approverName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken);
        await notifications.NotifyAsync(match.ChatRequestedByUserId!.Value, NotificationType.ChatRequestApproved,
            "Chat request approved", $"{approverName} approved your chat request. You can start chatting now.",
            match.Id, "Match", cancellationToken);

        return ApiResults.Ok(context, new { match.Id, match.Status }, "Chat request approved successfully.");
    }

    private static async Task<IResult> BlockMatch(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var match = await db.Matches.SingleOrDefaultAsync(
            x => x.Id == id && (x.User1Id == userId || x.User2Id == userId), cancellationToken);
        if (match is null) return EndpointHelpers.NotFound(context, "Match was not found.");
        if (match.Status == MatchStatus.Blocked)
            return EndpointHelpers.Conflict(context, "Match is already blocked.");
        match.Block();
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { match.Id, match.Status }, "Match blocked successfully.");
    }

    // Cursor-based pagination: pass the CreatedAt of the oldest message in the current
    // view as `before` to load earlier messages (standard chat scroll-back pattern).
    private static async Task<IResult> GetMessages(Guid id, HttpContext context, IMirageDbContext db,
        DateTimeOffset? before, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var userId = context.User.GetUserId();
        var inMatch = await db.Matches.AsNoTracking()
            .AnyAsync(x => x.Id == id && (x.User1Id == userId || x.User2Id == userId), cancellationToken);
        if (!inMatch) return EndpointHelpers.Forbidden(context);

        var query = db.Messages.AsNoTracking().Where(x => x.MatchId == id);
        if (before.HasValue) query = query.Where(x => x.CreatedAt < before.Value);

        var messages = await query
            .OrderByDescending(x => x.CreatedAt)
            .Take(pageSize)
            .Select(x => new
            {
                x.Id,
                x.SenderId,
                x.Content,
                x.Type,
                x.AttachmentUrl,
                SentAt = x.CreatedAt,
                x.IsRead,
                x.ReadAt
            })
            .ToListAsync(cancellationToken);

        // Return in chronological order for the client to append
        return ApiResults.Ok(context, new
        {
            Messages = Enumerable.Reverse(messages),
            HasMore = messages.Count == pageSize
        }, "Messages retrieved successfully.");
    }

    // REST fallback for sending messages (e.g. image messages after a Cloudinary upload
    // completes) — mirrors ChatHub.SendMessage and broadcasts through the same hub group
    // so connected SignalR clients still receive it in real time.
    private static async Task<IResult> SendMessage(Guid id, SendChatMessageRequest request, HttpContext context,
        IMirageDbContext db, IHubContext<ChatHub> hub, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var match = await db.Matches.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id
                && (x.User1Id == userId || x.User2Id == userId)
                && x.Status == MatchStatus.Active, cancellationToken);
        if (match is null) return EndpointHelpers.Forbidden(context);

        if (request.Type == MessageType.Image && string.IsNullOrWhiteSpace(request.AttachmentUrl))
            return EndpointHelpers.ValidationProblem(context, ("attachmentUrl", "Image messages require an attachment URL."));
        if (request.Type == MessageType.Text && string.IsNullOrWhiteSpace(request.Content))
            return EndpointHelpers.ValidationProblem(context, ("content", "Message content is required."));

        var message = new Message(id, userId, request.Content, request.Type, request.AttachmentUrl);
        db.Messages.Add(message);
        await db.SaveChangesAsync(cancellationToken);

        await hub.Clients.Group($"match:{id}").SendAsync("ReceiveMessage", new
        {
            message.Id,
            message.MatchId,
            message.SenderId,
            message.Content,
            message.Type,
            message.AttachmentUrl,
            SentAt = message.CreatedAt,
            message.IsRead
        }, cancellationToken);

        return ApiResults.Created(context, $"/api/v1/matching/matches/{id}/messages", new { message.Id },
            "Message sent successfully.");
    }

    private static async Task<IResult> MarkRead(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var inMatch = await db.Matches.AsNoTracking()
            .AnyAsync(x => x.Id == id && (x.User1Id == userId || x.User2Id == userId), cancellationToken);
        if (!inMatch) return EndpointHelpers.Forbidden(context);

        var unread = await db.Messages
            .Where(x => x.MatchId == id && x.SenderId != userId && !x.IsRead)
            .ToListAsync(cancellationToken);

        if (unread.Count == 0)
            return ApiResults.Ok(context, new { marked = 0 }, "No unread messages.");

        foreach (var msg in unread) msg.MarkRead();
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { marked = unread.Count }, "Messages marked as read.");
    }
}
