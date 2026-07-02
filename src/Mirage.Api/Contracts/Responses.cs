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
    DateOnly DateOfBirth,
    string City,
    string Country,
    string Denomination,
    RelationshipIntent Intent,
    string Bio,
    bool IsVerified,
    bool IsRecommended,
    SubscriptionTier SubscriptionTier,
    bool AnonymityEnabled,
    string[] Interests,
    string? AvatarUrl,
    Sex? Sex,
    RelationshipStatus? RelationshipStatus,
    int? HeightInches,
    SkinTone? SkinTone,
    string? PreferredLanguage,
    string? Occupation);

public sealed record OrganisationMemberResponse(
    Guid Id,
    Guid UserId,
    string DisplayName,
    string? AvatarUrl,
    Guid? BranchId,
    OrganisationMemberStatus Status,
    Guid? AssignedMentorUserId,
    Guid? AssignedCounsellorUserId,
    DateTimeOffset CreatedAt);

public sealed record OrganisationBranchResponse(Guid Id, string Name, string City, string Country, string? Address);

public sealed record OrgEventResponse(
    Guid Id,
    Guid OrganisationId,
    Guid? BranchId,
    string Title,
    string? Description,
    string? ImageUrl,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Location,
    int? Capacity,
    int TicketsIssued);

public sealed record EventTicketResponse(Guid Id, Guid EventId, string EventTitle, string? EventImageUrl, DateTimeOffset StartsAt, string Code, DateTimeOffset? CheckedInAt);

public sealed record MentorPostResponse(Guid Id, Guid MentorProfileId, string Content, string? ImageUrl, DateTimeOffset CreatedAt);

public sealed record MentorGroupMessageResponse(
    Guid Id,
    Guid MentorProfileId,
    Guid SenderId,
    string SenderName,
    string Content,
    MessageType Type,
    string? AttachmentUrl,
    DateTimeOffset CreatedAt);

public sealed record MentorMeetingResponse(
    Guid Id,
    Guid MentorProfileId,
    Guid ScheduledByUserId,
    string Title,
    string MeetingLink,
    DateTimeOffset ScheduledAt,
    int? DurationMinutes);

public sealed record CalendarItemResponse(
    string Source,
    Guid SourceId,
    string Title,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    string? Link,
    string? Location);

public sealed record CoupleResponse(
    Guid Id,
    Guid OtherUserId,
    string OtherDisplayName,
    Guid RequestedByUserId,
    CoupleStatus Status,
    DateTimeOffset CreatedAt);

public sealed record CounsellingMessageResponse(
    Guid Id,
    Guid SessionId,
    Guid SenderId,
    string SenderName,
    string Content,
    MessageType Type,
    string? AttachmentUrl,
    DateTimeOffset CreatedAt);

public sealed record CounsellingMeetingResponse(
    Guid Id,
    Guid SessionId,
    Guid ScheduledByUserId,
    string Title,
    string MeetingLink,
    DateTimeOffset ScheduledAt,
    int? DurationMinutes);

public sealed record MatchResponse(
    Guid Id,
    Guid OtherUserId,
    string OtherDisplayName,
    string? OtherAvatarUrl,
    bool OtherIsVerified,
    RelationshipStatus? OtherRelationshipStatus,
    MatchStatus Status,
    Guid? ChatRequestedByUserId,
    DateTimeOffset MatchedAt,
    DateTimeOffset? LastActivityAt);
