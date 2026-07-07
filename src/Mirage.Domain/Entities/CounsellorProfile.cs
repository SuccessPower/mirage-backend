using Mirage.Domain.Common;

namespace Mirage.Domain.Entities;

public sealed class CounsellorProfile : Entity
{
    public const int MinimumFreeSessionsBeforeCharging = 3;

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
        // Every counsellor starts on free sessions; charging is only ever switched on via
        // RequestCharging + admin ApproveCharging.
        AcceptsFreeSessions = true;
    }

    public Guid UserId { get; private set; }
    public Guid? OrganisationId { get; private set; }
    public int YearsExperience { get; private set; }
    public bool IsAnonymous { get; private set; } = true;
    public bool IsApproved { get; private set; }
    public bool IsRejected { get; private set; }
    public string? RejectionReason { get; private set; }
    public bool AcceptsFreeSessions { get; private set; }
    public int CompletedFreeSessionsCount { get; private set; }
    public bool IsEligibleToCharge => CompletedFreeSessionsCount >= MinimumFreeSessionsBeforeCharging;
    public string[] Specialisations { get; private set; } = [];
    public string[] Languages { get; private set; } = [];
    public string[] VerificationDocumentUrls { get; private set; } = [];
    public string? PhoneNumber { get; private set; }
    public decimal? PriceAmount { get; private set; }
    public string? PriceCurrency { get; private set; }
    public bool SupportsVoiceCalls { get; private set; } = true;
    public bool SupportsVideoCalls { get; private set; } = true;
    public double AverageRating { get; private set; }
    public int RatingCount { get; private set; }
    public bool ChargingRequested { get; private set; }
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

    // Counsellors can always self-service back to free sessions (strictly more generous),
    // but switching to charging requires admin approval — see RequestCharging/ApproveCharging.
    public void UpdateProfile(int yearsExperience, string[] specialisations, string[] languages, bool acceptsFreeSessions)
    {
        if (!acceptsFreeSessions && AcceptsFreeSessions)
            throw new InvalidOperationException(
                "Submit a charging request for admin approval instead of disabling free sessions directly.");
        YearsExperience = yearsExperience;
        Specialisations = specialisations.Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        Languages = languages.Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        AcceptsFreeSessions = acceptsFreeSessions;
        Touch();
    }

    public void RequestCharging()
    {
        if (!IsEligibleToCharge)
            throw new InvalidOperationException(
                $"Counsellor must complete {MinimumFreeSessionsBeforeCharging} free sessions before requesting to charge.");
        if (!AcceptsFreeSessions)
            throw new InvalidOperationException("Counsellor is already charging.");
        if (ChargingRequested)
            throw new InvalidOperationException("A charging request is already pending.");
        ChargingRequested = true;
        Touch();
    }

    public void ApproveCharging()
    {
        if (!ChargingRequested) throw new InvalidOperationException("No pending charging request to approve.");
        AcceptsFreeSessions = false;
        ChargingRequested = false;
        Touch();
    }

    public void DeclineChargingRequest()
    {
        if (!ChargingRequested) throw new InvalidOperationException("No pending charging request to decline.");
        ChargingRequested = false;
        Touch();
    }

    public void ToggleAnonymity(bool isAnonymous) { IsAnonymous = isAnonymous; Touch(); }

    public void RecordCompletedFreeSession()
    {
        if (AcceptsFreeSessions) CompletedFreeSessionsCount++;
        Touch();
    }

    public void SetContactAndPricing(string? phoneNumber, decimal? priceAmount, string? priceCurrency,
        bool supportsVoiceCalls, bool supportsVideoCalls)
    {
        PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();
        PriceAmount = priceAmount;
        PriceCurrency = string.IsNullOrWhiteSpace(priceCurrency) ? null : priceCurrency.Trim().ToUpperInvariant();
        SupportsVoiceCalls = supportsVoiceCalls;
        SupportsVideoCalls = supportsVideoCalls;
        Touch();
    }

    public void RecordRating(int rating)
    {
        AverageRating = (AverageRating * RatingCount + rating) / (RatingCount + 1);
        RatingCount++;
        Touch();
    }
}
