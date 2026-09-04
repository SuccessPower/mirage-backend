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
    public bool IsApproved { get; private set; }
    public bool AcceptsFreeSessions { get; private set; } = true;
    public bool AllowMenteesToSeeEachOther { get; private set; }
    public string? PhoneNumber { get; private set; }
    public string? InviteCode { get; private set; }

    // Paid mentorship. A mentor runs both sides at once: AcceptsFreeSessions keeps the free group
    // open while OffersPaidMentorship opens a paid one alongside it. The two are independent —
    // turning on paid places never closes the free door, which is the point of the free tier.
    public bool OffersPaidMentorship { get; private set; }
    public decimal? PriceAmount { get; private set; }
    public string? PriceCurrency { get; private set; }
    public string? BankCode { get; private set; }
    public string? BankName { get; private set; }
    public string? BankAccountNumber { get; private set; }
    public string? BankAccountName { get; private set; }
    public string? PaystackSubaccountCode { get; private set; }
    public string? PaystackTransferRecipientCode { get; private set; }
    public string? FlutterwaveSubaccountId { get; private set; }
    public bool HasPayoutAccount => BankCode is not null && BankAccountNumber is not null;

    /// <summary>A mentor can only take money once there is somewhere to send it and a price to charge.</summary>
    public bool CanChargeForMentorship =>
        OffersPaidMentorship && HasPayoutAccount && PriceAmount is > 0 && PriceCurrency is not null;

    public UserProfile UserProfile { get; private set; } = null!;

    public void Approve() { IsApproved = true; Touch(); }

    public void UpdateProfile(int yearsMarried, string testimony, string[] areasOfGuidance, string[] languages,
        bool acceptsFreeSessions, bool allowMenteesToSeeEachOther)
    {
        YearsMarried = yearsMarried;
        Testimony = testimony.Trim();
        AreasOfGuidance = areasOfGuidance.Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        Languages = languages.Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        AcceptsFreeSessions = acceptsFreeSessions;
        AllowMenteesToSeeEachOther = allowMenteesToSeeEachOther;
        Touch();
    }

    public void SetPhoneNumber(string? phoneNumber)
    {
        PhoneNumber = string.IsNullOrWhiteSpace(phoneNumber) ? null : phoneNumber.Trim();
        Touch();
    }

    /// <summary>
    /// Opens or closes the paid side of the practice. Enabling it without a payout account is
    /// rejected here rather than at checkout, so a mentee never reaches a payment page for money
    /// that has nowhere to settle.
    /// </summary>
    public void SetPaidMentorship(bool offersPaidMentorship, decimal? priceAmount, string? priceCurrency)
    {
        if (offersPaidMentorship)
        {
            if (!HasPayoutAccount)
                throw new InvalidOperationException("Add a payout bank account before offering paid mentorship.");
            if (priceAmount is not > 0)
                throw new InvalidOperationException("Set a price above zero before offering paid mentorship.");
            if (string.IsNullOrWhiteSpace(priceCurrency))
                throw new InvalidOperationException("Set a currency before offering paid mentorship.");
        }
        OffersPaidMentorship = offersPaidMentorship;
        PriceAmount = priceAmount;
        PriceCurrency = string.IsNullOrWhiteSpace(priceCurrency) ? null : priceCurrency.Trim().ToUpperInvariant();
        Touch();
    }

    public void SetBankAccount(string bankCode, string bankName, string accountNumber, string accountName)
    {
        BankCode = bankCode.Trim();
        BankName = bankName.Trim();
        BankAccountNumber = accountNumber.Trim();
        BankAccountName = accountName.Trim();
        Touch();
    }

    public void SetPaystackSubaccountCode(string code) { PaystackSubaccountCode = code; Touch(); }
    public void SetPaystackTransferRecipientCode(string code) { PaystackTransferRecipientCode = code; Touch(); }
    public void SetFlutterwaveSubaccountId(string id) { FlutterwaveSubaccountId = id; Touch(); }

    public void SetInviteCode(string inviteCode) { InviteCode = inviteCode.Trim().ToUpperInvariant(); Touch(); }
}
