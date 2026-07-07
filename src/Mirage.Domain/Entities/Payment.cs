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
}
