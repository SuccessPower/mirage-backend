using Mirage.Domain.Common;
using Mirage.Domain.Enums;

namespace Mirage.Domain.Entities;

// A partner-sync request aimed at an email address that has no Mirage account yet. The inviter
// cannot create a Couple against a user who does not exist, so the intent is parked here and the
// invitee is emailed an invitation to join. When someone registers with that address,
// registration converts every pending invite for it into a real Couple invitation, so the inviter
// never has to come back and send the request a second time.
public sealed class PartnerInvite : Entity
{
    private PartnerInvite() { }

    public PartnerInvite(Guid inviterUserId, string inviteeEmail)
    {
        InviterUserId = inviterUserId;
        InviteeEmail = Normalise(inviteeEmail);
        LastSentAt = DateTimeOffset.UtcNow;
    }

    public Guid InviterUserId { get; private set; }

    // Stored normalised (trimmed, lower-cased) because it is matched against a registering user's
    // address, and it is the only handle we have on someone with no account.
    public string InviteeEmail { get; private set; } = string.Empty;

    public PartnerInviteStatus Status { get; private set; } = PartnerInviteStatus.Pending;
    public DateTimeOffset LastSentAt { get; private set; }
    public DateTimeOffset? AcceptedAt { get; private set; }

    // The Couple invitation this became once the invitee signed up.
    public Guid? CoupleId { get; private set; }

    public static string Normalise(string email) => email.Trim().ToLowerInvariant();

    // Re-sending is how an inviter nudges someone who has not signed up yet; the timestamp is what
    // the endpoint throttles on so a repeated tap does not repeatedly email the same person.
    public void MarkResent()
    {
        LastSentAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public void MarkAccepted(Guid coupleId)
    {
        Status = PartnerInviteStatus.Accepted;
        CoupleId = coupleId;
        AcceptedAt = DateTimeOffset.UtcNow;
        Touch();
    }
}
