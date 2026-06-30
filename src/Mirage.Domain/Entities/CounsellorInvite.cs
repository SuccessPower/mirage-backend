using System.Security.Cryptography;
using System.Text;
using Mirage.Domain.Common;

namespace Mirage.Domain.Entities;

public sealed class CounsellorInvite : Entity
{
    private CounsellorInvite() { }

    public CounsellorInvite(Guid organisationId, string email, string token, DateTimeOffset expiresAt)
    {
        OrganisationId = organisationId;
        Email = email.ToLowerInvariant().Trim();
        TokenHash = ComputeHash(token);
        ExpiresAt = expiresAt;
    }

    public Guid OrganisationId { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RedeemedAt { get; private set; }
    public Organisation Organisation { get; private set; } = null!;

    public bool IsValid => RedeemedAt is null && ExpiresAt > DateTimeOffset.UtcNow;

    public void Redeem() { RedeemedAt = DateTimeOffset.UtcNow; Touch(); }

    public static string ComputeHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
