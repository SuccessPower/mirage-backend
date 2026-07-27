using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.RateLimiting;
using Mirage.Api.Contracts;
using Mirage.Application.Abstractions;

namespace Mirage.Api.Endpoints;

internal static class ContactEndpoints
{
    private static readonly HashSet<string> AllowedReasons = new(StringComparer.OrdinalIgnoreCase)
    {
        "Account support", "Billing", "Safety concern", "Feedback", "Partnership", "Other"
    };

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

        var errors = new List<(string Field, string Error)>();
        if (string.IsNullOrWhiteSpace(request.FullName) || request.FullName.Trim().Length is < 2 or > 100)
            errors.Add(("fullName", "Enter a name between 2 and 100 characters."));
        if (string.IsNullOrWhiteSpace(request.Email) || request.Email.Length > 254 ||
            !new EmailAddressAttribute().IsValid(request.Email))
            errors.Add(("email", "Enter a valid email address."));
        if (string.IsNullOrWhiteSpace(request.Country) || request.Country.Trim().Length > 100)
            errors.Add(("country", "Select your country."));
        if (!AllowedReasons.Contains(request.Reason?.Trim() ?? string.Empty))
            errors.Add(("reason", "Select a valid reason for contacting us."));
        if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Trim().Length is < 10 or > 4000)
            errors.Add(("message", "Enter a message between 10 and 4,000 characters."));
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
