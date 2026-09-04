using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;

namespace Mirage.Api.Services;

/// <summary>The outcome of trying to give a member their money back. <paramref name="Refunded"/>
/// is false for both "nothing to refund" (an unpaid or free session) and "the provider refused" —
/// <paramref name="Message"/> is written to be shown to whoever asked.</summary>
public sealed record RefundOutcome(bool Refunded, decimal Amount, string Message);

/// <summary>
/// One path for every refund, whether it fires automatically when a session is cancelled inside
/// the published policy or an admin issues it by hand. Keeping it in one place is what stops the
/// two drifting apart: the money moves at the provider first, and only a provider that accepted
/// the refund is written into the payment.
/// </summary>
public sealed class RefundService(
    IMirageDbContext db,
    PaystackService paystack,
    FlutterwaveService flutterwave,
    NotificationService notifications,
    ILogger<RefundService> logger)
{
    public async Task<RefundOutcome> RefundAsync(Payment payment, RefundReason reason, string? note,
        Guid? issuedByUserId, CancellationToken cancellationToken)
    {
        if (payment.Status == PaymentStatus.Refunded)
            return new RefundOutcome(false, 0, "This payment has already been refunded.");
        if (payment.Status != PaymentStatus.Successful)
            return new RefundOutcome(false, 0, "This session was never paid for, so there is nothing to refund.");
        if (!payment.IsRefundable)
            return new RefundOutcome(false, 0,
                "The counsellor has already been paid out for this session. Recover it through finance rather than an automatic refund.");

        var amount = payment.Amount;
        var merchantNote = note ?? $"Mirage session refund ({reason})";

        RefundResult result;
        try
        {
            result = payment.Provider switch
            {
                PaymentProvider.Paystack => await paystack.RefundAsync(payment, amount, merchantNote, cancellationToken),
                PaymentProvider.Flutterwave => await flutterwave.RefundAsync(payment, amount, merchantNote, cancellationToken),
                _ => new RefundResult(false, null, "This payment has no provider recorded against it."),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A provider that is down must not leave the payment marked refunded — the member
            // would be told the money is coming and it never would be.
            logger.LogError(ex, "Refund call to {Provider} failed for payment {PaymentId}",
                payment.Provider, payment.Id);
            return new RefundOutcome(false, 0,
                "The payment provider could not be reached. The session is cancelled; please retry the refund.");
        }

        if (!result.Accepted)
        {
            logger.LogWarning("Refund refused by {Provider} for payment {PaymentId}: {Message}",
                payment.Provider, payment.Id, result.FailureMessage);
            return new RefundOutcome(false, 0,
                result.FailureMessage ?? "The payment provider declined the refund. Please retry or contact support.");
        }

        payment.MarkRefunded(amount, reason, result.ProviderReference, note, issuedByUserId);
        await db.SaveChangesAsync(cancellationToken);

        var what = payment.IsMentorship ? "mentorship place" : "cancelled session";
        await notifications.NotifyAsync(payment.PayerUserId, NotificationType.PaymentRefunded,
            "Refund on its way",
            $"{payment.Currency} {amount:N2} for your {what} has been refunded to your original payment method. It usually arrives within 5–10 business days.",
            payment.MentorRequestId ?? payment.CounsellingSessionId,
            payment.IsMentorship ? "MentorRequest" : "CounsellingSession", cancellationToken);

        logger.LogInformation("Refunded {Currency} {Amount} for payment {PaymentId} ({Reason})",
            payment.Currency, amount, payment.Id, reason);

        return new RefundOutcome(true, amount,
            $"{payment.Currency} {amount:N2} refunded to the original payment method. It usually arrives within 5–10 business days.");
    }

    /// <summary>
    /// The published cancellation policy, in one place: the counsellor calling it off always
    /// refunds, and the member gets their money back as long as they give a day's notice.
    /// Inside 24 hours the counsellor's time is already committed, so the fee stands.
    /// </summary>
    public static bool PolicyAllowsRefund(bool cancelledByCounsellor, DateTimeOffset scheduledAt) =>
        cancelledByCounsellor || scheduledAt - DateTimeOffset.UtcNow >= TimeSpan.FromHours(24);
}
