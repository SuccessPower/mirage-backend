using Mirage.Domain.Common;
using Mirage.Domain.Enums;

namespace Mirage.Domain.Entities;

public sealed class CounsellingSession : Entity
{
    private CounsellingSession() { }

    public CounsellingSession(Guid counsellorId, Guid clientUserId, SessionType type, DateTimeOffset scheduledAt,
        string topic, bool counsellorAnonymous, bool clientAnonymous)
    {
        CounsellorId = counsellorId;
        ClientUserId = clientUserId;
        Type = type;
        ScheduledAt = scheduledAt;
        Topic = topic.Trim();
        CounsellorAnonymous = counsellorAnonymous;
        ClientAnonymous = clientAnonymous;
    }

    public Guid CounsellorId { get; private set; }
    public Guid ClientUserId { get; private set; }
    public SessionType Type { get; private set; }
    public DateTimeOffset ScheduledAt { get; private set; }
    public SessionStatus Status { get; private set; } = SessionStatus.Requested;
    public string Topic { get; private set; } = string.Empty;
    public bool CounsellorAnonymous { get; private set; }
    public bool ClientAnonymous { get; private set; }
    public TrustUnlockStatus TrustUnlockStatus { get; private set; }
    public bool ClientConsentedToReveal { get; private set; }
    public bool CounsellorConsentedToReveal { get; private set; }
    public CounsellorProfile Counsellor { get; private set; } = null!;

    public void Accept()
    {
        if (Status != SessionStatus.Requested) throw new InvalidOperationException("Only requested sessions can be accepted.");
        Status = SessionStatus.Scheduled;
        Touch();
    }

    public void Decline()
    {
        if (Status != SessionStatus.Requested) throw new InvalidOperationException("Only requested sessions can be declined.");
        Status = SessionStatus.Declined;
        Touch();
    }

    public void Start()
    {
        if (Status != SessionStatus.Scheduled) throw new InvalidOperationException("Only scheduled sessions can be started.");
        Status = SessionStatus.InProgress;
        Touch();
    }

    public void Complete()
    {
        if (Status != SessionStatus.InProgress) throw new InvalidOperationException("Only in-progress sessions can be completed.");
        Status = SessionStatus.Completed;
        Touch();
    }

    public void Cancel()
    {
        if (Status is SessionStatus.Completed or SessionStatus.Cancelled or SessionStatus.Declined)
            throw new InvalidOperationException("Session cannot be cancelled in its current state.");
        Status = SessionStatus.Cancelled;
        Touch();
    }

    public void ConsentToReveal(bool isClient)
    {
        if (isClient) ClientConsentedToReveal = true;
        else CounsellorConsentedToReveal = true;
        TrustUnlockStatus = ClientConsentedToReveal && CounsellorConsentedToReveal
            ? TrustUnlockStatus.Unlocked
            : TrustUnlockStatus.Pending;
        Touch();
    }
}

public sealed class AnonymityAuditLog : Entity
{
    private AnonymityAuditLog() { }
    public AnonymityAuditLog(Guid sessionId, Guid actorUserId, string action)
    {
        SessionId = sessionId;
        ActorUserId = actorUserId;
        Action = action;
    }
    public Guid SessionId { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string Action { get; private set; } = string.Empty;
}
