using Mirage.Domain.Enums;

namespace Mirage.Api.Contracts;

public sealed record LandingPageStatsResponse(
    int Profiles,
    int OpenDates,
    int Counsellors,
    int Organisations);

public sealed record GlobalSearchItemResponse(
    string Type, Guid Id, string Title, string Subtitle, string? ImageUrl, string Route);

public sealed record GlobalSearchResponse(IReadOnlyList<GlobalSearchItemResponse> Items);

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
    bool IsProfileComplete = true,
    string? CountryCode = null,
    string? ContinentCode = null,
    string? TimeZoneId = null,
    DiscoveryScope DiscoveryScope = DiscoveryScope.Continent,
    string[]? PreferredCountryCodes = null,
    DateOnly? WeddingAnniversaryDate = null,
    bool CelebrationOptOut = false,
    bool HasRequiredProfilePhotos = false,
    int RequiredProfilePhotoCount = 2,
    int DiscoveryProfilesRemaining = 0,
    bool IsProfilePhotoRequirementGrandfathered = false);

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
    bool RequireApproval,
    int MemberCount,
    int PostCount,
    bool IsMember,
    CommunityMemberRole? MyRole,
    DateTimeOffset CreatedAt,
    // Set for the auto-managed community of a church. IsMyChurch is true only for the one church
    // the viewer is an approved member of — an admin seat on some other church's community makes
    // them a moderator there, not a member of that church.
    Guid? OrganisationId = null,
    bool IsMyChurch = false);

// A person tagged with @ in a body, paired with the name to render in their place — the raw "@"
// is only a typing trigger and never shown back to readers.
public sealed record MentionedUserResponse(Guid UserId, string DisplayName);

// Someone the current user is allowed to tag with @ in a given context.
public sealed record CommunityMentionCandidateResponse(Guid UserId, string DisplayName, string? AvatarUrl);

public sealed record TestimonialResponse(
    Guid Id, Guid AuthorUserId, string AuthorName, string? AuthorAvatarUrl,
    Guid? TaggedUserId, string? TaggedUserName, string? TaggedUserAvatarUrl,
    string Title, string Body, string? ImageUrl, string? ImageUrl2, string? ImageUrl3,
    int ReadCount, int LikeCount, int CommentCount,
    bool LikedByMe, DateTimeOffset CreatedAt,
    IReadOnlyList<MentionedUserResponse>? MentionedUsers = null);

public sealed record TestimonialShareResponse(
    Guid Id, Guid AuthorUserId, string AuthorName, string? AuthorAvatarUrl,
    Guid? TaggedUserId, string? TaggedUserName, string? TaggedUserAvatarUrl,
    string Title, string Body, string? ImageUrl, string? ImageUrl2, string? ImageUrl3,
    int ReadCount, int LikeCount, int CommentCount, DateTimeOffset CreatedAt);

public sealed record CelebrationResponse(
    Guid Id, CelebrationType Type, string Title, string Body,
    Guid UserId, string DisplayName, string? AvatarUrl,
    Guid? PartnerUserId, string? PartnerDisplayName, string? PartnerAvatarUrl,
    DateTimeOffset CreatedAt, int WishCount);

public sealed record CelebrationWishResponse(
    Guid Id, Guid CelebrationId, Guid AuthorUserId, string AuthorName, string? AuthorAvatarUrl,
    string Body, DateTimeOffset CreatedAt);

public sealed record TestimonialCommentResponse(
    Guid Id, Guid TestimonialId, Guid AuthorUserId, string AuthorName, string? AuthorAvatarUrl,
    Guid? ParentCommentId, string Body, DateTimeOffset CreatedAt,
    int LikeCount, bool LikedByMe, int ReplyCount,
    IReadOnlyList<MentionedUserResponse>? MentionedUsers = null);

public sealed record CommunityMemberResponse(
    Guid Id,
    Guid UserId,
    string DisplayName,
    string? AvatarUrl,
    CommunityMemberRole Role,
    CommunityMemberStatus Status,
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
    string? ImageUrl2,
    string? ImageUrl3,
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
    bool IsHidden = false,
    IReadOnlyList<MentionedUserResponse>? MentionedUsers = null);

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
    bool IsHidden = false,
    IReadOnlyList<MentionedUserResponse>? MentionedUsers = null);

public sealed record CommunityCommentLocationResponse(Guid CommunityId, Guid PostId, Guid CommentId);

// An author on Hearth is a couple where both spouses are on the platform, and a single person
// where they are not — SpouseName is null in that case and clients render one avatar.
public sealed record HearthAuthorResponse(
    Guid UserId,
    string DisplayName,
    string? AvatarUrl,
    Guid? SpouseUserId,
    string? SpouseName,
    string? SpouseAvatarUrl,
    string CoupleName,
    string City,
    int? YearsMarried,
    string? OrgBadgeUrl = null,
    string? OrgName = null);

public sealed record HearthPostResponse(
    Guid Id,
    Guid CommunityId,
    string CircleName,
    bool IsHearthWide,
    HearthAuthorResponse Author,
    PostKind Kind,
    string Body,
    string? Place,
    IReadOnlyList<string> ImageUrls,
    int LoveCount,
    int AmenCount,
    int CommentCount,
    bool LovedByMe,
    bool AmenedByMe,
    bool IsMine,
    DateTimeOffset CreatedAt,
    IReadOnlyList<MentionedUserResponse>? MentionedUsers = null);

public sealed record HearthMeResponse(
    HearthAuthorResponse Author,
    bool HasSpouseOnPlatform,
    DateOnly? WeddingAnniversaryDate,
    int? DaysToAnniversary,
    int PostCount,
    int CircleCount,
    IReadOnlyList<HearthCircleResponse> Circles);

