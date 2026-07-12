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
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<OrganisationBranch> OrganisationBranches => Set<OrganisationBranch>();
    public DbSet<OrganisationMember> OrganisationMembers => Set<OrganisationMember>();
    public DbSet<OrganisationManager> OrganisationManagers => Set<OrganisationManager>();
    public DbSet<OrgEvent> OrgEvents => Set<OrgEvent>();
    public DbSet<EventTicket> EventTickets => Set<EventTicket>();
    public DbSet<Community> Communities => Set<Community>();
    public DbSet<CommunityMember> CommunityMembers => Set<CommunityMember>();
    public DbSet<CommunityPost> CommunityPosts => Set<CommunityPost>();
    public DbSet<CommunityPostLike> CommunityPostLikes => Set<CommunityPostLike>();
    public DbSet<CommunityPostComment> CommunityPostComments => Set<CommunityPostComment>();
    public DbSet<CommunityPostCommentLike> CommunityPostCommentLikes => Set<CommunityPostCommentLike>();
    public DbSet<MentorPost> MentorPosts => Set<MentorPost>();
    public DbSet<MentorGroupMessage> MentorGroupMessages => Set<MentorGroupMessage>();
    public DbSet<MentorMeeting> MentorMeetings => Set<MentorMeeting>();
    public DbSet<MentorMessage> MentorMessages => Set<MentorMessage>();
    public DbSet<Couple> Couples => Set<Couple>();
    public DbSet<OrganisationAdminInvite> OrganisationAdminInvites => Set<OrganisationAdminInvite>();
    public DbSet<CounsellingMessage> CounsellingMessages => Set<CounsellingMessage>();
    public DbSet<CounsellingMeeting> CounsellingMeetings => Set<CounsellingMeeting>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<GatheringInvite> GatheringInvites => Set<GatheringInvite>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("mirage");
        builder.ApplyConfigurationsFromAssembly(typeof(MirageDbContext).Assembly);
    }
}
