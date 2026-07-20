using Mirage.Domain.Enums;
#pragma warning disable CS8019 // suppress unused-using for enums referenced in record params

namespace Mirage.Api.Contracts;

public sealed record RegisterRequest(
    string Email,
    string Password,
    string ConfirmPassword,
    string DisplayName,
    DateOnly DateOfBirth,
    string City,
    string Country,
    string Denomination,
    string Bio,
    Sex? Sex = null,
    RelationshipStatus? RelationshipStatus = null,
    string? Occupation = null,
    // Church selection at signup — pick an existing organisation/branch, or propose a new one
    // (submitted for PlatformAdmin review, same as CreateOrganisationRequest) when it isn't listed.
    Guid? OrganisationId = null,
    Guid? BranchId = null,
    string? NewOrganisationName = null,
    string? NewOrganisationRegistrationNumber = null,
    string? NewBranchName = null,
    string? NewBranchCity = null);

public sealed record LoginRequest(string Email, string Password);
public sealed record GoogleAuthRequest(string IdToken);
public sealed record RefreshRequest(string RefreshToken);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record ForgotPasswordRequest(string Email);
public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword);
public sealed record ConfirmEmailRequest(string Email, string Token);
public sealed record ResendConfirmationEmailRequest(string Email);
public sealed record DeactivateAccountRequest(string CurrentPassword);
public sealed record DeleteAccountRequest(string CurrentPassword);
public sealed record UpdateProfileRequest(
    string DisplayName,
    string City,
    string Country,
    string Denomination,
    string Bio,
    bool AnonymityEnabled,
    string[] Interests,
    string? AvatarUrl = null,
    Sex? Sex = null,
    RelationshipStatus? RelationshipStatus = null,
    int? HeightInches = null,
    SkinTone? SkinTone = null,
    string? PreferredLanguage = null,
    string? Occupation = null);
public sealed record SetProfilePhotosRequest(string[] PhotoUrls);
public sealed record CompleteProfileRequest(
    DateOnly DateOfBirth,
    string City,
    string Country,
    string Denomination,
    string Bio,
    Sex? Sex = null,
    RelationshipStatus? RelationshipStatus = null,
    string? Occupation = null,
    Guid? OrganisationId = null,
    Guid? BranchId = null,
    string? NewOrganisationName = null,
    string? NewOrganisationRegistrationNumber = null,
    string? NewBranchName = null,
    string? NewBranchCity = null);
public sealed record JoinChurchRequest(
    Guid? OrganisationId = null,
    Guid? BranchId = null,
    string? NewOrganisationName = null,
    string? NewOrganisationRegistrationNumber = null,
    string? NewBranchName = null,
    string? NewBranchCity = null);
public sealed record CreateOrganisationRequest(
    string Name, string Denomination, string Country, string RegistrationNumber, string? InviteToken = null,
    string? LogoUrl = null, string? WebsiteUrl = null);
public sealed record UpdateOrganisationDetailsRequest(string? LogoUrl, string? WebsiteUrl);
public sealed record InviteOrganisationAdminRequest(string Email);
public sealed record JoinOrganisationRequest(Guid? BranchId, string? Description = null);
public sealed record AssignMemberRequest(Guid? MentorUserId, Guid? CounsellorUserId);
public sealed record CreateBranchRequest(string Name, string City, string Country, string? Address);
public sealed record CreateVendorRequest(
    string BusinessName, VendorCategory Category, string Description, string Email, string Phone,
    string Address, string City, string Country);
public sealed record UpdateVendorRequest(
    string BusinessName, VendorCategory Category, string Description, string Email, string Phone,
    string Address, string City, string Country);
public sealed record SetVendorPhotosRequest(string[] PhotoUrls);
public sealed record CreateMentorPostRequest(string Content, string? ImageUrl);
public sealed record SendMentorGroupMessageRequest(string Content, MessageType Type = MessageType.Text, string? AttachmentUrl = null);
public sealed record ScheduleMentorMeetingRequest(string Title, string MeetingLink, DateTimeOffset ScheduledAt, int? DurationMinutes);
public sealed record CreateCommunityRequest(
    string Name,
    string Category,
    string Description,
    string? AvatarUrl = null,
    string? AvatarKey = null);
