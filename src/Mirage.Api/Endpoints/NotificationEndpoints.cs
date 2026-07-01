using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Security;
using Mirage.Application.Abstractions;

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
        return api;
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
}
