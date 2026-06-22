using Mirage.Domain.Common;

namespace Mirage.Domain.Entities;

public sealed class CounsellorProfile : Entity
{
    private CounsellorProfile() { }

    public Guid UserId { get; private set; }
    public Guid OrganisationId { get; private set; }
    public int YearsExperience { get; private set; }
    public bool IsAnonymous { get; private set; } = true;
    public bool IsApproved { get; private set; }
    public bool AcceptsFreeSessions { get; private set; }
    public string[] Specialisations { get; private set; } = [];
    public string[] Languages { get; private set; } = [];
    public Organisation Organisation { get; private set; } = null!;
    public UserProfile UserProfile { get; private set; } = null!;
}
