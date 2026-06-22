using Microsoft.AspNetCore.Identity;

namespace Mirage.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class MirageRoles
{
    public const string User = "User";
    public const string ChurchAdmin = "ChurchAdmin";
    public const string Counsellor = "Counsellor";
    public const string Mentor = "Mentor";
    public const string PlatformAdmin = "PlatformAdmin";
    public static readonly string[] All = [User, ChurchAdmin, Counsellor, Mentor, PlatformAdmin];
}
