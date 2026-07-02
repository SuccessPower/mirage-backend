namespace Mirage.Domain.Enums;

public enum RelationshipIntent { Friendship = 1, Dating = 2, Marriage = 3 }
public enum Sex { Male = 1, Female = 2 }
public enum RelationshipStatus { Single = 1, Divorced = 2, Widowed = 3, Separated = 4, Married = 5 }
public enum CoupleStatus { Pending = 1, Approved = 2, Declined = 3 }
public enum SkinTone { Fair = 1, Light = 2, Medium = 3, Tan = 4, Brown = 5, Dark = 6 }
public enum SubscriptionTier { Free = 1, Plus = 2, Premium = 3 }
public enum OrganisationStatus { Pending = 1, Approved = 2, Rejected = 3, Suspended = 4 }
public enum OrganisationMemberStatus { Pending = 1, Approved = 2, Rejected = 3, Removed = 4 }
public enum DateRequestStatus { Open = 1, Confirmed = 2, Completed = 3, Cancelled = 4, Expired = 5 }
public enum DateAcceptanceStatus { Pending = 1, Selected = 2, Declined = 3, Withdrawn = 4 }
public enum SessionType { Group = 1, Personal = 2, Couples = 3 }
public enum SessionStatus { Requested = 1, Scheduled = 2, InProgress = 3, Completed = 4, Cancelled = 5, Declined = 6 }
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
    ChatRequestApproved = 13
}
