using Mirage.Domain.Common;
using Mirage.Domain.Enums;

namespace Mirage.Domain.Entities;

public sealed class Vendor : Entity
{
    private Vendor() { }

    public Vendor(Guid ownerUserId, string businessName, VendorCategory category, string description,
        string email, string phone, string address, string city, string country)
    {
        OwnerUserId = ownerUserId;
        BusinessName = businessName.Trim();
        Category = category;
        Description = description.Trim();
        Email = email.Trim();
        Phone = phone.Trim();
        Address = address.Trim();
        City = city.Trim();
        Country = country.Trim();
    }

    public const int MaxPhotos = 10;

    public Guid OwnerUserId { get; private set; }
    public string BusinessName { get; private set; } = string.Empty;
    public VendorCategory Category { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string Phone { get; private set; } = string.Empty;
    public string Address { get; private set; } = string.Empty;
    public string City { get; private set; } = string.Empty;
    public string Country { get; private set; } = string.Empty;
    public string[] PhotoUrls { get; private set; } = [];
    public VendorStatus Status { get; private set; } = VendorStatus.Pending;

    public void UpdateDetails(string businessName, VendorCategory category, string description,
        string email, string phone, string address, string city, string country)
    {
        BusinessName = businessName.Trim();
        Category = category;
        Description = description.Trim();
        Email = email.Trim();
        Phone = phone.Trim();
        Address = address.Trim();
        City = city.Trim();
        Country = country.Trim();
        Touch();
    }

    public void SetPhotos(string[] photoUrls)
    {
        var cleaned = photoUrls.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().ToArray();
        if (cleaned.Length > MaxPhotos)
            throw new InvalidOperationException($"A vendor can have at most {MaxPhotos} photos.");
        PhotoUrls = cleaned;
        Touch();
    }

    public void Approve() { Status = VendorStatus.Approved; Touch(); }
    public void Reject() { Status = VendorStatus.Rejected; Touch(); }
    public void Suspend() { Status = VendorStatus.Suspended; Touch(); }
}
