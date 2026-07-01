using Mirage.Domain.Common;
using Mirage.Domain.Enums;

namespace Mirage.Domain.Entities;

public sealed class DateRequest : Entity
{
    private DateRequest() { }

    public DateRequest(Guid requestorUserId, string activity, DateTimeOffset startsAt, DateTimeOffset endsAt,
        string locationArea, string? note, RelationshipIntent intent = RelationshipIntent.Dating,
        int capacity = 1, string? itemsToBring = null)
    {
        if (endsAt <= startsAt) throw new ArgumentException("Date request end time must be after its start time.");
        if (capacity < 1) throw new ArgumentException("Capacity must be at least 1.");
        RequestorUserId = requestorUserId;
        Activity = activity.Trim();
        StartsAt = startsAt;
        EndsAt = endsAt;
        LocationArea = locationArea.Trim();
        Note = note?.Trim();
        Intent = intent;
        Capacity = capacity;
        ItemsToBring = itemsToBring?.Trim();
    }

    public Guid RequestorUserId { get; private set; }
    public string Activity { get; private set; } = string.Empty;
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset EndsAt { get; private set; }
    public string LocationArea { get; private set; } = string.Empty;
    public string? Note { get; private set; }
    public RelationshipIntent Intent { get; private set; } = RelationshipIntent.Dating;
    public int Capacity { get; private set; } = 1;
    public string? ItemsToBring { get; private set; }
    public DateRequestStatus Status { get; private set; } = DateRequestStatus.Open;
    public Guid? SelectedUserId { get; private set; }
    public List<DateRequestAcceptance> Acceptances { get; private set; } = [];

    // For Capacity == 1 (1:1 dating) this selects the single winner and closes the request.
    // For Capacity > 1 (group gatherings) it accumulates selections; once Capacity is reached
    // the request is confirmed and any remaining pending acceptances are auto-declined.
    public void Select(Guid userId)
    {
        var acceptance = Acceptances.SingleOrDefault(x => x.AcceptorUserId == userId)
            ?? throw new InvalidOperationException("No acceptance was found for this user.");
        if (acceptance.Status != DateAcceptanceStatus.Pending)
            throw new InvalidOperationException("Only pending acceptances can be selected.");

        var selectedCount = Acceptances.Count(x => x.Status == DateAcceptanceStatus.Selected);
        if (selectedCount >= Capacity)
            throw new InvalidOperationException("This date request has already reached its capacity.");

        acceptance.MarkSelected(true);
        SelectedUserId ??= userId;
        selectedCount++;

        if (selectedCount >= Capacity)
        {
            Status = DateRequestStatus.Confirmed;
            foreach (var pending in Acceptances.Where(x => x.Status == DateAcceptanceStatus.Pending))
                pending.MarkSelected(false);
        }

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
