using System.Security.Cryptography;
using System.Text;
using Mirage.Domain.Common;

namespace Mirage.Domain.Entities;

// A PlatformAdmin-issued invite letting a prospective org admin's organisation skip the
// Pending review queue — the org is created already Approved when redeemed via this token.
public sealed class OrganisationAdminInvite : Entity
{
    private OrganisationAdminInvite() { }

    public OrganisationAdminInvite(string email, string token, DateTimeOffset expiresAt)
    {
        Email = email.ToLowerInvariant().Trim();
        TokenHash = ComputeHash(token);
        ExpiresAt = expiresAt;
    }

    public string Email { get; private set; } = string.Empty;
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RedeemedAt { get; private set; }

    public bool IsValid => RedeemedAt is null && ExpiresAt > DateTimeOffset.UtcNow;

    public void Redeem() { RedeemedAt = DateTimeOffset.UtcNow; Touch(); }

    public static string ComputeHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
