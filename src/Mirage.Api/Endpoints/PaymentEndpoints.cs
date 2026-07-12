using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Security;
using Mirage.Api.Services;
using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;
using Mirage.Infrastructure.Persistence;

namespace Mirage.Api.Endpoints;

internal static class PaymentEndpoints
{
    public static RouteGroupBuilder MapPaymentEndpoints(this RouteGroupBuilder api)
    {
        var payments = api.MapGroup("/payments").WithTags("Payments");
        payments.MapPost("/{id:guid}/initialize", Initialize).RequireAuthorization();
        payments.MapGet("/{id:guid}", GetStatus).RequireAuthorization();
        payments.MapPost("/webhooks/paystack", PaystackWebhook);
        payments.MapPost("/webhooks/flutterwave", FlutterwaveWebhook);
        return api;
    }

    private static async Task<IResult> Initialize(Guid id, InitializePaymentRequest request, HttpContext context,
        MirageDbContext db, PaystackService paystack, FlutterwaveService flutterwave, IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var payment = await db.Payments.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (payment is null) return EndpointHelpers.NotFound(context, "Payment was not found.");
        if (payment.PayerUserId != userId) return EndpointHelpers.Forbidden(context);
        if (payment.Status == PaymentStatus.Successful)
            return EndpointHelpers.Conflict(context, "This payment has already been completed.");

        var payerEmail = await db.Users.AsNoTracking()
            .Where(x => x.Id == userId).Select(x => x.Email).SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(payerEmail))
            return EndpointHelpers.Problem(context, StatusCodes.Status400BadRequest,
                "Missing email", "Your account has no email address on file.");

        // No subaccount configured yet means the full amount lands in the platform account
        // instead of auto-splitting — booking already requires HasPayoutAccount before a
        // counsellor can charge at all, so this is a defensive fallback, not the normal path.
        var counsellor = await db.Counsellors.AsNoTracking()
            .Where(x => x.Id == payment.CounsellorId)
            .Select(x => new { x.PaystackSubaccountCode, x.FlutterwaveSubaccountId })
            .SingleAsync(cancellationToken);

        var reference = $"mirage-{payment.Id:N}-{DateTimeOffset.UtcNow.Ticks}";
        payment.Initialize(request.Provider, request.Method, reference);
        await db.SaveChangesAsync(cancellationToken);

        // Both providers land the browser back on this URL after checkout; the paymentId lets the
        // frontend poll GET /payments/{id} and show "session scheduled" once the webhook confirms it.
        var redirectUrl = $"{configuration["Frontend:BaseUrl"]}/counselling/payment-result?paymentId={payment.Id}";

