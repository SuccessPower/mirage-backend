using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;
using Mirage.Infrastructure.Identity;

namespace Mirage.Infrastructure.Persistence;

public sealed class MirageDbContext(DbContextOptions<MirageDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options), IMirageDbContext
{
    public DbSet<UserProfile> Profiles => Set<UserProfile>();
    public DbSet<Organisation> Organisations => Set<Organisation>();
    public DbSet<CounsellorProfile> Counsellors => Set<CounsellorProfile>();
    public DbSet<Recommendation> Recommendations => Set<Recommendation>();
    public DbSet<UserLike> Likes => Set<UserLike>();
    public DbSet<Match> Matches => Set<Match>();
    public DbSet<DateRequest> DateRequests => Set<DateRequest>();
    public DbSet<DateRequestAcceptance> DateRequestAcceptances => Set<DateRequestAcceptance>();
    public DbSet<DateRequestComment> DateRequestComments => Set<DateRequestComment>();
    public DbSet<CounsellingSession> CounsellingSessions => Set<CounsellingSession>();
    public DbSet<AnonymityAuditLog> AnonymityAuditLogs => Set<AnonymityAuditLog>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<CounsellorInvite> CounsellorInvites => Set<CounsellorInvite>();
    public DbSet<MentorProfile> Mentors => Set<MentorProfile>();
    public DbSet<MentorRequest> MentorRequests => Set<MentorRequest>();
    public DbSet<SessionNote> SessionNotes => Set<SessionNote>();
    public DbSet<SessionRating> SessionRatings => Set<SessionRating>();
    public DbSet<MilestoneLog> MilestoneLogs => Set<MilestoneLog>();
    public DbSet<DateFeedback> DateFeedbacks => Set<DateFeedback>();
    public DbSet<ContentReport> ContentReports => Set<ContentReport>();
    public DbSet<AccountWarning> AccountWarnings => Set<AccountWarning>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<ChatEncryptionIdentity> ChatEncryptionIdentities => Set<ChatEncryptionIdentity>();
    public DbSet<ChatDeviceLink> ChatDeviceLinks => Set<ChatDeviceLink>();
    public DbSet<CounsellingKeyEnvelope> CounsellingKeyEnvelopes => Set<CounsellingKeyEnvelope>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();
    public DbSet<OrganisationBranch> OrganisationBranches => Set<OrganisationBranch>();
    public DbSet<OrganisationMember> OrganisationMembers => Set<OrganisationMember>();
    public DbSet<OrganisationManager> OrganisationManagers => Set<OrganisationManager>();
    public DbSet<OrgEvent> OrgEvents => Set<OrgEvent>();
    public DbSet<EventTicket> EventTickets => Set<EventTicket>();
    public DbSet<Community> Communities => Set<Community>();
    public DbSet<CommunityMember> CommunityMembers => Set<CommunityMember>();
    public DbSet<CommunityPost> CommunityPosts => Set<CommunityPost>();
    public DbSet<CommunityPostLike> CommunityPostLikes => Set<CommunityPostLike>();
    public DbSet<CommunityPostVote> CommunityPostVotes => Set<CommunityPostVote>();
    public DbSet<CommunityPostComment> CommunityPostComments => Set<CommunityPostComment>();
    public DbSet<CommunityPostCommentLike> CommunityPostCommentLikes => Set<CommunityPostCommentLike>();
    public DbSet<CommunityPostCommentVote> CommunityPostCommentVotes => Set<CommunityPostCommentVote>();
    public DbSet<MentorPost> MentorPosts => Set<MentorPost>();
    public DbSet<MentorGroupMessage> MentorGroupMessages => Set<MentorGroupMessage>();
    public DbSet<MentorMeeting> MentorMeetings => Set<MentorMeeting>();
    public DbSet<MentorMessage> MentorMessages => Set<MentorMessage>();
    public DbSet<Couple> Couples => Set<Couple>();
    public DbSet<CoupleFriendship> CoupleFriendships => Set<CoupleFriendship>();
    public DbSet<CoupleFriendMessage> CoupleFriendMessages => Set<CoupleFriendMessage>();
    public DbSet<ProfileVote> ProfileVotes => Set<ProfileVote>();
    public DbSet<ProfileVisit> ProfileVisits => Set<ProfileVisit>();
    public DbSet<DiscoveryProfileView> DiscoveryProfileViews => Set<DiscoveryProfileView>();
    public DbSet<OrganisationAdminInvite> OrganisationAdminInvites => Set<OrganisationAdminInvite>();
    public DbSet<CounsellingMessage> CounsellingMessages => Set<CounsellingMessage>();
    public DbSet<CounsellingMeeting> CounsellingMeetings => Set<CounsellingMeeting>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<GatheringInvite> GatheringInvites => Set<GatheringInvite>();
    public DbSet<Vendor> Vendors => Set<Vendor>();
    public DbSet<AnalyticsEvent> AnalyticsEvents => Set<AnalyticsEvent>();
    public DbSet<Testimonial> Testimonials => Set<Testimonial>();
    public DbSet<TestimonialRead> TestimonialReads => Set<TestimonialRead>();
    public DbSet<TestimonialLike> TestimonialLikes => Set<TestimonialLike>();
    public DbSet<TestimonialComment> TestimonialComments => Set<TestimonialComment>();
    public DbSet<TestimonialCommentLike> TestimonialCommentLikes => Set<TestimonialCommentLike>();
    public DbSet<CompanionPrompt> CompanionPrompts => Set<CompanionPrompt>();
    public DbSet<CompanionPartner> CompanionPartners => Set<CompanionPartner>();
    public DbSet<CompanionEntry> CompanionEntries => Set<CompanionEntry>();
    public DbSet<CompanionSubscription> CompanionSubscriptions => Set<CompanionSubscription>();
    public DbSet<CelebrationEntry> CelebrationEntries => Set<CelebrationEntry>();
    public DbSet<CelebrationWish> CelebrationWishes => Set<CelebrationWish>();
    public DbSet<Newsletter> Newsletters => Set<Newsletter>();
    public DbSet<NewsletterDelivery> NewsletterDeliveries => Set<NewsletterDelivery>();
    public DbSet<NewsletterReview> NewsletterReviews => Set<NewsletterReview>();
    public DbSet<NewsletterLike> NewsletterLikes => Set<NewsletterLike>();
    public DbSet<NewsletterComment> NewsletterComments => Set<NewsletterComment>();
    public DbSet<NewsletterCommentLike> NewsletterCommentLikes => Set<NewsletterCommentLike>();
    public DbSet<PlatformManagerInvite> PlatformManagerInvites => Set<PlatformManagerInvite>();
    public DbSet<PlatformPricing> PlatformPricing => Set<PlatformPricing>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("mirage");
        builder.Entity<ApplicationUser>().Property(x => x.IsNewsletterSubscribed).HasDefaultValue(true);
        builder.ApplyConfigurationsFromAssembly(typeof(MirageDbContext).Assembly);
    }
}
