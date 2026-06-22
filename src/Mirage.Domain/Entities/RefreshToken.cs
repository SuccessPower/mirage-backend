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
    public void Revoke() { RevokedAt = DateTimeOffset.UtcNow; Touch(); }
    public static string ComputeHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
