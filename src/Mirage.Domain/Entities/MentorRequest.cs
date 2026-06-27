using Mirage.Domain.Common;
using Mirage.Domain.Enums;

namespace Mirage.Domain.Entities;

public sealed class MentorRequest : Entity
{
    private MentorRequest() { }

    public MentorRequest(Guid mentorProfileId, Guid menteeUserId, string message)
    {
        MentorProfileId = mentorProfileId;
        MenteeUserId = menteeUserId;
        Message = message.Trim();
    }

    public Guid MentorProfileId { get; private set; }
    public Guid MenteeUserId { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public MentorRequestStatus Status { get; private set; } = MentorRequestStatus.Pending;
    public MentorProfile Mentor { get; private set; } = null!;

    public void Accept() { Status = MentorRequestStatus.Accepted; Touch(); }
    public void Decline() { Status = MentorRequestStatus.Declined; Touch(); }
    public void Withdraw() { Status = MentorRequestStatus.Withdrawn; Touch(); }
}
