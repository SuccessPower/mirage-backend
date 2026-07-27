using System.ComponentModel.DataAnnotations;

namespace Mirage.Application.Contact;

public static class ContactSubmissionValidator
{
    public static readonly IReadOnlySet<string> AllowedReasons = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Account support", "Billing", "Safety concern", "Feedback", "Partnership", "Other"
    };

    public static IReadOnlyList<(string Field, string Error)> Validate(
        string? fullName, string? email, string? country, string? reason, string? message)
    {
        var errors = new List<(string Field, string Error)>();
        if (string.IsNullOrWhiteSpace(fullName) || fullName.Trim().Length is < 2 or > 100)
            errors.Add(("fullName", "Enter a name between 2 and 100 characters."));
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254 ||
            !new EmailAddressAttribute().IsValid(email))
            errors.Add(("email", "Enter a valid email address."));
        if (string.IsNullOrWhiteSpace(country) || country.Trim().Length > 100)
            errors.Add(("country", "Select your country."));
        if (!AllowedReasons.Contains(reason?.Trim() ?? string.Empty))
            errors.Add(("reason", "Select a valid reason for contacting us."));
        if (string.IsNullOrWhiteSpace(message) || message.Trim().Length is < 10 or > 4000)
            errors.Add(("message", "Enter a message between 10 and 4,000 characters."));
        return errors;
    }
}
