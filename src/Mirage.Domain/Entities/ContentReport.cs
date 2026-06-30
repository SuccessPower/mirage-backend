using Mirage.Domain.Common;
using Mirage.Domain.Enums;

namespace Mirage.Domain.Entities;

public sealed class ContentReport : Entity
{
    private ContentReport() { }

    public ContentReport(Guid reportedByUserId, ContentReportTargetType targetType, Guid targetId,
        ContentReportReason reason, string? details)
    {
        ReportedByUserId = reportedByUserId;
        TargetType = targetType;
        TargetId = targetId;
        Reason = reason;
        Details = details?.Trim();
    }

    public Guid ReportedByUserId { get; private set; }
    public ContentReportTargetType TargetType { get; private set; }
    public Guid TargetId { get; private set; }
    public ContentReportReason Reason { get; private set; }
    public string? Details { get; private set; }
    public ContentReportStatus Status { get; private set; } = ContentReportStatus.Pending;
    public string? Resolution { get; private set; }

    public void MarkUnderReview() { Status = ContentReportStatus.UnderReview; Touch(); }
    public void TakeAction(string resolution) { Status = ContentReportStatus.ActionTaken; Resolution = resolution.Trim(); Touch(); }
    public void Dismiss(string resolution) { Status = ContentReportStatus.Dismissed; Resolution = resolution.Trim(); Touch(); }
}
