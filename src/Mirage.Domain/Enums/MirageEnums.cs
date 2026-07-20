namespace Mirage.Domain.Enums;

// The three sections of the app; categorizes gatherings and discovery feeds, not people.
public enum SectionCategory { Friendship = 1, Dating = 2, Marriage = 3 }
public enum Sex { Male = 1, Female = 2 }
public enum RelationshipStatus { Single = 1, Divorced = 2, Widowed = 3, Separated = 4, Married = 5, Engaged = 6, InARelationship = 7 }
public enum CoupleStatus { Pending = 1, Approved = 2, Declined = 3 }
public enum CoupleFriendshipStatus { Active = 1, Ended = 2 }
public enum SkinTone { Fair = 1, Light = 2, Medium = 3, Tan = 4, Brown = 5, Dark = 6 }
public enum SubscriptionTier { Free = 1, Plus = 2, Premium = 3 }
public enum OrganisationStatus { Pending = 1, Approved = 2, Rejected = 3, Suspended = 4 }
public enum OrganisationMemberStatus { Pending = 1, Approved = 2, Rejected = 3, Removed = 4 }
public enum CommunityStatus { Active = 1, Archived = 2 }
public enum CommunityMemberRole { Owner = 1, Moderator = 2, Member = 3 }
public enum CommunityVoteColor { White = 1, Amber = 2, Green = 3, Red = 4 }
public enum DateRequestStatus { Open = 1, Confirmed = 2, Completed = 3, Cancelled = 4, Expired = 5 }
public enum DateAcceptanceStatus { Pending = 1, Selected = 2, Declined = 3, Withdrawn = 4 }
public enum SessionType { Group = 1, Personal = 2, Couples = 3 }
public enum SessionStatus { Requested = 1, Scheduled = 2, InProgress = 3, Completed = 4, Cancelled = 5, Declined = 6, AwaitingPayment = 7 }
public enum PaymentProvider { Paystack = 1, Flutterwave = 2 }
public enum PaymentMethod { Card = 1, BankTransfer = 2 }
public enum PaymentStatus { Pending = 1, Successful = 2, Failed = 3 }
public enum MatchStatus { Active = 1, Closed = 2, Blocked = 3, PendingRequest = 4 }
public enum MessageType { Text = 1, Image = 2 }
public enum RecommendationStatus { Active = 1, Revoked = 2 }
public enum LikeType { Like = 1, SuperLike = 2 }
public enum TrustUnlockStatus { NotRequested = 1, Pending = 2, Unlocked = 3, Declined = 4 }
public enum MentorRequestStatus { Pending = 1, Accepted = 2, Declined = 3, Withdrawn = 4 }
public enum MilestoneType { Dating = 1, Engaged = 2, Married = 3, Separated = 4 }
public enum ContentReportTargetType { Profile = 1, DateRequest = 2, Recommendation = 3, CounsellorProfile = 4 }
public enum ContentReportReason { Inappropriate = 1, FakeProfile = 2, Harassment = 3, Spam = 4, Other = 5 }
public enum ContentReportStatus { Pending = 1, UnderReview = 2, ActionTaken = 3, Dismissed = 4 }
public enum GatheringInviteKind { Community = 1, DateRequest = 2, OrganisationManager = 3 }
public enum GatheringInviteStatus { Pending = 1, Accepted = 2, Declined = 3 }
public enum VendorStatus { Pending = 1, Approved = 2, Rejected = 3, Suspended = 4 }

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
    Other = 17
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
    DateOfBirthInvalid = 30
}
