using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Security;
using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;

namespace Mirage.Api.Endpoints;

internal static class NotificationEndpoints
{
    public static RouteGroupBuilder MapNotificationEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/notifications").WithTags("Notifications").RequireAuthorization();
        group.MapGet("/", List);
        group.MapGet("/unread-count", UnreadCount);
        group.MapPost("/{id:guid}/read", MarkRead);
        group.MapPost("/read-all", MarkAllRead);
        group.MapPost("/device-tokens", RegisterDeviceToken);
        // POST, not DELETE: the token is a 512-char opaque string, so it has to travel in a body,
        // and minimal APIs refuse to bind a body on DELETE (bodies on DELETE are also liable to be
        // stripped by intermediary proxies).
        group.MapPost("/device-tokens/revoke", RevokeDeviceToken);
        // Per-space notification switches. Everything is on by default, so the list holds only
        // the spaces the caller has turned off.
        group.MapGet("/mutes", ListMutes);
        group.MapPut("/mutes", SetMute);
        return api;
    }

    // Called after the client obtains an FCM registration token, and again on every token refresh.
    // Idempotent by token: the same token re-registering just refreshes LastSeenAt, and a token
    // that has moved to a different account (same device, new sign-in) is re-pointed at the
    // current user so pushes stop following the previous one.
    private static async Task<IResult> RegisterDeviceToken(HttpContext context, IMirageDbContext db,
        RegisterDeviceTokenRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            return EndpointHelpers.ValidationProblem(context, (nameof(request.Token), "A device token is required."));
        if (!Enum.IsDefined(request.Platform))
            return EndpointHelpers.ValidationProblem(context, (nameof(request.Platform), "Platform is not recognised."));

        var userId = context.User.GetUserId();
        var token = request.Token.Trim();
        var existing = await db.DeviceTokens.SingleOrDefaultAsync(x => x.Token == token, cancellationToken);

        if (existing is null)
            db.DeviceTokens.Add(new DeviceToken(userId, token, request.Platform, request.DeviceName));
        else
            existing.Reclaim(userId, request.Platform, request.DeviceName);

        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { registered = true }, "Device registered for push notifications.");
    }

    // Sign-out and "turn off notifications". Scoped to the caller so one user cannot silence
    // another's device by guessing a token.
    private static async Task<IResult> RevokeDeviceToken(HttpContext context, IMirageDbContext db,
        RevokeDeviceTokenRequest request, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var token = (request.Token ?? string.Empty).Trim();
        var existing = await db.DeviceTokens.SingleOrDefaultAsync(
            x => x.Token == token && x.UserId == userId, cancellationToken);
        if (existing is null) return ApiResults.Ok(context, new { revoked = false }, "Device token was not registered.");

        existing.Revoke();
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { revoked = true }, "Device unregistered from push notifications.");
    }

    private static async Task<IResult> List(HttpContext context, IMirageDbContext db,
        int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var userId = context.User.GetUserId();
        var query = db.Notifications.AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new
            {
                x.Id,
                x.Type,
                x.Title,
                x.Body,
                x.ReferenceId,
                x.ReferenceType,
                x.IsRead,
                x.CreatedAt,
                x.ReadAt
            });
        return ApiResults.Ok(context,
            await query.ToPagedResultAsync(page, pageSize, cancellationToken),
            "Notifications retrieved successfully.");
    }

    private static async Task<IResult> UnreadCount(HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var count = await db.Notifications.AsNoTracking()
            .CountAsync(x => x.UserId == userId && !x.IsRead, cancellationToken);
        return ApiResults.Ok(context, new { count }, "Unread notification count retrieved successfully.");
    }

    private static async Task<IResult> MarkRead(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var notification = await db.Notifications.SingleOrDefaultAsync(
            x => x.Id == id && x.UserId == userId, cancellationToken);
        if (notification is null) return EndpointHelpers.NotFound(context, "Notification was not found.");
        notification.MarkRead();
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { notification.Id, notification.IsRead }, "Notification marked as read.");
    }

    private static async Task<IResult> MarkAllRead(HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var unread = await db.Notifications
            .Where(x => x.UserId == userId && !x.IsRead)
            .ToListAsync(cancellationToken);
        foreach (var notification in unread) notification.MarkRead();
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { marked = unread.Count }, "All notifications marked as read.");
    }

    private static async Task<IResult> ListMutes(HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var mutes = await db.NotificationMutes.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => new { x.Scope, x.ScopeId })
            .ToListAsync(cancellationToken);
        return ApiResults.Ok(context, mutes, "Notification mutes retrieved successfully.");
    }

    // Not gated on membership: muting a space you are not in changes nothing anyone can see, and
    // letting it through means a mute set just before leaving still holds if you rejoin.
    private static async Task<IResult> SetMute(HttpContext context, IMirageDbContext db,
        SetNotificationMuteRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.Scope))
            return EndpointHelpers.ValidationProblem(context, (nameof(request.Scope), "Scope is not recognised."));
        if (request.ScopeId == Guid.Empty)
            return EndpointHelpers.ValidationProblem(context, (nameof(request.ScopeId), "A scope id is required."));

        var userId = context.User.GetUserId();
        var existing = await db.NotificationMutes.SingleOrDefaultAsync(
            x => x.UserId == userId && x.Scope == request.Scope && x.ScopeId == request.ScopeId, cancellationToken);

        if (request.Muted && existing is null)
            db.NotificationMutes.Add(new NotificationMute(userId, request.Scope, request.ScopeId));
        else if (!request.Muted && existing is not null)
            db.NotificationMutes.Remove(existing);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (request.Muted)
        {
            // A double-tap raced another insert of the same mute; the unique index kept one row,
            // which is the state the caller asked for.
        }

        return ApiResults.Ok(context, new { request.Scope, request.ScopeId, muted = request.Muted },
            request.Muted ? "Notifications turned off." : "Notifications turned on.");
    }
}
