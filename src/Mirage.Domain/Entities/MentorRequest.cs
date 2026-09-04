using Mirage.Domain.Common;
using Mirage.Domain.Enums;

namespace Mirage.Domain.Entities;

public sealed class MentorRequest : Entity
{
    private MentorRequest() { }

    public MentorRequest(Guid mentorProfileId, Guid menteeUserId, string message,
        MentorshipTier tier = MentorshipTier.Free)
    {
        MentorProfileId = mentorProfileId;
        MenteeUserId = menteeUserId;
        Message = message.Trim();
        Tier = tier;
        // A paid place is not a request until it is paid for. Holding it back keeps a mentor's
        // inbox free of requests that will never be funded.
        Status = tier == MentorshipTier.Paid ? MentorRequestStatus.AwaitingPayment : MentorRequestStatus.Pending;
    }

    public Guid MentorProfileId { get; private set; }
    public Guid MenteeUserId { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public MentorRequestStatus Status { get; private set; } = MentorRequestStatus.Pending;

    /// <summary>Which of the mentor's two groups this mentee belongs to.</summary>
    public MentorshipTier Tier { get; private set; } = MentorshipTier.Free;
    public DateTimeOffset? PaidAt { get; private set; }

    public MentorProfile Mentor { get; private set; } = null!;

    public void Accept() { Status = MentorRequestStatus.Accepted; Touch(); }
    public void Decline() { Status = MentorRequestStatus.Declined; Touch(); }
    public void Withdraw() { Status = MentorRequestStatus.Withdrawn; Touch(); }

    /// <summary>The mentee's payment cleared: the request becomes visible to the mentor.</summary>
    public void ConfirmPayment()
    {
        if (Status != MentorRequestStatus.AwaitingPayment) return;
        Status = MentorRequestStatus.Pending;
        PaidAt = DateTimeOffset.UtcNow;
        Touch();
    }

    /// <summary>
    /// Re-opens a request the mentee withdrew or the mentor declined, rather than inserting a
    /// second row — a mentor and a mentee only ever have one relationship record between them.
    /// </summary>
    public void Reopen(string message, MentorshipTier tier)
    {
        Message = message.Trim();
        Tier = tier;
        PaidAt = null;
        Status = tier == MentorshipTier.Paid ? MentorRequestStatus.AwaitingPayment : MentorRequestStatus.Pending;
        Touch();
    }

    /// <summary>
    /// Moves a mentee between the free and paid groups. The mentor's own call: they may comp a
    /// paying mentee into the free group, or move someone who paid out of band into the paid one.
    /// </summary>
    public void SetTier(MentorshipTier tier)
    {
        if (Tier == tier) return;
        Tier = tier;
        Touch();
    }
}
