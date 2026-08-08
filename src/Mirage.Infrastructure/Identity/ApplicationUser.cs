using Microsoft.AspNetCore.Identity;

namespace Mirage.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? WelcomeEmailSentAt { get; set; }

    // Stamped when the user's last realtime connection drops, so chat peers can show
    // "last seen 10:42" once they go offline. Null means never connected since the feature shipped.
    public DateTimeOffset? LastSeenAt { get; set; }

    // Re-engagement ("we miss you") emails. The count is what stops a dormant account from being
    // mailed forever; both are reset on the next sign-in so a user who returns and lapses again
    // starts a fresh series. See ReEngagementService.
    public DateTimeOffset? LastReEngagementEmailAt { get; set; }
    public int ReEngagementEmailCount { get; set; }
}

public static class MirageRoles
{
    public const string User = "User";
    public const string ChurchAdmin = "ChurchAdmin";
    public const string Counsellor = "Counsellor";
    public const string Mentor = "Mentor";
    public const string Vendor = "Vendor";
    public const string PlatformAdmin = "PlatformAdmin";
    public static readonly string[] All = [User, ChurchAdmin, Counsellor, Mentor, Vendor, PlatformAdmin];
}

public static class IdentityCacheKeys
{
    public const string DefaultUserRoleId = "mirage:identity:role:user";
}

// The "Mirage Team" account that authors automatic birthday/anniversary celebration posts and
// comments (see CelebrationPostService). Seeded once by DatabaseInitialiser; no one signs in as
// this account.
public static class SystemAccounts
{
    public const string MirageTeamEmail = "system@mirage.internal";
    public const string MirageTeamDisplayName = "Mirage Team";
    public const string MirageTeamUserIdCacheKey = "mirage:system:mirageteam:userid";
}
