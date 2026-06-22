using Mirage.Domain.Enums;

namespace Mirage.Api.Contracts;

public sealed record RegisterRequest(
    string Email,
    string Password,
    string DisplayName,
    DateOnly DateOfBirth,
    string City,
    string Country,
    string Denomination,
    RelationshipIntent Intent,
    string Bio);

public sealed record LoginRequest(string Email, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record UpdateProfileRequest(
    string DisplayName,
    string City,
    string Country,
    string Denomination,
    RelationshipIntent Intent,
    string Bio,
    bool AnonymityEnabled,
    string[] Interests);
public sealed record CreateOrganisationRequest(string Name, string Denomination, string Country, string RegistrationNumber);
public sealed record CreateRecommendationRequest(Guid RecommendedUserId, Guid? OrganisationId, string? Note);
public sealed record LikeProfileRequest(Guid TargetUserId, LikeType Type);
public sealed record CreateDateRequestRequest(
    string Activity,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string LocationArea,
    string? Note);
public sealed record BookSessionRequest(
    Guid CounsellorId,
    SessionType Type,
    DateTimeOffset ScheduledAt,
    bool CounsellorAnonymous,
    bool ClientAnonymous,
    string Topic);
