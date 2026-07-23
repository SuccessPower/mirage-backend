using Microsoft.EntityFrameworkCore;
using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;

namespace Mirage.Api.Services;

// Records admin-analytics events (likes, chat requests/approvals, conversation close/block,
// date request create/accept) with both parties' gender snapshotted at the moment of the action.
// Callers add the event to the same IMirageDbContext they already save via SaveChangesAsync, so
// no extra DB round-trip is added to any hot path.
internal static class AnalyticsRecorder
{
    public static async Task RecordAsync(IMirageDbContext db, AnalyticsEventType eventType,
        Guid actorUserId, Guid targetUserId, Guid? relatedEntityId, CancellationToken cancellationToken)
    {
        var sexes = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == actorUserId || x.UserId == targetUserId)
            .Select(x => new { x.UserId, x.Sex })
            .ToListAsync(cancellationToken);
        var actorSex = sexes.SingleOrDefault(x => x.UserId == actorUserId)?.Sex;
        var targetSex = sexes.SingleOrDefault(x => x.UserId == targetUserId)?.Sex;

        db.AnalyticsEvents.Add(new AnalyticsEvent(eventType, actorUserId, actorSex, targetUserId, targetSex, relatedEntityId));
    }
}
