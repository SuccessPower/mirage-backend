using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.RateLimiting;
using Mirage.Api.Contracts;
using Mirage.Application.Abstractions;
using Mirage.Application.Contact;

namespace Mirage.Api.Endpoints;

internal static class ContactEndpoints
{
    public static RouteGroupBuilder MapContactEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/contact", Submit)
            .WithTags("Contact")
            .RequireRateLimiting("contact");
        return api;
    }

    private static async Task<IResult> Submit(ContactRequest request, HttpContext context,
        IEmailService email, IConfiguration configuration, CancellationToken cancellationToken)
    {
        // Honeypot: bots commonly fill every field. Return the normal response without sending.
        if (!string.IsNullOrWhiteSpace(request.Website))
            return ApiResults.Ok(context, new { }, "Your message has been sent.");

        var errors = ContactSubmissionValidator.Validate(
            request.FullName, request.Email, request.Country, request.Reason, request.Message);
        if (errors.Count > 0)
            return EndpointHelpers.ValidationProblem(context, errors.ToArray());

        var recipient = configuration["Contact:RecipientEmail"];
        if (string.IsNullOrWhiteSpace(recipient) || !new EmailAddressAttribute().IsValid(recipient))
            return EndpointHelpers.Problem(context, StatusCodes.Status503ServiceUnavailable,
                "Contact service unavailable", "Contact delivery is not configured yet. Please try again later.");

        var sent = await email.SendContactEmailAsync(recipient, request.FullName.Trim(), request.Email.Trim(),
            request.Country.Trim(), request.Reason!.Trim(), request.Message.Trim(), cancellationToken);
        if (!sent)
            return EndpointHelpers.Problem(context, StatusCodes.Status503ServiceUnavailable,
                "Message not sent", "We could not send your message right now. Please try again later.");

        return ApiResults.Ok(context, new { }, "Your message has been sent.");
    }
}
