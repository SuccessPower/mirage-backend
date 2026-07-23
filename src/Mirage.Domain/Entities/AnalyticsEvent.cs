using Mirage.Domain.Common;
using Mirage.Domain.Enums;

namespace Mirage.Domain.Entities;

// Snapshot of an admin-analytics-relevant action (like, chat request/approve, conversation
// close/block, date request create/accept) plus both parties' gender at the moment it happened.
// Deliberately has no FK to AspNetUsers — this is an audit log and must survive independently
// of user deletion/anonymization.
public sealed class AnalyticsEvent : Entity
{
    private AnalyticsEvent() { }

    public AnalyticsEvent(AnalyticsEventType eventType, Guid actorUserId, Sex? actorSex,
        Guid targetUserId, Sex? targetSex, Guid? relatedEntityId = null)
    {
        EventType = eventType;
        ActorUserId = actorUserId;
        ActorSex = actorSex;
        TargetUserId = targetUserId;
        TargetSex = targetSex;
        RelatedEntityId = relatedEntityId;
    }

    public AnalyticsEventType EventType { get; private set; }
    public Guid ActorUserId { get; private set; }
    public Sex? ActorSex { get; private set; }
    public Guid TargetUserId { get; private set; }
    public Sex? TargetSex { get; private set; }
    public Guid? RelatedEntityId { get; private set; }
}
