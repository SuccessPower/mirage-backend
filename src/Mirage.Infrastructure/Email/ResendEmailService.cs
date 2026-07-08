using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Mirage.Application.Abstractions;
using Mirage.Domain.Enums;

namespace Mirage.Infrastructure.Email;

public sealed class ResendEmailService : IEmailService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<ResendEmailService> _logger;

    public ResendEmailService(HttpClient http, IConfiguration config, ILogger<ResendEmailService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;

        var apiKey = _config["Resend:ApiKey"];
        _http.BaseAddress = new Uri("https://api.resend.com/");
        if (!string.IsNullOrWhiteSpace(apiKey))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public Task SendWelcomeEmailAsync(string toEmail, string displayName, CancellationToken cancellationToken = default)
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

    private async Task SendAsync(string to, string subject, string html, CancellationToken cancellationToken)
    {
        var apiKey = _config["Resend:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Resend:ApiKey is not configured — skipping email to {To} ({Subject})", to, subject);
            return;
        }

        var from = _config["Resend:From"] ?? "Mirage <onboarding@resend.dev>";
        var payload = new { from, to = new[] { to }, subject, html };
        var body = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        try
        {
            var response = await _http.PostAsync("emails", body, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError(
                    "Resend rejected email to {To} — HTTP {Status}: {Detail}. Ensure the From domain is verified in Resend.",
                    to, (int)response.StatusCode, detail);
            }
            else
            {
                _logger.LogInformation("Email sent via Resend to {To} — subject: {Subject}", to, subject);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {To} — subject: {Subject}", to, subject);
        }
    }
}
