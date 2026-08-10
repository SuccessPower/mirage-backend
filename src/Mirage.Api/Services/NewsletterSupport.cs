using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using Mirage.Infrastructure.Identity;

namespace Mirage.Api.Services;

/// <summary>Single definition of "who receives a newsletter", shared by the audience preview and the dispatcher
/// so a scheduled recipient count can never drift from who actually gets mailed.</summary>
public static class NewsletterAudience
{
    public static readonly Expression<Func<ApplicationUser, bool>> IsSubscriber =
        x => x.IsActive && !x.IsDeleted && x.EmailConfirmed && x.IsNewsletterSubscribed && x.Email != null;
}

/// <summary>One-click unsubscribe links. The token is an HMAC over the user id keyed with the JWT signing key, so
/// links stay valid across restarts and deployments without a database round trip or a signed-in session.</summary>
public static class NewsletterUnsubscribe
{
    public static string BuildUrl(string appUrl, Guid userId, IConfiguration configuration) =>
        $"{appUrl}/newsletter-unsubscribe?token={Uri.EscapeDataString(CreateToken(userId, configuration))}";

    public static string CreateToken(Guid userId, IConfiguration configuration) =>
        $"{Encode(userId.ToByteArray())}.{Encode(Sign(userId, configuration))}";

    public static bool TryReadUserId(string? token, IConfiguration configuration, out Guid userId)
    {
        userId = Guid.Empty;
        var parts = (token ?? string.Empty).Split('.');
        if (parts.Length != 2) return false;
        try
        {
            var idBytes = Decode(parts[0]);
            if (idBytes.Length != 16) return false;
            var candidate = new Guid(idBytes);
            if (!CryptographicOperations.FixedTimeEquals(Decode(parts[1]), Sign(candidate, configuration))) return false;
            userId = candidate;
            return true;
        }
        catch (FormatException) { return false; }
    }

    private static byte[] Sign(Guid userId, IConfiguration configuration)
    {
        var key = configuration["Jwt:SigningKey"]
            ?? throw new InvalidOperationException("Jwt:SigningKey is required to sign unsubscribe links.");
        return HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), userId.ToByteArray());
    }

    private static string Encode(byte[] value) => Convert.ToBase64String(value)
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Decode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '='));
    }
}
