using Mirage.Domain.Enums;

namespace Mirage.Api.Contracts;

public sealed record ApiResponse<T>(
    bool Success,
    string Message,
    T Data,
    ApiResponseMetadata Meta);

public sealed record ApiResponseMetadata(
    string TraceId,
    DateTimeOffset TimestampUtc,
    double ResponseTimeMs);

public sealed record AuthResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken);
public sealed record AccountSessionResponse(
    Guid Id,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    bool IsActive);
public sealed record ProfileResponse(
    Guid UserId,
    string DisplayName,
    int Age,
    string City,
    string Country,
    string Denomination,
    RelationshipIntent Intent,
    string Bio,
    bool IsVerified,
    bool IsRecommended,
    SubscriptionTier SubscriptionTier,
    string[] Interests,
    string? AvatarUrl,
    Sex? Sex,
    RelationshipStatus? RelationshipStatus,
    int? HeightInches,
    SkinTone? SkinTone,
    string? PreferredLanguage);

public sealed record MatchResponse(
    Guid Id,
    Guid OtherUserId,
    string OtherDisplayName,
    string? OtherAvatarUrl,
    bool OtherIsVerified,
    MatchStatus Status,
    Guid? ChatRequestedByUserId,
    DateTimeOffset MatchedAt,
    DateTimeOffset? LastActivityAt);
