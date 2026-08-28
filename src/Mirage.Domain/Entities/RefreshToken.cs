using System.Security.Cryptography;
using System.Text;
using Mirage.Domain.Common;

namespace Mirage.Domain.Entities;

public sealed class RefreshToken : Entity
{
    private RefreshToken() { }
    public RefreshToken(Guid userId, string token, DateTimeOffset expiresAt)
    {
        UserId = userId;
        TokenHash = ComputeHash(token);
        ExpiresAt = expiresAt;
    }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public bool IsActive => RevokedAt is null && ExpiresAt > DateTimeOffset.UtcNow;
    public bool Matches(string token) => CryptographicOperations.FixedTimeEquals(
        Convert.FromHexString(TokenHash), Convert.FromHexString(ComputeHash(token)));
    public void Revoke()
    {
        // Idempotent: a token replayed inside the rotation grace window must keep its original
        // revocation time, otherwise every retry slides the window forward and a leaked token
        // could be refreshed indefinitely.
        if (RevokedAt is not null) return;
        RevokedAt = DateTimeOffset.UtcNow;
        Touch();
    }

    /// <summary>
    /// True when this token was rotated only moments ago. Mobile clients drop connections and get
    /// suspended mid-refresh, so the same refresh token legitimately arrives twice; treating the
    /// second arrival as a breach signs the member out for a network blip. Outside the window a
    /// replay is still rejected.
    /// </summary>
    public bool IsWithinRotationGrace(TimeSpan grace) =>
        RevokedAt is not null && DateTimeOffset.UtcNow - RevokedAt.Value <= grace;
    public static string ComputeHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
