using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Security;
using Mirage.Api.Services;
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

        var reference = $"mirage-{payment.Id:N}-{DateTimeOffset.UtcNow.Ticks}";
        payment.Initialize(request.Provider, request.Method, reference);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var result = request.Provider switch
            {
                PaymentProvider.Paystack => await paystack.InitializeAsync(payment, payerEmail, request.Method, cancellationToken),
                PaymentProvider.Flutterwave => await flutterwave.InitializeAsync(payment, payerEmail, request.Method,
                    $"{configuration["Frontend:BaseUrl"]}/counselling", cancellationToken),
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
        MirageDbContext db, NotificationService notifications, CancellationToken cancellationToken)
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

        db.AnonymityAuditLogs.Add(new AnonymityAuditLog(session.Id, payment.PayerUserId,
            "Session requested after payment confirmation"));
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok();
    }

    private static async Task<IResult> PaystackWebhook(HttpContext context, MirageDbContext db,
        NotificationService notifications, PaystackService paystack, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(context.Request.Body);
        var rawBody = await reader.ReadToEndAsync(cancellationToken);
        if (!paystack.VerifySignature(rawBody, context.Request.Headers["x-paystack-signature"]))
            return Results.Unauthorized();

        var result = paystack.ParseWebhook(rawBody);
        if (result.Successful && result.ProviderReference is not null)
        {
            var payment = await db.Payments.SingleOrDefaultAsync(
                x => x.ProviderReference == result.ProviderReference, cancellationToken);
            if (payment is not null)
                return await ConfirmPaymentAsync(payment.Id, result.ProviderTransactionId ?? result.ProviderReference,
                    db, notifications, cancellationToken);
        }
        return Results.Ok();
    }

    private static async Task<IResult> FlutterwaveWebhook(HttpContext context, MirageDbContext db,
        NotificationService notifications, FlutterwaveService flutterwave, CancellationToken cancellationToken)
    {
        if (!flutterwave.VerifySignature(context.Request.Headers["verif-hash"]))
            return Results.Unauthorized();

        using var reader = new StreamReader(context.Request.Body);
        var rawBody = await reader.ReadToEndAsync(cancellationToken);
        var result = flutterwave.ParseWebhook(rawBody);
        if (result.Successful && result.ProviderReference is not null)
        {
            var payment = await db.Payments.SingleOrDefaultAsync(
                x => x.ProviderReference == result.ProviderReference, cancellationToken);
            if (payment is not null)
                return await ConfirmPaymentAsync(payment.Id, result.ProviderTransactionId ?? result.ProviderReference,
                    db, notifications, cancellationToken);
        }
        return Results.Ok();
    }
}
