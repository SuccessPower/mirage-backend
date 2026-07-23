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
    DbSet<Message> Messages { get; }
    DbSet<Notification> Notifications { get; }
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
    DbSet<OrganisationAdminInvite> OrganisationAdminInvites { get; }
    DbSet<CounsellingMessage> CounsellingMessages { get; }
    DbSet<CounsellingMeeting> CounsellingMeetings { get; }
    DbSet<Payment> Payments { get; }
    DbSet<GatheringInvite> GatheringInvites { get; }
    DbSet<Vendor> Vendors { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
