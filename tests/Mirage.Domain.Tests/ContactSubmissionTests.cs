using Mirage.Application.Contact;
using Mirage.Infrastructure.Email;
using Xunit;

namespace Mirage.Domain.Tests;

public sealed class ContactSubmissionTests
{
    [Fact]
    public void Valid_contact_submission_has_no_validation_errors()
    {
        var errors = ContactSubmissionValidator.Validate(
            "Ada Lovelace", "ada@example.com", "Nigeria", "Account support",
            "I need help updating my account details.");

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("", "email")]
    [InlineData("not-an-email", "email")]
    [InlineData("person@example.com", null)]
    public void Email_validation_rejects_invalid_values(string email, string? expectedField)
    {
        var errors = ContactSubmissionValidator.Validate(
            "Ada Lovelace", email, "Nigeria", "Feedback", "This is useful feedback.");

        if (expectedField is null)
            Assert.DoesNotContain(errors, error => error.Field == "email");
        else
            Assert.Contains(errors, error => error.Field == expectedField);
    }

    [Fact]
    public void Contact_validation_rejects_unknown_reason_and_oversized_message()
    {
        var errors = ContactSubmissionValidator.Validate(
            "Ada Lovelace", "ada@example.com", "Nigeria", "Unexpected topic", new string('x', 4001));

        Assert.Contains(errors, error => error.Field == "reason");
        Assert.Contains(errors, error => error.Field == "message");
    }

    [Fact]
    public void Contact_email_encodes_user_content_and_does_not_embed_recipient_address()
    {
        var html = EmailTemplates.ContactSubmission(
            "<script>alert('name')</script>",
            "sender@example.com",
            "Nigeria",
            "Feedback",
            "<img src=x onerror=alert(1)>");

        Assert.DoesNotContain("<script>", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img src=x", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("sender@example.com", html);
        Assert.DoesNotContain("hello@themiragehub.com", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Admin_information_request_encodes_message_and_links_to_the_users_profile()
    {
        var profileUrl = "https://themiragehub.com/profiles/11111111-1111-1111-1111-111111111111";
        var html = EmailTemplates.AdminInformationRequest(
            "Ada", "<script>unsafe()</script>\nPlease add identification.", profileUrl);

        Assert.DoesNotContain("<script>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("Please add identification.", html);
        Assert.Contains(profileUrl, html);
    }
}
