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
        NotificationType.MentorApproved,
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
        ,NotificationType.CalendarReminder
        ,NotificationType.ProfessionalConnectionRequest
        // A mentorship request is the mentor's whole inbox. It had a template but was never on
        // this list, so the in-app notification was the only signal a mentor ever got — and a
        // mentor who wasn't in the app that day never learned the request existed.
        ,NotificationType.MentorRequestReceived
        ,NotificationType.ProfessionalInviteRedeemed
        ,NotificationType.MentorshipPaymentReceived
        ,NotificationType.MentorEventPublished
    ];

    public async Task NotifyAsync(Guid userId, NotificationType type, string title, string body,
        Guid? referenceId = null, string? referenceType = null, CancellationToken cancellationToken = default,
        string? actionUrl = null, string? actionLabel = null)
    {
        var notification = new Notification(userId, type, title, body, referenceId, referenceType);
        db.Notifications.Add(notification);
        await db.SaveChangesAsync(cancellationToken);

        await SendToHubAsync(notification, cancellationToken);

        if (!PushSuppressedTypes.Contains(type))
            await SendPushAsync([notification], cancellationToken);

        if (EmailableTypes.Contains(type) && email.HasNotificationTemplate(type))
            await SendNotificationEmailAsync(userId, type, title, body, actionUrl, actionLabel, cancellationToken);
    }

    // Fan-out for activity in a shared space (a community post, a group message): tells every
    // recipient except those who have muted that space. Written as one batch — one insert, one
    // device-token lookup — because a community can have thousands of members, and NotifyAsync's
    // per-recipient round trips would hold the poster's request open for all of them.
    public async Task NotifyMembersAsync(IEnumerable<Guid> recipientIds, NotificationScope scope, Guid scopeId,
        NotificationType type, string title, string body, Guid? referenceId = null, string? referenceType = null,
        CancellationToken cancellationToken = default)
    {
        var recipients = recipientIds.Distinct().ToList();
        if (recipients.Count == 0) return;

        var muted = await db.NotificationMutes.AsNoTracking()
            .Where(x => x.Scope == scope && x.ScopeId == scopeId && recipients.Contains(x.UserId))
            .Select(x => x.UserId)
            .ToListAsync(cancellationToken);
        if (muted.Count > 0) recipients = recipients.Except(muted).ToList();
        if (recipients.Count == 0) return;

        var created = recipients
            .Select(userId => new Notification(userId, type, title, body, referenceId, referenceType))
            .ToList();
        db.Notifications.AddRange(created);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var notification in created)
            await SendToHubAsync(notification, cancellationToken);

        if (!PushSuppressedTypes.Contains(type))
            await SendPushAsync(created, cancellationToken);

        if (EmailableTypes.Contains(type) && email.HasNotificationTemplate(type))
            foreach (var userId in recipients)
                await SendNotificationEmailAsync(userId, type, title, body, null, null, cancellationToken);
    }

    // The first line or so of a post or message, for a notification body. Image-only content has
    // no text, so it reads as a photo rather than as an empty quote.
    public static string Preview(string? text, int maxLength = 120)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0) return "Shared a photo";
        return trimmed.Length > maxLength ? trimmed[..maxLength].TrimEnd() + "…" : trimmed;
    }

    private Task SendToHubAsync(Notification notification, CancellationToken cancellationToken) =>
        hub.Clients.Group(NotificationHub.UserGroup(notification.UserId)).SendAsync("Notification", new
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

    private async Task SendPushAsync(IReadOnlyCollection<Notification> notifications,
        CancellationToken cancellationToken)
    {
        if (!push.IsEnabled || notifications.Count == 0) return;

        var userIds = notifications.Select(x => x.UserId).Distinct().ToList();
        var tokensByUser = (await db.DeviceTokens.AsNoTracking()
                .Where(x => userIds.Contains(x.UserId) && x.RevokedAt == null)
                .Select(x => new { x.UserId, x.Token })
                .ToListAsync(cancellationToken))
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Token).ToList());
        if (tokensByUser.Count == 0) return;

        var withDevices = tokensByUser.Keys.ToList();
        var unreadByUser = await db.Notifications
            .Where(x => withDevices.Contains(x.UserId) && !x.IsRead)
            .GroupBy(x => x.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, cancellationToken);

        var dead = new List<string>();
        foreach (var notification in notifications)
        {
            if (!tokensByUser.TryGetValue(notification.UserId, out var tokens)) continue;

            // Enum names, not numbers: the mobile and web clients switch on these strings to pick the
            // route to deep-link into (see PushNotificationService._routeFor on Flutter).
            var data = new Dictionary<string, string>
            {
                ["notificationId"] = notification.Id.ToString(),
                ["type"] = notification.Type.ToString()
            };
            if (notification.ReferenceId is { } referenceId) data["referenceId"] = referenceId.ToString();
            if (notification.ReferenceType is { } referenceType) data["referenceType"] = referenceType;

            dead.AddRange(await push.SendAsync(tokens,
                new PushPayload(notification.Title, notification.Body, data,
                    unreadByUser.GetValueOrDefault(notification.UserId)), cancellationToken));
        }
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
