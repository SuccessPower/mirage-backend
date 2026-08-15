using Mirage.Domain.Common;
using Mirage.Domain.Enums;

namespace Mirage.Domain.Entities;

// The audit record behind an admin's profile/conduct warning email — also what
// WarningReminderService scans to nudge the issuing admin if a deadline passes unresolved.
// DeadlineUtc is null for an immediate suspension: there's nothing to follow up on, so the row
// is created already resolved.
public sealed class AccountWarning : Entity
{
    private AccountWarning() { }

    public AccountWarning(Guid userId, Guid issuedByUserId, WarningType type, string message,
        DateTimeOffset? deadlineUtc)
    {
        UserId = userId;
        IssuedByUserId = issuedByUserId;
        Type = type;
        Message = message.Trim();
        DeadlineUtc = deadlineUtc;
        if (deadlineUtc is null) ResolvedAt = DateTimeOffset.UtcNow;
    }

    public Guid UserId { get; private set; }
    public Guid IssuedByUserId { get; private set; }
    public WarningType Type { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public DateTimeOffset? DeadlineUtc { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    // Set once NotificationService.NotifyAdminsOfProfileUpdateAsync has nudged IssuedByUserId that
    // the user updated their profile — separate from ResolvedAt, which only the admin sets by
    // actually resolving the warning, so that nudge fires at most once per warning.
    public DateTimeOffset? ProfileUpdateNotifiedAt { get; private set; }

    public void Resolve()
    {
        if (ResolvedAt is not null) return;
        ResolvedAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public void MarkProfileUpdateNotified()
    {
        if (ProfileUpdateNotifiedAt is not null) return;
        ProfileUpdateNotifiedAt = DateTimeOffset.UtcNow;
        Touch();
    }
}
