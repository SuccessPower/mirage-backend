using Mirage.Domain.Enums;
#pragma warning disable CS8019 // suppress unused-using for enums referenced in record params

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
    string[] Interests,
    string? AvatarUrl = null);
public sealed record CreateOrganisationRequest(string Name, string Denomination, string Country, string RegistrationNumber);
public sealed record CreateRecommendationRequest(Guid RecommendedUserId, Guid? OrganisationId, string? Note);
public sealed record LikeProfileRequest(Guid TargetUserId, LikeType Type);
public sealed record CreateDateRequestRequest(
    string Activity,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string LocationArea,
    string? Note);
public sealed record RegisterCounsellorRequest(
    string InviteToken,
    string Email,
    string Password,
    string DisplayName,
    DateOnly DateOfBirth,
    string City,
    string Country,
    string Denomination,
    string Bio,
    int YearsExperience,
    string[] Specialisations,
    string[] Languages);

public sealed record RegisterMentorRequest(
    string Email,
    string Password,
    string DisplayName,
    DateOnly DateOfBirth,
    string City,
    string Country,
    string Denomination,
    string Bio,
    int YearsMarried,
    string Testimony,
    string[] AreasOfGuidance,
    string[] Languages);

public sealed record InviteCounsellorRequest(string Email);
public sealed record ApproveOrgRequest(string? Note);
public sealed record UpdateCounsellorProfileRequest(
    int YearsExperience,
    string[] Specialisations,
    string[] Languages,
    bool AcceptsFreeSessions,
    bool IsAnonymous);
public sealed record UpdateMentorProfileRequest(
    int YearsMarried,
    string Testimony,
    string[] AreasOfGuidance,
    string[] Languages,
    bool AcceptsFreeSessions,
    bool IsAnonymous);
public sealed record RequestMentorRequest(string Message);
public sealed record AddSessionNoteRequest(string Content);
public sealed record RateSessionRequest(int Rating, string? Comment);
public sealed record LogMilestoneRequest(MilestoneType Type, Guid? PartnerId, string? Note);
public sealed record SubmitDateFeedbackRequest(Guid ReviewedUserId, int Rating, string? Comment);
public sealed record SubmitContentReportRequest(
    ContentReportTargetType TargetType,
    Guid TargetId,
    ContentReportReason Reason,
    string? Details);
public sealed record ResolveReportRequest(string Resolution);

public sealed record BookSessionRequest(
    Guid CounsellorId,
    SessionType Type,
    DateTimeOffset ScheduledAt,
    bool CounsellorAnonymous,
    bool ClientAnonymous,
    string Topic);
