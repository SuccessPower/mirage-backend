using Mirage.Domain.Enums;

namespace Mirage.Application.Abstractions;

public interface IEmailService
{
    // Returns whether the send succeeded, so callers can record delivery (e.g. WelcomeEmailSentAt)
    // and retry later on failure instead of assuming it always went out.
    Task<bool> SendWelcomeEmailAsync(string toEmail, string displayName, CancellationToken cancellationToken = default);

    Task SendPasswordChangedEmailAsync(string toEmail, string displayName,
        CancellationToken cancellationToken = default);

    Task SendPasswordResetEmailAsync(string toEmail, string displayName, string resetUrl,
        CancellationToken cancellationToken = default);

    Task SendPaymentConfirmedEmailAsync(string toEmail, string displayName, string description, decimal amount,
        string currency, CancellationToken cancellationToken = default);

    // Dispatches to the dedicated template for `type` (see EmailTemplates.TypeTemplates in
    // Mirage.Infrastructure). Callers should check HasNotificationTemplate first.
    Task SendNotificationEmailAsync(string toEmail, string displayName, NotificationType type, string title,
        string body, string? actionUrl = null, string? actionLabel = null, CancellationToken cancellationToken = default);

    bool HasNotificationTemplate(NotificationType type);
}
