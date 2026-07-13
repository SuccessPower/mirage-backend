using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Mirage.Application.Abstractions;
using Mirage.Domain.Enums;

namespace Mirage.Infrastructure.Email;

// Uses Mailjet's HTTPS Send API rather than raw SMTP — most PaaS hosts (Render included)
// block outbound SMTP ports (25/465/587) for anti-abuse reasons, which makes a real
// SmtpClient connection hang or fail silently. The HTTPS API uses the same API key /
// secret key as SMTP credentials, just over port 443.
public sealed class MailjetSmtpEmailService : IEmailService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<MailjetSmtpEmailService> _logger;

    public MailjetSmtpEmailService(HttpClient http, IConfiguration config, ILogger<MailjetSmtpEmailService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;

        var apiKey = _config["Mailjet:ApiKey"];
        var secretKey = _config["Mailjet:SecretKey"];
        _http.BaseAddress = new Uri("https://api.mailjet.com/v3.1/");
        if (!string.IsNullOrWhiteSpace(apiKey) && !string.IsNullOrWhiteSpace(secretKey))
        {
            var creds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:{secretKey}"));
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", creds);
        }
    }

    public Task<bool> SendWelcomeEmailAsync(string toEmail, string displayName, CancellationToken cancellationToken = default)
    {
        var appUrl = _config["Frontend:BaseUrl"] ?? "https://mirage-ui-iota.vercel.app";
        return SendAsync(toEmail, $"Welcome to Mirage, {displayName}!",
            EmailTemplates.Welcome(displayName, appUrl), cancellationToken);
    }

    public Task SendPasswordChangedEmailAsync(string toEmail, string displayName,
        CancellationToken cancellationToken = default) =>
        SendAsync(toEmail, "Your Mirage password was changed",
            EmailTemplates.PasswordChanged(displayName), cancellationToken);

    public Task SendPasswordResetEmailAsync(string toEmail, string displayName, string resetUrl,
        CancellationToken cancellationToken = default) =>
        SendAsync(toEmail, "Reset your Mirage password",
            EmailTemplates.PasswordReset(displayName, resetUrl), cancellationToken);

    public Task SendPaymentConfirmedEmailAsync(string toEmail, string displayName, string description,
        decimal amount, string currency, CancellationToken cancellationToken = default) =>
        SendAsync(toEmail, $"Payment confirmed: {description}",
            EmailTemplates.PaymentConfirmed(displayName, description, amount, currency), cancellationToken);

    public bool HasNotificationTemplate(NotificationType type) => EmailTemplates.HasTemplate(type);

    public Task SendNotificationEmailAsync(string toEmail, string displayName, NotificationType type, string title,
        string body, string? actionUrl = null, string? actionLabel = null,
        CancellationToken cancellationToken = default) =>
        SendAsync(toEmail, title,
            EmailTemplates.Notification(type, displayName, title, body, actionUrl, actionLabel), cancellationToken);

    private async Task<bool> SendAsync(string to, string subject, string html, CancellationToken cancellationToken)
    {
        var apiKey = _config["Mailjet:ApiKey"];
        var secretKey = _config["Mailjet:SecretKey"];
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(secretKey))
        {
            _logger.LogWarning("Mailjet:ApiKey/Mailjet:SecretKey not configured — skipping email to {To} ({Subject})", to, subject);
            return false;
        }

        var from = _config["Mailjet:From"] ?? "Mirage <onboarding@mirageapp.dev>";
        var fromEmail = ParseAddress(from);
        var fromName = ParseName(from);

        var payload = new
        {
            Messages = new[]
            {
                new
                {
                    From = new { Email = fromEmail, Name = fromName },
                    To = new[] { new { Email = to } },
                    Subject = subject,
                    HTMLPart = html,
                },
            },
        };
        var body = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        try
        {
            var response = await _http.PostAsync("send", body, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError(
                    "Mailjet rejected email to {To} — HTTP {Status}: {Detail}. Ensure the From address/domain is verified in Mailjet.",
                    to, (int)response.StatusCode, detail);
                return false;
            }

            _logger.LogInformation("Email sent via Mailjet API to {To} — subject: {Subject}", to, subject);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email via Mailjet API to {To} — subject: {Subject}", to, subject);
            return false;
        }
    }

    private static string ParseAddress(string from)
    {
        var start = from.IndexOf('<');
        return start >= 0 ? from[(start + 1)..from.IndexOf('>')] : from;
    }

    private static string ParseName(string from)
    {
        var start = from.IndexOf('<');
        return start >= 0 ? from[..start].Trim() : string.Empty;
    }
}