public sealed record HearthCircleResponse(Guid Id, string Name, string? AvatarUrl, int MemberCount,
    bool IsHearthWide);

public sealed record HearthMentionableResponse(Guid UserId, string DisplayName, string? AvatarUrl,
    string CoupleName, string City);

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
    string? Location,
    Guid? RouteId = null);

// Deliberately minimal — served without auth to link-preview crawlers (WhatsApp, etc.),
// so it must never carry anything beyond what's already public on a shared gathering link.
public sealed record DateRequestShareResponse(
    Guid Id,
    string Activity,
    string? Note,
    string? ImageUrl,
    string LocationArea,
    DateTimeOffset StartsAt,
    SectionCategory Category,
    string HostDisplayName);

public sealed record DateRequestCommentResponse(
    Guid Id,
    Guid DateRequestId,
    Guid AuthorUserId,
    string AuthorDisplayName,
    string? AuthorAvatarUrl,
    string Body,
    DateTimeOffset CreatedAt);

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
    Guid Couple1Id,
    Guid Couple2Id,
    IReadOnlyList<CoupleFriendParticipant> Participants,
    CoupleFriendshipStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastActivityAt);

public sealed record CompanionPromptResponse(
    Guid Id,
    string Text,
    string Category,
    CompanionCadence Cadence);

public sealed record CompanionTodayResponse(
    CompanionPromptResponse Prompt,
    CompanionCadence Cadence,
    DateTimeOffset NextDueAt,
    bool AnsweredToday);

public sealed record CompanionEntryResponse(
    Guid Id,
    Guid PromptId,
    string PromptText,
    Guid AuthorUserId,
    string AuthorDisplayName,
    string AnswerText,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PartnerReadAt = null);

public sealed record CompanionPartnerResponse(
    Guid Id,
    Guid PartnerUserId,
    string PartnerDisplayName,
    Guid RequestedByUserId,
    CompanionPartnerStatus Status,
    DateTimeOffset CreatedAt,
    string? PartnerAvatarUrl = null);

public sealed record CounsellingMessageResponse(
    Guid Id,
    Guid SessionId,
    Guid SenderId,
    string SenderName,
    string Content,
    MessageType Type,
    string? AttachmentUrl,
    DateTimeOffset CreatedAt,
    string? Ciphertext = null,
    string? EncryptionNonce = null,
    string? ClientMessageId = null,
    int EncryptionVersion = 0);

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
    string? ClientOrgName = null,
    PayoutStatus? PayoutStatus = null);

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
    string? OtherOrgName = null,
    Guid? ClosedByUserId = null,
    // Presence is in-memory and per-process; OtherLastSeenAt is only meaningful while offline.
    bool OtherIsOnline = false,
    DateTimeOffset? OtherLastSeenAt = null,
    Guid? BlockedByUserId = null);

// "Mixed" collapses Male-Female and Female-Male since actor/target order carries no meaning
// for a gender-pair breakdown; "Unknown" covers events where either party's Sex was unset.
public sealed record AdminAnalyticsGenderBucket(string GenderPair, int Count);

public sealed record AdminAnalyticsEventSummary(
    AnalyticsEventType EventType,
    int Total,
    IReadOnlyList<AdminAnalyticsGenderBucket> ByGenderPair);

public sealed record AdminAnalyticsSummaryResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<AdminAnalyticsEventSummary> Events);

public sealed record AdminAnalyticsTimeseriesPoint(DateOnly BucketStart, int Count);

public sealed record AdminAnalyticsTimeseriesResponse(
    AnalyticsEventType EventType,
    string Bucket,
    IReadOnlyList<AdminAnalyticsTimeseriesPoint> Points);

public sealed record AdminUserActivitySummary(
    int RegisteredUsers,
    int EnabledUsers,
    int SuspendedUsers,
    int ActiveUsers,
    int InactiveUsers,
    int NeverLoggedInUsers,
    DateTimeOffset InactivityCutoff);

public sealed record AdminTierSummary(SubscriptionTier Tier, int Users, int ActiveUsers, int InactiveUsers);

/// <summary>Headcount by gender. <see cref="Sex"/> is null for the "not stated" bucket, which covers members who
/// have not finished a profile as well as those who left the field blank.</summary>
public sealed record AdminGenderSummary(Sex? Sex, int Users, int ActiveUsers, int RegistrationsInPeriod);

public sealed record AdminCountrySummary(string Country, int Users, int ActiveUsers, int RegistrationsInPeriod);

public sealed record AdminRevenueSummary(
    string Source,
    string Currency,
    decimal GrossAmount,
    decimal PlatformRevenue,
    decimal ProviderPayable,
    int TransactionCount,
    decimal PaidOut,
    decimal OutstandingPayout);

public sealed record AdminComprehensiveAnalyticsResponse(
    DateTimeOffset From,
    DateTimeOffset To,
    string? Country,
    DateTimeOffset GeneratedAt,
    AdminUserActivitySummary Users,
    IReadOnlyList<AdminTierSummary> Tiers,
    IReadOnlyList<AdminGenderSummary> Genders,
    IReadOnlyList<AdminCountrySummary> Countries,
    IReadOnlyList<AdminRevenueSummary> Revenue,
    int NewRegistrations,
    int CompletedCounsellingSessions,
    int ApprovedCouples,
    int ApprovedOrganisations,
    int ApprovedCounsellors,
    int ApprovedMentors,
    int OpenContentReports);
