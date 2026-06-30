using Mirage.Domain.Common;
using Mirage.Domain.Enums;

namespace Mirage.Domain.Entities;

public sealed class Organisation : Entity
{
    private Organisation() { }

    public Organisation(Guid adminUserId, string name, string denomination, string country, string registrationNumber)
    {
        AdminUserId = adminUserId;
        Name = name.Trim();
        Denomination = denomination.Trim();
        Country = country.Trim();
        RegistrationNumber = registrationNumber.Trim();
    }

    public Guid AdminUserId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Denomination { get; private set; } = string.Empty;
    public string Country { get; private set; } = string.Empty;
    public string RegistrationNumber { get; private set; } = string.Empty;
    public OrganisationStatus Status { get; private set; } = OrganisationStatus.Pending;
    public bool OffersFreeSessions { get; private set; }

    public void Approve() { Status = OrganisationStatus.Approved; Touch(); }
    public void Reject() { Status = OrganisationStatus.Rejected; Touch(); }
    public void Suspend() { Status = OrganisationStatus.Suspended; Touch(); }
}
