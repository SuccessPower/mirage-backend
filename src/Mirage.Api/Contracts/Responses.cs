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
    string[] PhotoUrls,
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
    bool? IsCounsellor = null,
    bool? EmailConfirmed = null,
    string? OrganisationBadgeUrl = null,
    string? OrganisationName = null,
    bool IsProfileComplete = true);

public sealed record OrganisationMemberResponse(
    Guid Id,
    Guid UserId,
    string DisplayName,
    string? AvatarUrl,
    Guid? BranchId,
    OrganisationMemberStatus Status,
    Guid? AssignedMentorUserId,
    Guid? AssignedCounsellorUserId,
    DateTimeOffset CreatedAt,
    string? Description = null);

public sealed record OrganisationRosterMemberResponse(
    Guid UserId,
    string DisplayName,
    string? AvatarUrl);

public sealed record OrganisationBranchResponse(Guid Id, string Name, string City, string Country, string? Address);

// Used by the public church typeahead at signup — only Approved organisations are searchable,
// with their branches inlined so the UI can offer branch selection in the same step.
public sealed record ChurchSearchResultResponse(
    Guid Id,
    string Name,
    string Denomination,
    string Country,
    string? LogoUrl,
    string? WebsiteUrl,
    OrganisationBranchResponse[] Branches);

public sealed record AdminOrganisationSummaryResponse(
    Guid Id,
    string Name,
    string Denomination,
    string Country,
    string? LogoUrl,
    string? WebsiteUrl,
    OrganisationStatus Status,
    Guid AdminUserId,
    string? AdminDisplayName,
    string? AdminEmail,
    int ApprovedMemberCount,
    int PendingMemberCount,
    int BranchCount,
    int ManagerCount,
    DateTimeOffset CreatedAt);

// A user's org badge — shown next to their display name wherever it appears, like a Twitter
// verified checkmark. Populated via IMirageDbContextExtensions.GetOrgBadgesAsync.
public sealed record OrgBadge(string? LogoUrl, string OrganisationName);

public sealed record VendorResponse(
    Guid Id,
    Guid OwnerUserId,
    string BusinessName,
    VendorCategory Category,
    string Description,
    string Email,
    string Phone,
    string Address,
    string City,
    string Country,
    string[] PhotoUrls,
    VendorStatus Status,
    DateTimeOffset CreatedAt);

public sealed record OrganisationManagerResponse(
    Guid UserId,
    string DisplayName,
    string? AvatarUrl,
    Guid? BranchId,
    string? BranchName,
    bool IsOriginalOwner);

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

public sealed record PublicEventResponse(
    Guid Id,
    Guid OrganisationId,
    string OrganisationName,
    Guid? BranchId,
    string? BranchName,
    string Title,
    string? Description,
    string? ImageUrl,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Location,
    int? Capacity,
    int TicketsIssued,
    bool IsRegistered);

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
    DateTimeOffset JoinedAt,
    string? OrgBadgeUrl = null,
    string? OrgName = null);

public sealed record GatheringInviteResponse(
    Guid Id,
    GatheringInviteKind Kind,
    Guid TargetId,
    string TargetTitle,
    Guid InviterUserId,
    string InviterDisplayName,
    string? InviterAvatarUrl,
    GatheringInviteStatus Status,
    DateTimeOffset CreatedAt,
    string? InviterOrgBadgeUrl = null,
    string? InviterOrgName = null);

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
    DateTimeOffset CreatedAt,
    string? AuthorOrgBadgeUrl = null,
    string? AuthorOrgName = null,
    int UpvoteCount = 0,
    int DownvoteCount = 0,
    sbyte? MyVote = null,
    CommunityVoteColor VoteColor = CommunityVoteColor.White,
    bool IsHidden = false);

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
    bool IsEdited,
    bool IsDeleted,
    DateTimeOffset CreatedAt,
    string? AuthorOrgBadgeUrl = null,
    string? AuthorOrgName = null,
    int UpvoteCount = 0,
    int DownvoteCount = 0,
    sbyte? MyVote = null,
    CommunityVoteColor VoteColor = CommunityVoteColor.White,
    bool IsHidden = false);

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
    DateTimeOffset CreatedAt,
    string? MentorPhoneNumber = null,
    string? MentorOrgBadgeUrl = null,
    string? MentorOrgName = null,
    string? MenteeOrgBadgeUrl = null,
    string? MenteeOrgName = null);

public sealed record MentorMenteeResponse(
    Guid MentorRequestId,
    Guid MenteeUserId,
    string DisplayName,
    string? AvatarUrl,
    DateTimeOffset AcceptedAt,
    string? OrgBadgeUrl = null,
    string? OrgName = null);

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
    DateTimeOffset CreatedAt,
    string? OtherOrgBadgeUrl = null,
    string? OtherOrgName = null);

public sealed record CouplePartnerSummary(
    Guid UserId,
    string DisplayName,
    int Age,
    string? AvatarUrl,
    string Bio,
    string City,
    string Country,
    string Denomination,
    bool IsVerified,
    string? OrgBadgeUrl = null,
    string? OrgName = null);

public sealed record CoupleCardResponse(
    Guid CoupleId,
    CouplePartnerSummary Partner1,
    CouplePartnerSummary Partner2,
    bool AlreadyFriends,
    DateTimeOffset? MarriedSince);

public sealed record CoupleFriendParticipant(
    Guid UserId,
    string DisplayName,
    string? AvatarUrl,
    bool IsVerified);

public sealed record CoupleFriendshipResponse(
    Guid Id,
    Guid CoupleId,
    Guid FriendUserId,
    CoupleFriendParticipant Friend,
    CoupleFriendParticipant Partner1,
    CoupleFriendParticipant Partner2,
    CoupleFriendshipStatus Status,
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
    string Room,
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
    Guid? PartnerUserId,
    bool PartnerAccepted,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    string? CounsellorPhoneNumber = null,
    Guid? PaymentId = null,
    bool HasRating = false,
    string? CounsellorOrgBadgeUrl = null,
    string? CounsellorOrgName = null,
    string? ClientOrgBadgeUrl = null,
    string? ClientOrgName = null);

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
    DateTimeOffset? LastActivityAt,
    string? OtherOrgBadgeUrl = null,
    string? OtherOrgName = null);
