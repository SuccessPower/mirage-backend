using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Mirage.Api.Hubs;
using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;
using Mirage.Infrastructure.Identity;

namespace Mirage.Api.Services;

public sealed class NotificationService(IMirageDbContext db, IHubContext<NotificationHub> hub,
    IEmailService email, UserManager<ApplicationUser> userManager)
{
    // High-signal events worth an email; noisy/high-frequency ones (likes, mentions, chat/session
    // messages) stay in-app only so users aren't spammed.
    private static readonly HashSet<NotificationType> EmailableTypes =
    [
        NotificationType.GatheringInviteReceived,
        NotificationType.GatheringInviteAccepted,
        NotificationType.MembershipApproved,
        NotificationType.MembershipRejected,
        NotificationType.OrganisationApproved,
        NotificationType.OrganisationRejected,
        NotificationType.CounsellorApproved,
        NotificationType.SessionBooked,
        NotificationType.SessionAccepted,
        NotificationType.MentorRequestAccepted,
        NotificationType.ChatRequestApproved,
        NotificationType.ProfileVerified,
        NotificationType.VendorApproved,
        NotificationType.VendorRejected,
        NotificationType.DateOfBirthInvalid
    ];

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

        if (EmailableTypes.Contains(type) && email.HasNotificationTemplate(type))
            await SendNotificationEmailAsync(userId, type, title, body, cancellationToken);
    }

    private async Task SendNotificationEmailAsync(Guid userId, NotificationType type, string title, string body,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user?.Email is null) return;

        var displayName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken);

        await email.SendNotificationEmailAsync(user.Email, displayName ?? "there", type, title, body,
            cancellationToken: cancellationToken);
    }
}
