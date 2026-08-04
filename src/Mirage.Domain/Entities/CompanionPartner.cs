using Mirage.Domain.Common;
using Mirage.Domain.Enums;

namespace Mirage.Domain.Entities;

// A Companion-specific partner link, independent of the marriage-only Couple entity — usable at
// any relationship stage. Once approved, both users can see each other's CompanionEntry answers.
public sealed class CompanionPartner : Entity
{
    private CompanionPartner() { }

    public CompanionPartner(Guid requestedByUserId, Guid partnerUserId)
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
    public CompanionPartnerStatus Status { get; private set; } = CompanionPartnerStatus.Pending;
    public DateTimeOffset? ReviewedAt { get; private set; }

    public void Approve(Guid approverUserId)
    {
        if (Status != CompanionPartnerStatus.Pending)
            throw new InvalidOperationException("Only a pending Companion partner invitation can be approved.");
        if (approverUserId == RequestedByUserId)
            throw new InvalidOperationException("You cannot approve your own Companion partner invitation.");
        Status = CompanionPartnerStatus.Approved;
        ReviewedAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public void Decline()
    {
        if (Status != CompanionPartnerStatus.Pending)
            throw new InvalidOperationException("Only a pending Companion partner invitation can be declined.");
        Status = CompanionPartnerStatus.Declined;
        ReviewedAt = DateTimeOffset.UtcNow;
        Touch();
    }
}
