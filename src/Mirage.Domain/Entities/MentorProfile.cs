using Mirage.Domain.Common;

namespace Mirage.Domain.Entities;

public sealed class MentorProfile : Entity
{
    private MentorProfile() { }

    public MentorProfile(Guid userId, int yearsMarried, string testimony, string[] areasOfGuidance, string[] languages)
    {
        UserId = userId;
        YearsMarried = yearsMarried;
        Testimony = testimony.Trim();
        AreasOfGuidance = areasOfGuidance.Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        Languages = languages.Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
    }

    public Guid UserId { get; private set; }
    public int YearsMarried { get; private set; }
    public string Testimony { get; private set; } = string.Empty;
    public string[] AreasOfGuidance { get; private set; } = [];
    public string[] Languages { get; private set; } = [];
    public bool IsAnonymous { get; private set; } = true;
    public bool IsApproved { get; private set; }
    public bool AcceptsFreeSessions { get; private set; } = true;
    public UserProfile UserProfile { get; private set; } = null!;

    public void Approve() { IsApproved = true; Touch(); }

    public void UpdateProfile(int yearsMarried, string testimony, string[] areasOfGuidance, string[] languages, bool acceptsFreeSessions)
    {
        YearsMarried = yearsMarried;
        Testimony = testimony.Trim();
        AreasOfGuidance = areasOfGuidance.Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        Languages = languages.Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        AcceptsFreeSessions = acceptsFreeSessions;
        Touch();
    }

    public void ToggleAnonymity(bool isAnonymous) { IsAnonymous = isAnonymous; Touch(); }
}
