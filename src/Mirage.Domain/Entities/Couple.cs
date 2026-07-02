using Mirage.Domain.Common;
using Mirage.Domain.Enums;

namespace Mirage.Domain.Entities;

// A formally linked married couple, distinct from the dating Match system. One spouse invites
// the other; once approved, both profiles are marked Married and drop out of the discovery pool.
public sealed class Couple : Entity
{
    private Couple() { }

    public Couple(Guid requestedByUserId, Guid partnerUserId)
    {
        RequestedByUserId = requestedByUserId;
        if (requestedByUserId.CompareTo(partnerUserId) < 0)
        {
            User1Id = requestedByUserId;
            User2Id = partnerUserId;
        }
        else
        {
            User1Id = partnerUserId;
            User2Id = requestedByUserId;
        }
    }

    public Guid User1Id { get; private set; }
    public Guid User2Id { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public CoupleStatus Status { get; private set; } = CoupleStatus.Pending;
    public DateTimeOffset? ReviewedAt { get; private set; }

    public void Approve(Guid approverUserId)
    {
        if (Status != CoupleStatus.Pending)
            throw new InvalidOperationException("Only a pending couple invitation can be approved.");
        if (approverUserId == RequestedByUserId)
            throw new InvalidOperationException("You cannot approve your own couple invitation.");
        Status = CoupleStatus.Approved;
        ReviewedAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public void Decline()
    {
        if (Status != CoupleStatus.Pending)
            throw new InvalidOperationException("Only a pending couple invitation can be declined.");
        Status = CoupleStatus.Declined;
        ReviewedAt = DateTimeOffset.UtcNow;
        Touch();
    }
}
