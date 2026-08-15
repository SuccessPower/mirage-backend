namespace Mirage.Domain.Enums;

// The three sections of the app; categorizes gatherings and discovery feeds, not people.
public enum SectionCategory { Friendship = 1, Dating = 2, Marriage = 3 }
public enum Sex { Male = 1, Female = 2 }
public enum RelationshipStatus { Single = 1, Divorced = 2, Widowed = 3, Separated = 4, Married = 5, Engaged = 6, InARelationship = 7 }
public enum CoupleStatus { Pending = 1, Approved = 2, Declined = 3 }
public enum CoupleFriendshipStatus { Active = 1, Ended = 2 }
public enum SkinTone { Fair = 1, Light = 2, Medium = 3, Tan = 4, Brown = 5, Dark = 6 }
public enum SubscriptionTier { Free = 1, Plus = 2, Premium = 3 }
public enum DiscoveryScope { Country = 1, Continent = 2, Worldwide = 3 }
public enum OrganisationStatus { Pending = 1, Approved = 2, Rejected = 3, Suspended = 4 }
public enum OrganisationMemberStatus { Pending = 1, Approved = 2, Rejected = 3, Removed = 4 }
public enum CommunityStatus { Active = 1, Archived = 2 }
public enum CommunityMemberRole { Owner = 1, Moderator = 2, Member = 3 }
public enum CommunityMemberStatus { Pending = 1, Approved = 2, Rejected = 3, Removed = 4 }
public enum CommunityVoteColor { White = 1, Amber = 2, Green = 3, Red = 4 }
// What a post is for. Drives the Hearth feed filters and how a post is presented — a Milestone
// reads differently from a Tuesday photo. Community posts default to Everyday.
public enum PostKind { Everyday = 1, Milestone = 2, Prayer = 3, Question = 4 }
// A reaction is either warmth (Love) or agreement in prayer (Amen). Community posts only ever
// carry Love, so the existing community like/unlike endpoints stay a plain toggle.
public enum PostReactionKind { Love = 1, Amen = 2 }
public enum DateRequestStatus { Open = 1, Confirmed = 2, Completed = 3, Cancelled = 4, Expired = 5 }
public enum DateAcceptanceStatus { Pending = 1, Selected = 2, Declined = 3, Withdrawn = 4 }
public enum SessionType { Group = 1, Personal = 2, Couples = 3 }
public enum SessionStatus { Requested = 1, Scheduled = 2, InProgress = 3, Completed = 4, Cancelled = 5, Declined = 6, AwaitingPayment = 7 }
public enum PaymentProvider { Paystack = 1, Flutterwave = 2 }
public enum PaymentMethod { Card = 1, BankTransfer = 2 }
public enum PaymentStatus { Pending = 1, Successful = 2, Failed = 3, Refunded = 4 }
// Cancelled: the payment behind the payout was refunded, so the counsellor is never paid for it.
public enum PayoutStatus { NotApplicable = 0, Held = 1, AwaitingApproval = 2, Processing = 3, Paid = 4, Failed = 5, Cancelled = 6 }
// Who or what triggered a refund. Recorded on the payment so support can tell an automatic
// cancellation refund from one an admin issued by hand.
public enum RefundReason { ClientCancelled = 1, CounsellorCancelled = 2, CounsellorNoShow = 3, TechnicalFailure = 4, AdminDiscretion = 5 }
public enum MatchStatus { Active = 1, Closed = 2, Blocked = 3, PendingRequest = 4 }
// Voice and Gif both carry their media in AttachmentUrl like Image does. Gif points at a
// third-party (Tenor) CDN rather than our own Cloudinary account, so it is deliberately a
// distinct type — the client renders it without the tap-to-zoom treatment photos get, and it
// is the one attachment kind we never re-host.
public enum MessageType { Text = 1, Image = 2, Voice = 3, Gif = 4 }
public enum RecommendationStatus { Active = 1, Revoked = 2 }
public enum LikeType { Like = 1, SuperLike = 2 }
public enum TrustUnlockStatus { NotRequested = 1, Pending = 2, Unlocked = 3, Declined = 4 }
public enum MentorRequestStatus { Pending = 1, Accepted = 2, Declined = 3, Withdrawn = 4 }
public enum MilestoneType { Dating = 1, Engaged = 2, Married = 3, Separated = 4 }
public enum ContentReportTargetType { Profile = 1, DateRequest = 2, Recommendation = 3, CounsellorProfile = 4 }
public enum ContentReportReason { Inappropriate = 1, FakeProfile = 2, Harassment = 3, Spam = 4, Other = 5 }
public enum ContentReportStatus { Pending = 1, UnderReview = 2, ActionTaken = 3, Dismissed = 4 }
// The two admin warning emails (see AccountWarning) — distinct because the remedy differs: a
// profile edit vs. a behaviour change.
public enum WarningType { Profile = 1, Conduct = 2 }
public enum GatheringInviteKind { Community = 1, DateRequest = 2, OrganisationManager = 3 }
public enum GatheringInviteStatus { Pending = 1, Accepted = 2, Declined = 3 }
public enum VendorStatus { Pending = 1, Approved = 2, Rejected = 3, Suspended = 4 }
public enum CompanionCadence { Daily = 1, Weekly = 2, BiWeekly = 3, Monthly = 4 }
public enum CompanionPartnerStatus { Pending = 1, Approved = 2, Declined = 3 }

