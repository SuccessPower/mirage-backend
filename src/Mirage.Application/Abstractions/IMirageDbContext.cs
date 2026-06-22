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
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
