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

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("mirage");
        builder.ApplyConfigurationsFromAssembly(typeof(MirageDbContext).Assembly);
    }
}