// Append-only admin analytics log — never inferred from live Match/Like/DateRequest state
// so gender-pair counts stay accurate even after a profile's gender or a match's status changes.
public enum AnalyticsEventType
{
    ProfileLiked = 1,
    ChatRequested = 2,
    ChatApproved = 3,
    ConversationClosed = 4,
    ConversationBlocked = 5,
    DateRequestCreated = 6,
    DateRequestAccepted = 7
}

public enum VendorCategory
{
    Photography = 1,
    Catering = 2,
    Venue = 3,
    Decor = 4,
    MakeupAndBeauty = 5,
    MusicAndEntertainment = 6,
    Planning = 7,
    Attire = 8,
    Jewellery = 10,
    Other = 9,
    Mc = 11,
    Dj = 12,
    LiveBand = 13,
    EventPlanner = 14
}

// Drives the Denomination dropdown on signup. UserProfile.Denomination stays a plain string column
// (avoids a data migration for existing free-text values) — registration validates the submitted
// value against this enum's names instead of constraining the column itself.
public enum ChristianDenomination
{
    NonDenominational = 1,
    Catholic = 2,
    Orthodox = 3,
    Anglican = 4,
    Baptist = 5,
    Methodist = 6,
    Lutheran = 7,
    Presbyterian = 8,
    Pentecostal = 9,
    Charismatic = 10,
    Evangelical = 11,
    Adventist = 12,
    Reformed = 13,
    Apostolic = 14,
    Quaker = 15,
    Anabaptist = 16,
    Other = 17,
    Contemporary = 18
}

public enum NotificationType
{
    NewLike = 1,
    NewMatch = 2,
    DateRequestAccepted = 3,
    DateRequestSelected = 4,
    MentorRequestReceived = 5,
    MentorRequestAccepted = 6,
    MentorRequestDeclined = 7,
    SessionBooked = 8,
    SessionAccepted = 9,
    SessionDeclined = 10,
    NewMessage = 11,
    ChatRequestReceived = 12,
    ChatRequestApproved = 13,
    OrganisationApproved = 14,
    OrganisationRejected = 15,
    CounsellorApproved = 16,
    MembershipApproved = 17,
    MembershipRejected = 18,
    Mention = 19,
    CounsellorApplicationReceived = 20,
    PaymentConfirmed = 21,
    GatheringInviteReceived = 22,
    GatheringInviteAccepted = 23,
    GatheringInviteDeclined = 24,
    ProfileVerified = 25,
    VendorApproved = 26,
    VendorRejected = 27,
    CoupleFriendshipCreated = 28,
    CoupleFriendshipEnded = 29,
    DateOfBirthInvalid = 30,
    ConversationEnded = 31,
    ProfileVisited = 32,
    CommunityMembershipApproved = 33,
    CommunityMembershipRejected = 34,
    CommunityMemberRemoved = 35,
    CompanionPromptDue = 36,
    CompanionPartnerInvite = 37,
    CompanionPartnerApproved = 38,
    ProfilePhotosRequired = 39,
    ProfilePhotosComplete = 40,
    PostReaction = 41,
    PostComment = 42,
    PaymentRefunded = 43,
    // Sent to the admin who issued an AccountWarning when its deadline passes with the account
    // still active — see WarningReminderService.
    WarningDeadlinePassed = 44,
    // The member-facing pair for AdminEndpoints.HideUser/UnhideUser — distinct from suspension:
    // the account can still sign in, it's just hidden from other members in the meantime.
    ProfileHidden = 45,
    ProfileVisibleAgain = 46
}

public enum CelebrationType
{
    Birthday = 1,
    Anniversary = 2
}

public enum DevicePlatform { Android = 1, IOS = 2, Web = 3 }

public enum NewsletterStatus { Draft = 1, Scheduled = 2, Sending = 3, Sent = 4, Cancelled = 5, Failed = 6, InReview = 7, Approved = 8 }
public enum NewsletterReviewDecision { Comment = 1, Approved = 2, ChangesRequested = 3 }
public enum NewsletterDeliveryStatus { Pending = 1, Sent = 2, Failed = 3 }
