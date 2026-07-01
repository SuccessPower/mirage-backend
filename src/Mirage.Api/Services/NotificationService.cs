using Microsoft.AspNetCore.SignalR;
using Mirage.Api.Hubs;
using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;

namespace Mirage.Api.Services;

public sealed class NotificationService(IMirageDbContext db, IHubContext<NotificationHub> hub)
{
    public async Task NotifyAsync(Guid userId, NotificationType type, string title, string body,
        Guid? referenceId = null, string? referenceType = null, CancellationToken cancellationToken = default)
    {
        var notification = new Notification(userId, type, title, body, referenceId, referenceType);
        db.Notifications.Add(notification);
        await db.SaveChangesAsync(cancellationToken);

        await hub.Clients.Group(NotificationHub.UserGroup(userId)).SendAsync("Notification", new
        {
            notification.Id,
            notification.Type,
            notification.Title,
            notification.Body,
            notification.ReferenceId,
            notification.ReferenceType,
            notification.IsRead,
            notification.CreatedAt
        }, cancellationToken);
    }
}
