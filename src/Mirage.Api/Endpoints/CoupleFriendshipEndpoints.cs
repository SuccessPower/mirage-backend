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

// The shared conversation thread between a married member and another approved couple —
// exactly three participants (the friend plus both partners), private to them.
internal static class CoupleFriendshipEndpoints
{
    public static RouteGroupBuilder MapCoupleFriendshipEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/couple-friendships").WithTags("CoupleFriendships").RequireAuthorization();
        group.MapGet("/mine", GetMine);
        group.MapDelete("/{id:guid}", EndFriendship);
        group.MapGet("/{id:guid}/messages", GetMessages);
        group.MapPost("/{id:guid}/messages", SendMessage);
        return api;
    }

    private static Task<bool> IsParticipantAsync(Guid friendshipId, Guid userId, IMirageDbContext db,
        CancellationToken cancellationToken) =>
        db.CoupleFriendships.AsNoTracking().AnyAsync(f => f.Id == friendshipId
            && f.Status == CoupleFriendshipStatus.Active
            && (f.FriendUserId == userId || db.Couples.Any(c => c.Id == f.CoupleId
                && (c.User1Id == userId || c.User2Id == userId))), cancellationToken);

    private static async Task<IResult> GetMine(HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var friendships = await db.CoupleFriendships.AsNoTracking()
            .Where(f => f.Status == CoupleFriendshipStatus.Active
                && (f.FriendUserId == userId || db.Couples.Any(c => c.Id == f.CoupleId
                    && (c.User1Id == userId || c.User2Id == userId))))
            .Join(db.Couples.AsNoTracking(), f => f.CoupleId, c => c.Id,
                (f, c) => new { f.Id, f.CoupleId, f.FriendUserId, f.Status, f.CreatedAt, c.User1Id, c.User2Id })
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        var participantIds = friendships
            .SelectMany(x => new[] { x.FriendUserId, x.User1Id, x.User2Id })
            .Distinct().ToArray();
        var profiles = await db.Profiles.AsNoTracking()
            .Where(p => participantIds.Contains(p.UserId))
            .Select(p => new { p.UserId, p.DisplayName, p.AvatarUrl, p.IsVerified })
            .ToDictionaryAsync(p => p.UserId, cancellationToken);

        CoupleFriendParticipant ToParticipant(Guid participantId)
        {
            var profile = profiles.GetValueOrDefault(participantId);
            return new CoupleFriendParticipant(participantId, profile?.DisplayName ?? "Unknown",
                profile?.AvatarUrl, profile?.IsVerified ?? false);
        }

        var response = friendships.Select(x => new CoupleFriendshipResponse(
            x.Id, x.CoupleId, x.FriendUserId, ToParticipant(x.FriendUserId),
            ToParticipant(x.User1Id), ToParticipant(x.User2Id), x.Status, x.CreatedAt)).ToList();
        return ApiResults.Ok(context, response, "Couple friendships retrieved successfully.");
    }

    // Any of the three participants can end the friendship, closing the thread for everyone
    // and freeing a conversation slot for all three.
    private static async Task<IResult> EndFriendship(Guid id, HttpContext context, IMirageDbContext db,
        NotificationService notifications, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var friendship = await db.CoupleFriendships.SingleOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (friendship is null) return EndpointHelpers.NotFound(context, "Couple friendship was not found.");

        var couple = await db.Couples.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == friendship.CoupleId, cancellationToken);
        var participantIds = new[] { friendship.FriendUserId, couple?.User1Id ?? Guid.Empty, couple?.User2Id ?? Guid.Empty };
        if (!participantIds.Contains(userId)) return EndpointHelpers.Forbidden(context);

        try { friendship.End(); }
        catch (InvalidOperationException ex) { return EndpointHelpers.Conflict(context, ex.Message); }
        await db.SaveChangesAsync(cancellationToken);

        var enderName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken);
        foreach (var participantId in participantIds.Where(x => x != Guid.Empty && x != userId))
        {
            await notifications.NotifyAsync(participantId, NotificationType.CoupleFriendshipEnded,
                "Couple friendship ended", $"{enderName} ended your shared couple conversation.",
                friendship.Id, "CoupleFriendship", cancellationToken);
        }

        return ApiResults.Ok(context, new { friendship.Id, friendship.Status },
            "Couple friendship ended successfully.");
    }

    // Cursor-based pagination, same pattern as match chat: pass the CreatedAt of the oldest
    // loaded message as `before` to scroll back.
    private static async Task<IResult> GetMessages(Guid id, HttpContext context, IMirageDbContext db,
        DateTimeOffset? before, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        var userId = context.User.GetUserId();
        if (!await IsParticipantAsync(id, userId, db, cancellationToken))
            return EndpointHelpers.Forbidden(context);

        var query = db.CoupleFriendMessages.AsNoTracking().Where(x => x.FriendshipId == id);
        if (before.HasValue) query = query.Where(x => x.CreatedAt < before.Value);

        var messages = await query
            .OrderByDescending(x => x.CreatedAt)
            .Take(pageSize)
            .Join(db.Profiles.AsNoTracking(), m => m.SenderId, p => p.UserId, (m, p) => new
            {
                m.Id,
                FriendshipId = m.FriendshipId,
                m.SenderId,
                SenderName = p.DisplayName,
                m.Content,
                m.Type,
                m.AttachmentUrl,
                SentAt = m.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return ApiResults.Ok(context, new
        {
            Messages = Enumerable.Reverse(messages),
            HasMore = messages.Count == pageSize
        }, "Messages retrieved successfully.");
    }

    // REST fallback mirroring ChatHub.SendCoupleFriendMessage — broadcasts through the same
    // hub group so connected clients still receive it in real time.
    private static async Task<IResult> SendMessage(Guid id, SendChatMessageRequest request, HttpContext context,
        IMirageDbContext db, IHubContext<ChatHub> hub, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        if (!await IsParticipantAsync(id, userId, db, cancellationToken))
            return EndpointHelpers.Forbidden(context);

        if (request.Type == MessageType.Image && string.IsNullOrWhiteSpace(request.AttachmentUrl))
            return EndpointHelpers.ValidationProblem(context, ("attachmentUrl", "Image messages require an attachment URL."));
        if (request.Type == MessageType.Text && string.IsNullOrWhiteSpace(request.Content))
            return EndpointHelpers.ValidationProblem(context, ("content", "Message content is required."));

        var message = new CoupleFriendMessage(id, userId, request.Content, request.Type, request.AttachmentUrl);
        db.CoupleFriendMessages.Add(message);
        await db.SaveChangesAsync(cancellationToken);

        var senderName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken);

        await hub.Clients.Group($"couplefriend:{id}").SendAsync("ReceiveCoupleFriendMessage", new
        {
            message.Id,
            FriendshipId = id,
            message.SenderId,
            SenderName = senderName,
            message.Content,
            message.Type,
            message.AttachmentUrl,
            SentAt = message.CreatedAt
        }, cancellationToken);

        return ApiResults.Created(context, $"/api/v1/couple-friendships/{id}/messages", new { message.Id },
            "Message sent successfully.");
    }
}
