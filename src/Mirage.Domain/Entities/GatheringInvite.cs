using Mirage.Domain.Common;
using Mirage.Domain.Enums;

namespace Mirage.Domain.Entities;

// A direct invite to join a Community or a DateRequest (friendship gathering), sent to an
// existing user by email/username lookup rather than the token-redemption flow used for
// onboarding new counsellors/org-admins (see CounsellorInvite/OrganisationAdminInvite).
public sealed class GatheringInvite : Entity
{
    private GatheringInvite() { }

    public GatheringInvite(GatheringInviteKind kind, Guid targetId, Guid inviterUserId, Guid inviteeUserId,
        Guid? branchId = null)
    {
        Kind = kind;
        TargetId = targetId;
        InviterUserId = inviterUserId;
        InviteeUserId = inviteeUserId;
        BranchId = branchId;
    }

    public GatheringInviteKind Kind { get; private set; }
    public Guid TargetId { get; private set; }
    public Guid InviterUserId { get; private set; }
    public Guid InviteeUserId { get; private set; }
    // Only meaningful for Kind == OrganisationManager: scopes the invited manager to one branch
    // of TargetId's organisation, or null for an org-wide manager.
    public Guid? BranchId { get; private set; }
    public GatheringInviteStatus Status { get; private set; } = GatheringInviteStatus.Pending;

    public void Accept()
    {
        if (Status != GatheringInviteStatus.Pending)
            throw new InvalidOperationException("Only pending invites can be accepted.");
        Status = GatheringInviteStatus.Accepted;
        Touch();
    }

    public void Decline()
    {
        if (Status != GatheringInviteStatus.Pending)
            throw new InvalidOperationException("Only pending invites can be declined.");
        Status = GatheringInviteStatus.Declined;
        Touch();
    }
}
