namespace Mirage.Api.Security;

public sealed class JwtOptions
{
    public string Issuer { get; init; } = "mirage-api";
    public string Audience { get; init; } = "mirage-client";
    public string SigningKey { get; init; } = string.Empty;
    public int AccessTokenMinutes { get; init; } = 15;
    public int RefreshTokenDays { get; init; } = 30;
}
