using Mirage.Domain.Common;
using Mirage.Domain.Enums;

namespace Mirage.Domain.Entities;

public sealed class Recommendation : Entity
{
    private Recommendation() { }

    public Recommendation(Guid recommendedUserId, Guid recommendedByUserId, Guid? organisationId, string? note)
    {
        RecommendedUserId = recommendedUserId;
        RecommendedByUserId = recommendedByUserId;
        OrganisationId = organisationId;
        Note = note?.Trim();
    }

    public Guid RecommendedUserId { get; private set; }
    public Guid RecommendedByUserId { get; private set; }
    public Guid? OrganisationId { get; private set; }
    public string? Note { get; private set; }
    public RecommendationStatus Status { get; private set; } = RecommendationStatus.Active;
    public Organisation? Organisation { get; private set; }

    public void Revoke()
    {
        Status = RecommendationStatus.Revoked;
        Touch();
    }
}
