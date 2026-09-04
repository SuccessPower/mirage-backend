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
        : this(payerUserId, amount, currency)
    {
        CounsellingSessionId = counsellingSessionId;
        CounsellorId = counsellorId;
    }

    /// <summary>A mentee paying for a place in a mentor's paid group.</summary>
    public static Payment ForMentorship(Guid mentorRequestId, Guid payerUserId, Guid mentorProfileId,
        decimal amount, string currency) =>
        new(payerUserId, amount, currency)
        {
            MentorRequestId = mentorRequestId,
            MentorProfileId = mentorProfileId,
        };

    private Payment(Guid payerUserId, decimal amount, string currency)
    {
        PayerUserId = payerUserId;
        Amount = amount;
        Currency = currency.Trim().ToUpperInvariant();
        PlatformFeeAmount = Math.Round(amount * PlatformCommissionRate, 2);
        CounsellorAmount = amount - PlatformFeeAmount;
    }

    // Exactly one of these pairs is set: a payment is either for a counselling session or for a
    // mentorship place. Both were non-nullable when counselling was the only thing anyone paid
    // for; mentorship charging made them a choice.
    public Guid? CounsellingSessionId { get; private set; }
    public Guid? CounsellorId { get; private set; }
    public Guid? MentorRequestId { get; private set; }
    public Guid? MentorProfileId { get; private set; }
    public bool IsMentorship => MentorRequestId is not null;

    public Guid PayerUserId { get; private set; }
    public decimal Amount { get; private set; }
    public decimal PlatformFeeAmount { get; private set; }
    /// <summary>The practitioner's share after the platform fee — the counsellor's or the mentor's.</summary>
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

    // Refunds. Mirage only ever refunds a session in full — the published policy has no partial
    // case — but the amount is stored rather than implied so a future partial refund, or a
    // provider that returns less than was asked for, is recorded truthfully.
    public decimal? RefundedAmount { get; private set; }
    public DateTimeOffset? RefundedAt { get; private set; }
    public RefundReason? RefundReason { get; private set; }
    public string? RefundProviderReference { get; private set; }
    public string? RefundNote { get; private set; }
    /// <summary>Null when the refund was automatic under the cancellation policy.</summary>
    public Guid? RefundedByUserId { get; private set; }

    public CounsellingSession? CounsellingSession { get; private set; }
    public MentorRequest? MentorRequest { get; private set; }

    public bool IsRefundable =>
        Status == PaymentStatus.Successful && PayoutStatus is not (PayoutStatus.Paid or PayoutStatus.Processing);

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

    /// <summary>
    /// Records a refund the provider has already accepted. A payout still sitting with us is
    /// cancelled in the same breath: the money is going back to the member, so the counsellor is
    /// never paid for that session. A payout already paid or in flight cannot be unwound here —
    /// <see cref="IsRefundable"/> gates that, and it becomes a finance matter instead.
    /// </summary>
    public void MarkRefunded(decimal refundedAmount, RefundReason reason, string? providerReference,
        string? note, Guid? refundedByUserId)
    {
        if (Status == PaymentStatus.Refunded) return;
        if (Status != PaymentStatus.Successful)
            throw new InvalidOperationException("Only a successful payment can be refunded.");
        if (PayoutStatus is PayoutStatus.Paid or PayoutStatus.Processing)
            throw new InvalidOperationException(IsMentorship
                ? "The mentor has already been paid, so this cannot be refunded automatically."
                : "The counsellor has already been paid for this session, so it cannot be refunded automatically.");
        if (refundedAmount <= 0 || refundedAmount > Amount)
            throw new InvalidOperationException("The refund amount must be positive and no more than the amount paid.");

        Status = PaymentStatus.Refunded;
        RefundedAmount = decimal.Round(refundedAmount, 2);
        RefundedAt = DateTimeOffset.UtcNow;
        RefundReason = reason;
        RefundProviderReference = providerReference;
        RefundNote = note?.Trim() is { Length: > 0 } trimmed ? trimmed[..Math.Min(trimmed.Length, 500)] : null;
        RefundedByUserId = refundedByUserId;
        PayoutStatus = PayoutStatus.Cancelled;
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
