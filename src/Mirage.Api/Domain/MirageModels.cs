namespace Mirage.Api.Domain;

public enum RelationshipIntent
{
    Friendship,
    Dating,
    Marriage
}

public enum DateRequestStatus
{
    Open,
    Confirmed,
    Completed,
    Cancelled
}

public enum SessionType
{
    Group,
    Personal
}

public enum SessionStatus
{
    Requested,
    Scheduled,
    Completed,
    Cancelled
}

public sealed record Profile(
    Guid Id,
    string Name,
    int Age,
    string City,
    string Country,
    string Denomination,
    RelationshipIntent Intent,
    bool IsVerified,
    string? RecommendedBy,
    string Bio,
    IReadOnlyList<string> Interests);

public sealed record DateRequest(
    Guid Id,
    Guid RequestorId,
    string Activity,
    string DateTimeWindow,
    string LocationArea,
    string Note,
    DateRequestStatus Status,
    int CompatibleAcceptors);

public sealed record Counsellor(
    Guid Id,
    string DisplayName,
    string Denomination,
    string Organisation,
    int YearsExperience,
    bool IsAnonymous,
    bool IsApproved,
    IReadOnlyList<string> Specialisations,
    IReadOnlyList<string> Languages);

public sealed record Organisation(
    Guid Id,
    string Name,
    string Denomination,
    string Country,
    bool IsApproved,
    int ApprovedCounsellors,
    IReadOnlyList<SessionType> CounsellingModes,
    bool OffersFreeSessions);

public sealed record CounsellingSession(
    Guid Id,
    Guid CounsellorId,
    Guid ClientId,
    SessionType Type,
    DateTimeOffset ScheduledAt,
    SessionStatus Status,
    bool CounsellorAnonymous,
    bool ClientAnonymous,
    string Topic);

public sealed record CompanionCheckIn(
    Guid Id,
    string Prompt,
    string Cadence,
    string Category);