public sealed record UpdateCommunityAvatarRequest(string? AvatarUrl, string? AvatarKey);
public sealed record UpdateCommunityMemberRoleRequest(CommunityMemberRole Role);
public sealed record CastVoteRequest(sbyte Value);
public sealed record CreateCommunityPostRequest(string? Body, string? ImageUrl = null);
public sealed record CreateCommunityPostCommentRequest(string Body, Guid? ParentCommentId = null,
    Guid[]? MentionedUserIds = null);
public sealed record InviteToGatheringRequest(string EmailOrUsername);
public sealed record UpdateCommunityPostCommentRequest(string Body);

public sealed record CreateEventRequest(
    string Title,
    string? Description,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string Location,
    string? ImageUrl,
    int? Capacity,
    Guid? BranchId);
public sealed record CreateRecommendationRequest(Guid RecommendedUserId, Guid? OrganisationId, string? Note);
public sealed record LikeProfileRequest(Guid TargetUserId, LikeType Type, SectionCategory Category = SectionCategory.Dating);
public sealed record CreateDateRequestRequest(
    string Activity,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    string LocationArea,
    string? Note,
    SectionCategory Category = SectionCategory.Dating,
    int Capacity = 1,
    string? ItemsToBring = null,
    string? ImageUrl = null);
public sealed record SendChatMessageRequest(string Content, MessageType Type = MessageType.Text, string? AttachmentUrl = null);
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

public sealed record RegisterIndependentCounsellorRequest(
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
    string[] Languages,
    string[]? VerificationDocumentUrls = null);

public sealed record UpdateVerificationDocumentsRequest(string[] DocumentUrls);
public sealed record RejectCounsellorRequest(string Reason);
public sealed record ApplyCounsellorRequest(
    int YearsExperience,
    string[] Specialisations,
    string[] Languages,
    Guid? OrganisationId = null,
    string[]? VerificationDocumentUrls = null);
public sealed record ApplyMentorRequest(
    int YearsMarried,
    string Testimony,
    string[] AreasOfGuidance,
    string[] Languages);
public sealed record ResolveBankAccountRequest(string BankCode, string AccountNumber);
public sealed record SaveBankAccountRequest(string BankCode, string BankName, string AccountNumber, string AccountName);
public sealed record InviteCoupleRequest(string PartnerEmail);
public sealed record SendCounsellingMessageRequest(string Content, MessageType Type = MessageType.Text, string? AttachmentUrl = null);
public sealed record ScheduleCounsellingMeetingRequest(string Title, DateTimeOffset ScheduledAt, int? DurationMinutes);

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
public sealed record InviteManagerRequest(string EmailOrUsername, Guid? BranchId);
public sealed record ApproveOrgRequest(string? Note);
public sealed record UpdateCounsellorProfileRequest(
    int YearsExperience,
    string[] Specialisations,
    string[] Languages,
    bool AcceptsFreeSessions,
    bool IsAnonymous,
    string? PhoneNumber = null,
    decimal? PriceAmount = null,
    string? PriceCurrency = null,
    bool SupportsVoiceCalls = true,
    bool SupportsVideoCalls = true);
public sealed record UpdateMentorProfileRequest(
    int YearsMarried,
    string Testimony,
    string[] AreasOfGuidance,
    string[] Languages,
    bool AcceptsFreeSessions,
    bool AllowMenteesToSeeEachOther,
    string? PhoneNumber = null);
public sealed record RequestMentorRequest(string Message);
public sealed record SendMentorMessageRequest(string Content, MessageType Type = MessageType.Text, string? AttachmentUrl = null);
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
    bool ClientAnonymous,
    string Topic,
    string? PartnerEmail = null);

public sealed record InitializePaymentRequest(PaymentProvider Provider, PaymentMethod Method);
public sealed record ResetWelcomeEmailsRequest(string[] Emails);