        try
        {
            var result = request.Provider switch
            {
                PaymentProvider.Paystack => await paystack.InitializeAsync(payment, payerEmail, request.Method,
                    counsellor.PaystackSubaccountCode, redirectUrl, cancellationToken),
                PaymentProvider.Flutterwave => await flutterwave.InitializeAsync(payment, payerEmail, request.Method,
                    redirectUrl, counsellor.FlutterwaveSubaccountId, cancellationToken),
                _ => throw new InvalidOperationException("Unsupported payment provider."),
            };
            return ApiResults.Ok(context, result, "Payment initialized successfully.");
        }
        catch (Exception)
        {
            payment.MarkFailed();
            await db.SaveChangesAsync(cancellationToken);
            return EndpointHelpers.Problem(context, StatusCodes.Status502BadGateway,
                "Payment provider error", "Could not start the payment with the selected provider. Please try again.");
        }
    }

    private static async Task<IResult> GetStatus(Guid id, HttpContext context, MirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var payment = await db.Payments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (payment is null) return EndpointHelpers.NotFound(context, "Payment was not found.");
        if (payment.PayerUserId != userId) return EndpointHelpers.Forbidden(context);
        return ApiResults.Ok(context, new { payment.Id, payment.Status, payment.CounsellingSessionId },
            "Payment status retrieved successfully.");
    }

    private static async Task<IResult> ConfirmPaymentAsync(Guid paymentId, string providerTransactionId,
        MirageDbContext db, NotificationService notifications, IEmailService emailService,
        CancellationToken cancellationToken)
    {
        var payment = await db.Payments.SingleOrDefaultAsync(x => x.Id == paymentId, cancellationToken);
        if (payment is null || payment.Status == PaymentStatus.Successful) return Results.Ok();

        payment.MarkSuccessful(providerTransactionId);
        var session = await db.CounsellingSessions.Include(x => x.Counsellor)
            .SingleAsync(x => x.Id == payment.CounsellingSessionId, cancellationToken);
        session.ConfirmPayment();
        await db.SaveChangesAsync(cancellationToken);

        await notifications.NotifyAsync(payment.PayerUserId, NotificationType.PaymentConfirmed,
            "Payment confirmed", "Your payment was received — your session request has been sent to the counsellor.",
            session.Id, "CounsellingSession", cancellationToken);
        await notifications.NotifyAsync(session.Counsellor.UserId, NotificationType.SessionBooked,
            "New session request", $"A new {session.Type.ToString().ToLowerInvariant()} session was requested.",
            session.Id, "CounsellingSession", cancellationToken);

        var payer = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == payment.PayerUserId)
            .Select(x => new { x.DisplayName })
            .SingleOrDefaultAsync(cancellationToken);
        var payerEmail = await db.Users.AsNoTracking()
            .Where(x => x.Id == payment.PayerUserId).Select(x => x.Email).SingleOrDefaultAsync(cancellationToken);
        if (payerEmail is not null)
            await emailService.SendPaymentConfirmedEmailAsync(payerEmail, payer?.DisplayName ?? "there",
                $"{session.Type} counselling session", payment.Amount, payment.Currency, cancellationToken);

        db.AnonymityAuditLogs.Add(new AnonymityAuditLog(session.Id, payment.PayerUserId,
            "Session requested after payment confirmation"));
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok();
    }

    // A reference-bearing webhook that isn't a success (e.g. charge.failed/declined) still needs
    // to unstick the session — otherwise it sits in AwaitingPayment forever with no signal to the
    // client that checkout failed and they should retry.
    private static async Task MarkPaymentFailedAsync(string providerReference, MirageDbContext db,
        CancellationToken cancellationToken)
    {
        var payment = await db.Payments.Include(x => x.CounsellingSession)
            .SingleOrDefaultAsync(x => x.ProviderReference == providerReference, cancellationToken);
        if (payment is null || payment.Status == PaymentStatus.Successful) return;
        payment.MarkFailed();
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<IResult> PaystackWebhook(HttpContext context, MirageDbContext db,
        NotificationService notifications, IEmailService emailService, PaystackService paystack,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(context.Request.Body);
        var rawBody = await reader.ReadToEndAsync(cancellationToken);
        if (!paystack.VerifySignature(rawBody, context.Request.Headers["x-paystack-signature"]))
            return Results.Unauthorized();

        var result = paystack.ParseWebhook(rawBody);
        if (result.ProviderReference is not null)
        {
            if (result.Successful)
            {
                var payment = await db.Payments.SingleOrDefaultAsync(
                    x => x.ProviderReference == result.ProviderReference, cancellationToken);
                if (payment is not null)
                    return await ConfirmPaymentAsync(payment.Id, result.ProviderTransactionId ?? result.ProviderReference,
                        db, notifications, emailService, cancellationToken);
            }
            else
            {
                await MarkPaymentFailedAsync(result.ProviderReference, db, cancellationToken);
            }
        }
        return Results.Ok();
    }

    private static async Task<IResult> FlutterwaveWebhook(HttpContext context, MirageDbContext db,
        NotificationService notifications, IEmailService emailService, FlutterwaveService flutterwave,
        CancellationToken cancellationToken)
    {
        if (!flutterwave.VerifySignature(context.Request.Headers["verif-hash"]))
            return Results.Unauthorized();

        using var reader = new StreamReader(context.Request.Body);
        var rawBody = await reader.ReadToEndAsync(cancellationToken);
        var result = flutterwave.ParseWebhook(rawBody);
        if (result.ProviderReference is not null)
        {
            if (result.Successful)
            {
                var payment = await db.Payments.SingleOrDefaultAsync(
                    x => x.ProviderReference == result.ProviderReference, cancellationToken);
                if (payment is not null)
                    return await ConfirmPaymentAsync(payment.Id, result.ProviderTransactionId ?? result.ProviderReference,
                        db, notifications, emailService, cancellationToken);
            }
            else
            {
                await MarkPaymentFailedAsync(result.ProviderReference, db, cancellationToken);
            }
        }
        return Results.Ok();
    }
}
