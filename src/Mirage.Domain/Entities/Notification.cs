using Mirage.Domain.Common;
using Mirage.Domain.Enums;

namespace Mirage.Domain.Entities;

public sealed class Notification : Entity
{
    private Notification() { }

    public Notification(Guid userId, NotificationType type, string title, string body,
        Guid? referenceId = null, string? referenceType = null)
    {
        UserId = userId;
        Type = type;
        Title = title;
        Body = body;
        ReferenceId = referenceId;
        ReferenceType = referenceType;
    }

    public Guid UserId { get; private set; }
    public NotificationType Type { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public Guid? ReferenceId { get; private set; }
    public string? ReferenceType { get; private set; }
    public bool IsRead { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }

    public void MarkRead()
    {
        if (IsRead) return;
        IsRead = true;
        ReadAt = DateTimeOffset.UtcNow;
        Touch();
    }
}
