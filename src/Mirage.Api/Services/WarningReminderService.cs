using Microsoft.EntityFrameworkCore;
using Mirage.Domain.Enums;
using Mirage.Infrastructure.Persistence;

namespace Mirage.Api.Services;

// Scans for admin warnings whose deadline has passed with no action taken, and nudges the
// issuing admin to go review the account. Dedupes the same way ProfilePhotoReminderService does
// (checking for an existing Notification against the warning) rather than a separate "sent" flag.
public sealed class WarningReminderService(MirageDbContext db, NotificationService notifications,
    IConfiguration configuration)
{
    public async Task RunBatchAsync(int batchSize, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var overdue = await db.AccountWarnings.AsNoTracking()
            .Where(w => w.ResolvedAt == null && w.DeadlineUtc != null && w.DeadlineUtc <= now
                && !db.Notifications.Any(n => n.Type == NotificationType.WarningDeadlinePassed
                    && n.ReferenceId == w.Id))
            .OrderBy(w => w.DeadlineUtc)
            .Take(batchSize)
            .Select(w => new { w.Id, w.UserId, w.IssuedByUserId, w.Type })
            .ToListAsync(cancellationToken);
        if (overdue.Count == 0) return;

        // An account already suspended (via this warning or any other route) needs no reminder —
        // resolve those instead of nagging an admin about something already handled.
        var userIds = overdue.Select(w => w.UserId).Distinct().ToList();
        var stillActive = (await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id) && u.IsActive)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken)).ToHashSet();

        var frontendUrl = (configuration["Frontend:BaseUrl"] ?? "https://mirage-ui-iota.vercel.app").TrimEnd('/');
        var toResolve = new List<Guid>();
        foreach (var warning in overdue)
        {
            if (!stillActive.Contains(warning.UserId))
            {
                toResolve.Add(warning.Id);
                continue;
            }

            var memberName = await db.Profiles.AsNoTracking()
                .Where(p => p.UserId == warning.UserId).Select(p => p.DisplayName)
                .FirstOrDefaultAsync(cancellationToken) ?? "A member";
            var kind = warning.Type == WarningType.Profile ? "profile warning" : "conduct warning";
            await notifications.NotifyAsync(warning.IssuedByUserId, NotificationType.WarningDeadlinePassed,
                "Warning deadline passed",
                $"The {kind} deadline for {memberName} passed with no action taken. Review the account and suspend it if the issue is still unresolved.",
                referenceId: warning.Id, referenceType: "AccountWarning", cancellationToken: cancellationToken,
                actionUrl: $"{frontendUrl}/profiles/{warning.UserId}", actionLabel: "Review account");
        }

        if (toResolve.Count == 0) return;
        var rows = await db.AccountWarnings.Where(w => toResolve.Contains(w.Id)).ToListAsync(cancellationToken);
        foreach (var row in rows) row.Resolve();
        await db.SaveChangesAsync(cancellationToken);
    }
}
