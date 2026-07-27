using Mirage.Domain.Common;

namespace Mirage.Domain.Entities;

public sealed class ProfileVisit : Entity
{
    private ProfileVisit() { }

    public ProfileVisit(Guid profileUserId, Guid visitorUserId, int revealOrdinal)
    {
        ProfileUserId = profileUserId;
        VisitorUserId = visitorUserId;
        RevealOrdinal = revealOrdinal;
        LastVisitedAt = DateTimeOffset.UtcNow;
    }

    public Guid ProfileUserId { get; private set; }
    public Guid VisitorUserId { get; private set; }
    public int RevealOrdinal { get; private set; }
    public DateTimeOffset LastVisitedAt { get; private set; }

    public bool IsIdentityRevealed => RevealOrdinal <= 10;

    public void RecordReturnVisit()
    {
        LastVisitedAt = DateTimeOffset.UtcNow;
        Touch();
    }
}
