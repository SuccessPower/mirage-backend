using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Mirage.Api.Services;

// Mints a per-session, per-user JWT for Jitsi as a Service (JaaS, 8x8.vc) — the paid/free-tier
// hosted Jitsi product. Unlike the public meet.jit.si server, JaaS rooms are private to our App ID
// and require a signed JWT to join, which avoids meet.jit.si's "waiting for a moderator to log in"
// gate and its native-app deep-linking prompts on mobile.
public sealed class JitsiService(IConfiguration configuration)
{
    public string AppId =>
        configuration["Jitsi:AppId"] ?? throw new InvalidOperationException("Jitsi:AppId is not configured.");

    private string ApiKeyId =>
        configuration["Jitsi:ApiKeyId"] ?? throw new InvalidOperationException("Jitsi:ApiKeyId is not configured.");

    private string PrivateKeyPem =>
        (configuration["Jitsi:PrivateKey"] ?? throw new InvalidOperationException("Jitsi:PrivateKey is not configured."))
            .Replace("\\n", "\n");

    public string CreateToken(Guid userId, string displayName, string? email, string room, bool isModerator)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(PrivateKeyPem);
        var key = new RsaSecurityKey(rsa) { KeyId = ApiKeyId };
        var credentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);

        var now = DateTimeOffset.UtcNow;
        var header = new JwtHeader(credentials);
        header["kid"] = ApiKeyId;

        var payload = new JwtPayload
        {
            { "aud", "jitsi" },
            { "iss", "chat" },
            { "sub", AppId },
            { "room", room },
            { "exp", now.AddHours(2).ToUnixTimeSeconds() },
            { "nbf", now.AddSeconds(-10).ToUnixTimeSeconds() },
            {
                "context", new Dictionary<string, object?>
                {
                    ["user"] = new Dictionary<string, object?>
                    {
                        ["id"] = userId.ToString(),
                        ["name"] = displayName,
                        ["email"] = email,
                        ["moderator"] = isModerator ? "true" : "false",
                    },
                    ["features"] = new Dictionary<string, object?>
                    {
                        ["livestreaming"] = "false",
                        ["recording"] = "false",
                        ["transcription"] = "false",
                        ["outbound-call"] = "false",
                    },
                }
            },
        };

        var token = new JwtSecurityToken(header, payload);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
