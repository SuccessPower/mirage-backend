using Mirage.Domain.Common;
using Mirage.Domain.Enums;

namespace Mirage.Domain.Entities;

public sealed class Payment : Entity
{
    // Platform's cut of every paid session. Snapshotted onto each Payment at creation time
    // (PlatformFeeAmount/CounsellorAmount below) so a future rate change never retroactively
    // alters the split on already-created payments.
    public const decimal PlatformCommissionRate = 0.15m;

    private Payment() { }

    public Payment(Guid counsellingSessionId, Guid payerUserId, Guid counsellorId, decimal amount, string currency)
    {
        CounsellingSessionId = counsellingSessionId;
        PayerUserId = payerUserId;
        CounsellorId = counsellorId;
        Amount = amount;
        Currency = currency.Trim().ToUpperInvariant();
        PlatformFeeAmount = Math.Round(amount * PlatformCommissionRate, 2);
        CounsellorAmount = amount - PlatformFeeAmount;
    }

    public Guid CounsellingSessionId { get; private set; }
    public Guid PayerUserId { get; private set; }
    public Guid CounsellorId { get; private set; }
    public decimal Amount { get; private set; }
    public decimal PlatformFeeAmount { get; private set; }
    public decimal CounsellorAmount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public PaymentProvider? Provider { get; private set; }
    public PaymentMethod? Method { get; private set; }
    public string? ProviderReference { get; private set; }
    public string? ProviderTransactionId { get; private set; }
    public PaymentStatus Status { get; private set; } = PaymentStatus.Pending;
    public PayoutStatus PayoutStatus { get; private set; } = PayoutStatus.Held;
    public string? PayoutReference { get; private set; }
    public string? ProviderTransferId { get; private set; }
    public Guid? PayoutApprovedByUserId { get; private set; }
    public DateTimeOffset? PayoutApprovedAt { get; private set; }
    public DateTimeOffset? PayoutPaidAt { get; private set; }
    public string? PayoutFailureReason { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }
    public CounsellingSession CounsellingSession { get; private set; } = null!;

    public void Initialize(PaymentProvider provider, PaymentMethod method, string providerReference)
    {
        if (Status == PaymentStatus.Successful)
            throw new InvalidOperationException("Payment has already been completed.");
        Provider = provider;
        Method = method;
        ProviderReference = providerReference;
        Status = PaymentStatus.Pending;
        Touch();
    }

    public void MarkSuccessful(string providerTransactionId)
    {
        if (Status == PaymentStatus.Successful) return;
        Status = PaymentStatus.Successful;
        ProviderTransactionId = providerTransactionId;
        PaidAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public void MarkFailed()
    {
        if (Status == PaymentStatus.Successful) return;
        Status = PaymentStatus.Failed;
        Touch();
    }

    public void RequestPayoutApproval()
    {
        if (Status != PaymentStatus.Successful)
            throw new InvalidOperationException("Only a successful payment can be submitted for payout.");
        if (PayoutStatus != PayoutStatus.Held)
            throw new InvalidOperationException("Payout is not held or has already been submitted.");
        PayoutStatus = PayoutStatus.AwaitingApproval;
        Touch();
    }

    public void ApprovePayout(Guid adminUserId)
    {
        if (PayoutStatus is not (PayoutStatus.AwaitingApproval or PayoutStatus.Failed))
            throw new InvalidOperationException("Only an awaiting or failed payout can be approved.");
        PayoutReference ??= $"mirage-payout-{Id:N}";
        PayoutApprovedByUserId = adminUserId;
        PayoutApprovedAt = DateTimeOffset.UtcNow;
        PayoutFailureReason = null;
        PayoutStatus = PayoutStatus.Processing;
        Touch();
    }

    public void MarkPayoutSubmitted(string? providerTransferId)
    {
        if (PayoutStatus != PayoutStatus.Processing)
            throw new InvalidOperationException("Payout is not being processed.");
        ProviderTransferId = providerTransferId;
        Touch();
    }

    public void MarkPayoutPaid(string? providerTransferId = null)
    {
        if (PayoutStatus == PayoutStatus.Paid) return;
        if (PayoutStatus != PayoutStatus.Processing)
            throw new InvalidOperationException("Only a processing payout can be paid.");
        ProviderTransferId = providerTransferId ?? ProviderTransferId;
        PayoutStatus = PayoutStatus.Paid;
        PayoutPaidAt = DateTimeOffset.UtcNow;
        PayoutFailureReason = null;
        Touch();
    }

    public void MarkPayoutFailed(string reason)
    {
        if (PayoutStatus == PayoutStatus.Paid) return;
        PayoutStatus = PayoutStatus.Failed;
        PayoutFailureReason = reason.Trim()[..Math.Min(reason.Trim().Length, 500)];
        Touch();
    }
}
