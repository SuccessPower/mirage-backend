using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Mirage.Application.Abstractions;
using Mirage.Domain.Enums;

namespace Mirage.Infrastructure.Email;

public sealed class MailjetSmtpEmailService : IEmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<MailjetSmtpEmailService> _logger;

    public MailjetSmtpEmailService(IConfiguration config, ILogger<MailjetSmtpEmailService> logger)
    {
        _config = config;
        _logger = logger;
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
        var apiKey = _config["Mailjet:ApiKey"];
        var secretKey = _config["Mailjet:SecretKey"];
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(secretKey))
        {
            _logger.LogWarning("Mailjet:ApiKey/Mailjet:SecretKey not configured — skipping email to {To} ({Subject})", to, subject);
            return;
        }

        var host = _config["Mailjet:SmtpHost"] ?? "in-v3.mailjet.com";
        var port = int.TryParse(_config["Mailjet:SmtpPort"], out var p) ? p : 587;
        var from = _config["Mailjet:From"] ?? "Mirage <onboarding@mirageapp.dev>";

        using var message = new MailMessage
        {
            From = new MailAddress(ParseAddress(from), ParseName(from)),
            Subject = subject,
            Body = html,
            IsBodyHtml = true,
        };
        message.To.Add(to);

        using var client = new SmtpClient(host, port)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(apiKey, secretKey),
        };

        try
        {
            await client.SendMailAsync(message, cancellationToken);
            _logger.LogInformation("Email sent via Mailjet SMTP to {To} — subject: {Subject}", to, subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email via Mailjet SMTP to {To} — subject: {Subject}", to, subject);
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
