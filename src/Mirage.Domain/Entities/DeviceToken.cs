using Mirage.Domain.Common;
using Mirage.Domain.Enums;

namespace Mirage.Domain.Entities;

// One FCM registration token per app install. Tokens are globally unique but not stable: FCM
// reissues them on reinstall, restore, or its own rotation schedule, and a reissued token can
// land on a different user (same phone, new sign-in). So the token is the natural key and
// re-registering simply re-points the row at whoever is signed in now.
public sealed class DeviceToken : Entity
{
    private DeviceToken() { }

    public DeviceToken(Guid userId, string token, DevicePlatform platform, string? deviceName = null)
    {
        UserId = userId;
        Token = token;
        Platform = platform;
        DeviceName = deviceName;
        LastSeenAt = DateTimeOffset.UtcNow;
    }

    public Guid UserId { get; private set; }
    public string Token { get; private set; } = string.Empty;
    public DevicePlatform Platform { get; private set; }
    public string? DeviceName { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }

    // Set when FCM tells us the token is dead (UNREGISTERED/INVALID_ARGUMENT) or the user signs
    // out. Kept rather than deleted so a re-register can revive the same row.
    public DateTimeOffset? RevokedAt { get; private set; }
    public bool IsActive => RevokedAt is null;

    public void Reclaim(Guid userId, DevicePlatform platform, string? deviceName)
    {
        UserId = userId;
        Platform = platform;
        if (!string.IsNullOrWhiteSpace(deviceName)) DeviceName = deviceName;
        LastSeenAt = DateTimeOffset.UtcNow;
        RevokedAt = null;
        Touch();
    }

    public void Revoke()
    {
        if (RevokedAt is not null) return;
        RevokedAt = DateTimeOffset.UtcNow;
        Touch();
    }
}
