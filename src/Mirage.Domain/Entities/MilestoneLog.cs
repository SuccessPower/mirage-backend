using Mirage.Domain.Common;
using Mirage.Domain.Enums;

namespace Mirage.Domain.Entities;

public sealed class MilestoneLog : Entity
{
    private MilestoneLog() { }

    public MilestoneLog(Guid userId, MilestoneType type, Guid? partnerId, string? note)
    {
        UserId = userId;
        Type = type;
        PartnerId = partnerId;
        Note = note?.Trim();
    }

    public Guid UserId { get; private set; }
    public MilestoneType Type { get; private set; }
    public Guid? PartnerId { get; private set; }
    public string? Note { get; private set; }
}
