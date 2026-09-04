using Mirage.Domain.Common;
using Mirage.Domain.Enums;

namespace Mirage.Domain.Entities;

public sealed class ProfessionalConnection : Entity
{
    private ProfessionalConnection() { }

    public ProfessionalConnection(Guid professionalUserId, Guid memberUserId, ProfessionalRole role)
    {
        ProfessionalUserId = professionalUserId;
        MemberUserId = memberUserId;
        Role = role;
    }

    public Guid ProfessionalUserId { get; private set; }
    public Guid MemberUserId { get; private set; }
    public ProfessionalRole Role { get; private set; }
    public ProfessionalConnectionStatus Status { get; private set; } = ProfessionalConnectionStatus.Pending;
    public void Accept() { Status = ProfessionalConnectionStatus.Accepted; Touch(); }
    public void Decline() { Status = ProfessionalConnectionStatus.Declined; Touch(); }
    public void Withdraw() { Status = ProfessionalConnectionStatus.Withdrawn; Touch(); }
}

public sealed class CalendarReminderDelivery : Entity
{
    private CalendarReminderDelivery() { }
    public CalendarReminderDelivery(string source, Guid sourceId, Guid userId, CalendarReminderLeadTime leadTime)
    {
        Source = source;
        SourceId = sourceId;
        UserId = userId;
        LeadTime = leadTime;
    }
    public string Source { get; private set; } = string.Empty;
    public Guid SourceId { get; private set; }
    public Guid UserId { get; private set; }
    public CalendarReminderLeadTime LeadTime { get; private set; }
}
