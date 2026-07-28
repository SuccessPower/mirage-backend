using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
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

    [Fact]
    public void Shared_email_footer_does_not_use_the_os_label()
    {
        var html = EmailTemplates.Welcome("Ada", "https://themiragehub.com");

        Assert.Contains("Relationship", html);
        Assert.DoesNotContain("Relationship OS", html);
    }

    [Fact]
    public async Task Email_delivery_uses_the_configured_brand_logo()
    {
        var handler = new CapturingHandler();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mailjet:ApiKey"] = "test-key",
                ["Mailjet:SecretKey"] = "test-secret",
                ["Brand:LogoUrl"] = "https://cdn.example.com/mirage.png?version=2&theme=light"
            })
            .Build();
        var service = new MailjetSmtpEmailService(
            new HttpClient(handler), configuration, NullLogger<MailjetSmtpEmailService>.Instance);

        var sent = await service.SendContactEmailAsync(
            "support@example.com", "Ada", "ada@example.com", "Nigeria", "Feedback", "Hello");

        Assert.True(sent);
        using var payload = JsonDocument.Parse(handler.RequestBody);
        var html = payload.RootElement
            .GetProperty("Messages")[0]
            .GetProperty("HTMLPart")
            .GetString()!;
        Assert.Contains(
            "https://cdn.example.com/mirage.png?version=2&amp;theme=light",
            html);
        Assert.DoesNotContain("{{BRAND_LOGO_URL}}", html);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"Messages":[{"Status":"success","To":[{"Email":"support@example.com"}]}]}""",
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }
}
