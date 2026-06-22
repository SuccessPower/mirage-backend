namespace Mirage.Domain.Enums;

public enum RelationshipIntent { Friendship = 1, Dating = 2, Marriage = 3 }
public enum SubscriptionTier { Free = 1, Plus = 2, Premium = 3 }
public enum OrganisationStatus { Pending = 1, Approved = 2, Rejected = 3, Suspended = 4 }
public enum DateRequestStatus { Open = 1, Confirmed = 2, Completed = 3, Cancelled = 4, Expired = 5 }
public enum DateAcceptanceStatus { Pending = 1, Selected = 2, Declined = 3, Withdrawn = 4 }
public enum SessionType { Group = 1, Personal = 2 }
public enum SessionStatus { Requested = 1, Scheduled = 2, InProgress = 3, Completed = 4, Cancelled = 5 }
public enum MatchStatus { Active = 1, Closed = 2, Blocked = 3 }
public enum RecommendationStatus { Active = 1, Revoked = 2 }
public enum LikeType { Like = 1, SuperLike = 2 }
public enum TrustUnlockStatus { NotRequested = 1, Pending = 2, Unlocked = 3, Declined = 4 }
