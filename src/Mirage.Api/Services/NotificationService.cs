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
    IEmailService email, UserManager<ApplicationUser> userManager, PushSender push)
{
    // Push goes out for everything except the genuinely ambient events — a buzz for every profile
    // view or ended conversation trains people to turn notifications off. These still land in-app.
    private static readonly HashSet<NotificationType> PushSuppressedTypes =
    [
        NotificationType.ProfileVisited,
        NotificationType.ConversationEnded,
        // A popular post can collect reactions all day; buzzing for each one is the fastest way to
        // get notifications switched off. Mentions and comments still push — someone is talking to
        // you, not just passing by.
        NotificationType.PostReaction
    ];

    // High-signal events worth an email; noisy/high-frequency ones (likes, mentions, mentor and
    // counselling session chatter) stay in-app only so users aren't spammed.
    //
    // NewMessage is on this list but is not high-frequency in practice: ChatHub only raises it
    // when the recipient is offline and has no unread message from that sender waiting, so a
    // whole conversation produces at most one email until it is read.
    private static readonly HashSet<NotificationType> EmailableTypes =
    [
        NotificationType.NewMessage,
        NotificationType.ChatRequestReceived,
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
        NotificationType.DateOfBirthInvalid,
        NotificationType.ProfilePhotosRequired,
        NotificationType.ProfilePhotosComplete,
        // Goes to an admin, not a member — it's the one nudge that a warning deadline passed
        // unresolved, so it has to actually reach them rather than sit unread in-app.
        NotificationType.WarningDeadlinePassed,
        NotificationType.ProfileHidden,
        NotificationType.ProfileVisibleAgain
    ];

    public async Task NotifyAsync(Guid userId, NotificationType type, string title, string body,
        Guid? referenceId = null, string? referenceType = null, CancellationToken cancellationToken = default,
        string? actionUrl = null, string? actionLabel = null)
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

        if (!PushSuppressedTypes.Contains(type))
            await SendPushAsync(notification, cancellationToken);

        if (EmailableTypes.Contains(type) && email.HasNotificationTemplate(type))
            await SendNotificationEmailAsync(userId, type, title, body, actionUrl, actionLabel, cancellationToken);
    }

    // Called after a self-service profile update (UpdateMine/UpdateMyPhotos) to nudge whichever
    // admin(s) flagged this account — via a direct hide or an open profile/conduct warning — that
    // it's worth another look. Each flag fires at most once: the hide flag clears itself here, and
    // each warning is stamped so it won't re-fire on the next edit.
    public async Task NotifyAdminsOfProfileUpdateAsync(Guid userId, string frontendUrl,
        CancellationToken cancellationToken = default)
    {
        var adminIds = new List<Guid>();

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user?.HiddenByAdminId is { } hiddenBy)
        {
            adminIds.Add(hiddenBy);
            user.HiddenByAdminId = null;
            await userManager.UpdateAsync(user);
        }

        var pendingWarnings = await db.AccountWarnings
            .Where(w => w.UserId == userId && w.ResolvedAt == null && w.ProfileUpdateNotifiedAt == null
                && (w.Type == WarningType.Profile || w.Type == WarningType.Conduct))
            .ToListAsync(cancellationToken);
        foreach (var warning in pendingWarnings)
        {
            adminIds.Add(warning.IssuedByUserId);
            warning.MarkProfileUpdateNotified();
        }
        if (pendingWarnings.Count > 0) await db.SaveChangesAsync(cancellationToken);
        if (adminIds.Count == 0) return;

        var memberName = await db.Profiles.AsNoTracking().Where(p => p.UserId == userId)
            .Select(p => p.DisplayName).FirstOrDefaultAsync(cancellationToken) ?? "A member";

        foreach (var adminId in adminIds.Distinct())
            await NotifyAsync(adminId, NotificationType.ProfileUpdatedAfterReview, "Member updated their profile",
                $"{memberName} updated their profile after you flagged it for review. Take a look and resolve it if it's sorted.",
                referenceId: userId, referenceType: "Profile", cancellationToken: cancellationToken,
                actionUrl: $"{frontendUrl}/profiles/{userId}", actionLabel: "Review profile");
    }

    private async Task SendPushAsync(Notification notification, CancellationToken cancellationToken)
    {
        if (!push.IsEnabled) return;

        var tokens = await db.DeviceTokens.AsNoTracking()
            .Where(x => x.UserId == notification.UserId && x.RevokedAt == null)
            .Select(x => x.Token)
            .ToListAsync(cancellationToken);
        if (tokens.Count == 0) return;

        var unread = await db.Notifications
            .CountAsync(x => x.UserId == notification.UserId && !x.IsRead, cancellationToken);

        // Enum names, not numbers: the mobile and web clients switch on these strings to pick the
        // route to deep-link into (see PushNotificationService._routeFor on Flutter).
        var data = new Dictionary<string, string>
        {
            ["notificationId"] = notification.Id.ToString(),
            ["type"] = notification.Type.ToString()
        };
        if (notification.ReferenceId is { } referenceId) data["referenceId"] = referenceId.ToString();
        if (notification.ReferenceType is { } referenceType) data["referenceType"] = referenceType;

        var dead = await push.SendAsync(tokens,
            new PushPayload(notification.Title, notification.Body, data, unread), cancellationToken);
        if (dead.Count == 0) return;

        var stale = await db.DeviceTokens.Where(x => dead.Contains(x.Token)).ToListAsync(cancellationToken);
        foreach (var token in stale) token.Revoke();
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task SendNotificationEmailAsync(Guid userId, NotificationType type, string title, string body,
        string? actionUrl, string? actionLabel, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user?.Email is null) return;

        var displayName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.DisplayName).SingleOrDefaultAsync(cancellationToken);

        await email.SendNotificationEmailAsync(user.Email, displayName ?? "there", type, title, body,
            actionUrl, actionLabel, cancellationToken);
    }
}
