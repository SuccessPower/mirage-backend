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
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
