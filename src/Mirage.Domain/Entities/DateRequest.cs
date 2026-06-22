using Mirage.Domain.Common;
using Mirage.Domain.Enums;

namespace Mirage.Domain.Entities;

public sealed class DateRequest : Entity
{
    private DateRequest() { }

    public DateRequest(Guid requestorUserId, string activity, DateTimeOffset startsAt, DateTimeOffset endsAt,
        string locationArea, string? note)
    {
        if (endsAt <= startsAt) throw new ArgumentException("Date request end time must be after its start time.");
        RequestorUserId = requestorUserId;
        Activity = activity.Trim();
        StartsAt = startsAt;
        EndsAt = endsAt;
        LocationArea = locationArea.Trim();
        Note = note?.Trim();
    }

    public Guid RequestorUserId { get; private set; }
    public string Activity { get; private set; } = string.Empty;
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset EndsAt { get; private set; }
    public string LocationArea { get; private set; } = string.Empty;
    public string? Note { get; private set; }
    public DateRequestStatus Status { get; private set; } = DateRequestStatus.Open;
    public Guid? SelectedUserId { get; private set; }
    public List<DateRequestAcceptance> Acceptances { get; private set; } = [];

    public void Select(Guid userId)
    {
        SelectedUserId = userId;
        Status = DateRequestStatus.Confirmed;
        foreach (var acceptance in Acceptances) acceptance.MarkSelected(acceptance.AcceptorUserId == userId);
        Touch();
    }

    public void Cancel()
    {
        if (Status is DateRequestStatus.Completed or DateRequestStatus.Cancelled)
            throw new InvalidOperationException("The date request can no longer be cancelled.");
        Status = DateRequestStatus.Cancelled;
        Touch();
    }
}

public sealed class DateRequestAcceptance : Entity
{
    private DateRequestAcceptance() { }
    public DateRequestAcceptance(Guid dateRequestId, Guid acceptorUserId)
    {
        DateRequestId = dateRequestId;
        AcceptorUserId = acceptorUserId;
    }
    public Guid DateRequestId { get; private set; }
    public Guid AcceptorUserId { get; private set; }
    public DateAcceptanceStatus Status { get; private set; } = DateAcceptanceStatus.Pending;
    public DateRequest DateRequest { get; private set; } = null!;
    internal void MarkSelected(bool selected) => Status = selected ? DateAcceptanceStatus.Selected : DateAcceptanceStatus.Declined;
    public void Withdraw()
    {
        if (Status != DateAcceptanceStatus.Pending)
            throw new InvalidOperationException("Only pending acceptances can be withdrawn.");
        Status = DateAcceptanceStatus.Withdrawn;
        Touch();
    }
}
