using Microsoft.EntityFrameworkCore;
using Mirage.Domain.Entities;

namespace Mirage.Application.Abstractions;

public interface IMirageDbContext
{
    DbSet<UserProfile> Profiles { get; }
    DbSet<Organisation> Organisations { get; }
    DbSet<CounsellorProfile> Counsellors { get; }
    DbSet<Recommendation> Recommendations { get; }
    DbSet<UserLike> Likes { get; }
    DbSet<Match> Matches { get; }
    DbSet<DateRequest> DateRequests { get; }
    DbSet<DateRequestAcceptance> DateRequestAcceptances { get; }
    DbSet<DateRequestComment> DateRequestComments { get; }
    DbSet<CounsellingSession> CounsellingSessions { get; }
    DbSet<AnonymityAuditLog> AnonymityAuditLogs { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<CounsellorInvite> CounsellorInvites { get; }
    DbSet<MentorProfile> Mentors { get; }
    DbSet<MentorRequest> MentorRequests { get; }
    DbSet<SessionNote> SessionNotes { get; }
    DbSet<SessionRating> SessionRatings { get; }
    DbSet<MilestoneLog> MilestoneLogs { get; }
    DbSet<DateFeedback> DateFeedbacks { get; }
    DbSet<ContentReport> ContentReports { get; }
    DbSet<AccountWarning> AccountWarnings { get; }
    DbSet<Message> Messages { get; }
    DbSet<ChatEncryptionIdentity> ChatEncryptionIdentities { get; }
    DbSet<ChatDeviceLink> ChatDeviceLinks { get; }
    DbSet<CounsellingKeyEnvelope> CounsellingKeyEnvelopes { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<DeviceToken> DeviceTokens { get; }
    DbSet<OrganisationBranch> OrganisationBranches { get; }
    DbSet<OrganisationMember> OrganisationMembers { get; }
    DbSet<OrganisationManager> OrganisationManagers { get; }
    DbSet<OrgEvent> OrgEvents { get; }
    DbSet<EventTicket> EventTickets { get; }
    DbSet<Community> Communities { get; }
    DbSet<CommunityMember> CommunityMembers { get; }
    DbSet<CommunityPost> CommunityPosts { get; }
    DbSet<CommunityPostLike> CommunityPostLikes { get; }
    DbSet<CommunityPostVote> CommunityPostVotes { get; }
    DbSet<CommunityPostComment> CommunityPostComments { get; }
    DbSet<CommunityPostCommentLike> CommunityPostCommentLikes { get; }
    DbSet<CommunityPostCommentVote> CommunityPostCommentVotes { get; }
    DbSet<MentorPost> MentorPosts { get; }
    DbSet<MentorGroupMessage> MentorGroupMessages { get; }
    DbSet<MentorMeeting> MentorMeetings { get; }
    DbSet<MentorMessage> MentorMessages { get; }
    DbSet<Couple> Couples { get; }
    DbSet<CoupleFriendship> CoupleFriendships { get; }
    DbSet<CoupleFriendMessage> CoupleFriendMessages { get; }
    DbSet<ProfileVote> ProfileVotes { get; }
    DbSet<ProfileVisit> ProfileVisits { get; }
    DbSet<DiscoveryProfileView> DiscoveryProfileViews { get; }
    DbSet<OrganisationAdminInvite> OrganisationAdminInvites { get; }
    DbSet<CounsellingMessage> CounsellingMessages { get; }
    DbSet<CounsellingMeeting> CounsellingMeetings { get; }
    DbSet<Payment> Payments { get; }
    DbSet<GatheringInvite> GatheringInvites { get; }
    DbSet<Vendor> Vendors { get; }
    DbSet<AnalyticsEvent> AnalyticsEvents { get; }
    DbSet<Testimonial> Testimonials { get; }
    DbSet<TestimonialRead> TestimonialReads { get; }
    DbSet<TestimonialLike> TestimonialLikes { get; }
    DbSet<TestimonialComment> TestimonialComments { get; }
    DbSet<TestimonialCommentLike> TestimonialCommentLikes { get; }
    DbSet<CompanionPrompt> CompanionPrompts { get; }
    DbSet<CompanionPartner> CompanionPartners { get; }
    DbSet<CompanionEntry> CompanionEntries { get; }
    DbSet<CompanionSubscription> CompanionSubscriptions { get; }
    DbSet<CelebrationEntry> CelebrationEntries { get; }
    DbSet<CelebrationWish> CelebrationWishes { get; }
    DbSet<CelebrationWishLike> CelebrationWishLikes { get; }
    DbSet<Newsletter> Newsletters { get; }
    DbSet<NewsletterDelivery> NewsletterDeliveries { get; }
    DbSet<NewsletterReview> NewsletterReviews { get; }
    DbSet<NewsletterLike> NewsletterLikes { get; }
    DbSet<NewsletterComment> NewsletterComments { get; }
    DbSet<NewsletterCommentLike> NewsletterCommentLikes { get; }
    DbSet<PlatformManagerInvite> PlatformManagerInvites { get; }
    DbSet<PlatformPricing> PlatformPricing { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
