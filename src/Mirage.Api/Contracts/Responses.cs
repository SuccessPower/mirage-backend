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
    string? Email,
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
    string? Occupation,
    DateTimeOffset CreatedAt,
    string[]? Roles = null,
    Guid? MentorProfileId = null,
    bool? HasApprovedMentorProfile = null,
    bool? IsChurchAdmin = null,
    bool? IsCounsellor = null);

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

public sealed record CommunityResponse(
    Guid Id,
    string Name,
    string Category,
    string Description,
    string? AvatarUrl,
    string? AvatarKey,
    Guid CreatedByUserId,
    CommunityStatus Status,
    int MemberCount,
    int PostCount,
    bool IsMember,
    CommunityMemberRole? MyRole,
    DateTimeOffset CreatedAt);

public sealed record CommunityMemberResponse(
    Guid Id,
    Guid UserId,
    string DisplayName,
    string? AvatarUrl,
    CommunityMemberRole Role,
    DateTimeOffset JoinedAt);

public sealed record CommunityPostResponse(
    Guid Id,
    Guid CommunityId,
    Guid AuthorUserId,
    string AuthorName,
    string? AuthorAvatarUrl,
    string Body,
    string? ImageUrl,
    int LikeCount,
    int CommentCount,
    bool LikedByMe,
    DateTimeOffset CreatedAt);

public sealed record CommunityPostCommentResponse(
    Guid Id,
    Guid PostId,
    Guid AuthorUserId,
    string AuthorName,
    string? AuthorAvatarUrl,
    Guid? ParentCommentId,
    string Body,
    Guid[] MentionedUserIds,
    int LikeCount,
    bool LikedByMe,
    DateTimeOffset CreatedAt);

public sealed record CommunityCommentLocationResponse(Guid CommunityId, Guid PostId, Guid CommentId);

public sealed record CommunityAvatarPresetResponse(string Key, string Label, string Url);

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

public sealed record MentorMessageResponse(
    Guid Id,
    Guid MentorRequestId,
    Guid SenderId,
    string SenderName,
    string Content,
    MessageType Type,
    string? AttachmentUrl,
    DateTimeOffset CreatedAt);

public sealed record MentorRequestDetailResponse(
    Guid Id,
    Guid MentorProfileId,
    Guid MentorUserId,
    string MentorName,
    string? MentorAvatarUrl,
    Guid MenteeUserId,
    string MenteeName,
    string? MenteeAvatarUrl,
    string Message,
    MentorRequestStatus Status,
    DateTimeOffset CreatedAt);

public sealed record MentorMenteeResponse(
    Guid MentorRequestId,
    Guid MenteeUserId,
    string DisplayName,
    string? AvatarUrl,
    DateTimeOffset AcceptedAt);

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

public sealed record CounsellingSessionResponse(
    Guid Id,
    Guid CounsellorId,
    Guid CounsellorUserId,
    string CounsellorDisplayName,
    string? CounsellorAvatarUrl,
    Guid ClientUserId,
    string ClientDisplayName,
    string? ClientAvatarUrl,
    SessionType Type,
    DateTimeOffset ScheduledAt,
    SessionStatus Status,
    string Topic,
    bool ClientAnonymous,
    TrustUnlockStatus TrustUnlockStatus,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

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
