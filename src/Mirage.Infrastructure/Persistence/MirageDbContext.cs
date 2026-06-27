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

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("mirage");
        builder.ApplyConfigurationsFromAssembly(typeof(MirageDbContext).Assembly);
    }
}
