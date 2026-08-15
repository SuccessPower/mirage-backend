using Mirage.Domain.Enums;

namespace Mirage.Application.Abstractions;

public interface IEmailService
{
    // Returns whether the send succeeded, so callers can record delivery (e.g. WelcomeEmailSentAt)
    // and retry later on failure instead of assuming it always went out. When confirmUrl is
    // supplied, the welcome email doubles as the confirmation email — one send instead of two,
    // so a new signup doesn't have to wait on (or receive) a second message.
    Task<bool> SendWelcomeEmailAsync(string toEmail, string displayName, string? confirmUrl = null,
        CancellationToken cancellationToken = default);

    Task SendEmailConfirmationAsync(string toEmail, string displayName, string confirmUrl,
        CancellationToken cancellationToken = default);

    Task SendPasswordChangedEmailAsync(string toEmail, string displayName,
        CancellationToken cancellationToken = default);

    Task SendAccountClosedEmailAsync(string toEmail, string displayName, bool permanent,
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

    Task SendProfileVisitEmailAsync(string toEmail, string displayName, string visitorName,
        string? visitorAvatarUrl, bool revealIdentity, string profileUrl,
        CancellationToken cancellationToken = default);

    Task<bool> SendContactEmailAsync(string recipientEmail, string senderName, string senderEmail,
        string country, string reason, string message, CancellationToken cancellationToken = default);

    Task<bool> SendAdminInformationRequestEmailAsync(string toEmail, string displayName, string message,
        string profileUrl, CancellationToken cancellationToken = default);

    // Warning that fictitious or non-compliant profile content (photos, name, DOB, bio) needs
    // fixing by `deadline`, or the account becomes eligible for suspension. A null deadline means
    // the account was already suspended immediately — the email tells the member that instead of
    // giving them time to fix it. See SendConductWarningEmailAsync for behavioural warnings.
    Task<bool> SendProfileWarningEmailAsync(string toEmail, string displayName, string message,
        DateTimeOffset? deadline, string profileUrl, CancellationToken cancellationToken = default);

    // Warning for a confirmed content-report violation (harassing, harmful, or otherwise
    // rule-breaking posts/comments) — distinct from SendProfileWarningEmailAsync because the
    // remedy here is behavioural, not a profile edit. Null deadline = immediate suspension.
    Task<bool> SendConductWarningEmailAsync(string toEmail, string displayName, string message,
        DateTimeOffset? deadline, string profileUrl, CancellationToken cancellationToken = default);

    // Sent alongside the in-app celebration post (see CelebrationPostService) when a member's
    // birthday or wedding anniversary falls on today's date.
    Task<bool> SendCelebrationEmailAsync(string toEmail, string displayName, CelebrationType type,
        string storyUrl, CancellationToken cancellationToken = default);

    // The "we've missed you" nudge for dormant accounts (see ReEngagementService). `highlights`
    // is the caller-composed tour of the app, since it varies by relationship status.
    Task<bool> SendReEngagementEmailAsync(string toEmail, string displayName, string title, string intro,
        string appUrl, IReadOnlyList<(string Heading, string Blurb, string Url)> highlights,
        CancellationToken cancellationToken = default);

    Task<bool> SendNewsletterAsync(string toEmail, string displayName, string subject, string title,
        string excerpt, string contentHtml, IReadOnlyList<string> imageUrls, string newsletterUrl,
        string unsubscribeUrl, string? thumbnailUrl = null, CancellationToken cancellationToken = default);

    Task<bool> SendPlatformManagerInviteAsync(string toEmail, string inviteUrl,
        CancellationToken cancellationToken = default);
}
