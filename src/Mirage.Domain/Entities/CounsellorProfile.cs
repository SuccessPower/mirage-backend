using Mirage.Domain.Common;

namespace Mirage.Domain.Entities;

public sealed class CounsellorProfile : Entity
{
    private CounsellorProfile() { }

    public CounsellorProfile(Guid userId, Guid? organisationId, int yearsExperience, string[] specialisations,
        string[] languages, string[]? verificationDocumentUrls = null)
    {
        UserId = userId;
        OrganisationId = organisationId;
        YearsExperience = yearsExperience;
        Specialisations = specialisations.Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        Languages = languages.Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        VerificationDocumentUrls = (verificationDocumentUrls ?? []).Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
    }

    public Guid UserId { get; private set; }
    public Guid? OrganisationId { get; private set; }
    public int YearsExperience { get; private set; }
    public bool IsAnonymous { get; private set; } = true;
    public bool IsApproved { get; private set; }
    public bool IsRejected { get; private set; }
    public string? RejectionReason { get; private set; }
    public bool AcceptsFreeSessions { get; private set; }
    public string[] Specialisations { get; private set; } = [];
    public string[] Languages { get; private set; } = [];
    public string[] VerificationDocumentUrls { get; private set; } = [];
    public Organisation? Organisation { get; private set; }
    public UserProfile UserProfile { get; private set; } = null!;

    public void Approve() { IsApproved = true; IsRejected = false; RejectionReason = null; Touch(); }

    public void Reject(string reason)
    {
        IsRejected = true;
        RejectionReason = reason.Trim();
        Touch();
    }

    public void SetVerificationDocuments(string[] documentUrls)
    {
        VerificationDocumentUrls = documentUrls.Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        Touch();
    }

    public void UpdateProfile(int yearsExperience, string[] specialisations, string[] languages, bool acceptsFreeSessions)
    {
        YearsExperience = yearsExperience;
        Specialisations = specialisations.Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        Languages = languages.Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        AcceptsFreeSessions = acceptsFreeSessions;
        Touch();
    }

    public void ToggleAnonymity(bool isAnonymous) { IsAnonymous = isAnonymous; Touch(); }
}
