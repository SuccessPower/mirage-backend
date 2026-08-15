using System.Net;
using Amazon.Runtime;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Mirage.Application.Abstractions;
using Mirage.Domain.Enums;

namespace Mirage.Infrastructure.Email;

// Uses the SES v2 HTTPS API rather than SMTP because many PaaS hosts block outbound
// SMTP ports. Authentication is supplied by the standard AWS credential provider chain.
public sealed class AmazonSesEmailService : IEmailService
{
    private const string DefaultAppUrl = "https://www.themiragehub.com";
    private const string DefaultBrandLogoUrl =
        "https://res.cloudinary.com/dl2z33x6z/image/upload/v1785248851/Asset_3Mirage_obqm6m.png";
    private readonly IAmazonSimpleEmailServiceV2 _ses;
    private readonly IConfiguration _config;
    private readonly ILogger<AmazonSesEmailService> _logger;
    private readonly string _brandLogoUrl;

    public AmazonSesEmailService(
        IAmazonSimpleEmailServiceV2 ses,
        IConfiguration config,
        ILogger<AmazonSesEmailService> logger)
    {
        _ses = ses;
        _config = config;
        _logger = logger;
        _brandLogoUrl = config["Brand:LogoUrl"]?.Trim() is { Length: > 0 } configuredLogoUrl
            ? configuredLogoUrl
            : DefaultBrandLogoUrl;

    }

    public Task<bool> SendWelcomeEmailAsync(string toEmail, string displayName, string? confirmUrl = null,
        CancellationToken cancellationToken = default)
    {
        var appUrl = _config["Frontend:BaseUrl"] ?? "https://mirage-ui-iota.vercel.app";
        return SendAsync(toEmail, $"Welcome to Mirage, {displayName}!",
            EmailTemplates.Welcome(displayName, appUrl, confirmUrl), cancellationToken);
    }

    public Task SendEmailConfirmationAsync(string toEmail, string displayName, string confirmUrl,
        CancellationToken cancellationToken = default) =>
        SendAsync(toEmail, "Confirm your Mirage email address",
            EmailTemplates.EmailConfirmation(displayName, confirmUrl), cancellationToken);

    public Task SendPasswordChangedEmailAsync(string toEmail, string displayName,
        CancellationToken cancellationToken = default) =>
        SendAsync(toEmail, "Your Mirage password was changed",
            EmailTemplates.PasswordChanged(displayName), cancellationToken);

    public Task SendAccountClosedEmailAsync(string toEmail, string displayName, bool permanent,
        CancellationToken cancellationToken = default) =>
        SendAsync(toEmail, permanent ? "Your Mirage account has been deleted" : "Your Mirage account has been deactivated",
            EmailTemplates.AccountClosed(displayName, permanent), cancellationToken);

    public Task SendPasswordResetEmailAsync(string toEmail, string displayName, string resetUrl,
        CancellationToken cancellationToken = default) =>
        SendAsync(toEmail, "Reset your Mirage password",
            EmailTemplates.PasswordReset(displayName, resetUrl), cancellationToken);

    public Task SendPaymentConfirmedEmailAsync(string toEmail, string displayName, string description,
        decimal amount, string currency, CancellationToken cancellationToken = default) =>
        SendAsync(toEmail, $"Payment confirmed: {description}",
            EmailTemplates.PaymentConfirmed(displayName, description, amount, currency), cancellationToken);

    public bool HasNotificationTemplate(NotificationType type) => EmailTemplates.HasTemplate(type);

    public Task SendProfileVisitEmailAsync(string toEmail, string displayName, string visitorName,
        string? visitorAvatarUrl, bool revealIdentity, string profileUrl,
        CancellationToken cancellationToken = default) =>
        SendAsync(toEmail,
            revealIdentity ? $"{visitorName} viewed your Mirage profile" : "Someone viewed your Mirage profile",
            EmailTemplates.ProfileVisit(displayName, visitorName, visitorAvatarUrl, revealIdentity, profileUrl),
            cancellationToken);

    public Task SendNotificationEmailAsync(string toEmail, string displayName, NotificationType type, string title,
        string body, string? actionUrl = null, string? actionLabel = null,
        CancellationToken cancellationToken = default) =>
        SendAsync(toEmail, title,
            EmailTemplates.Notification(type, displayName, title, body, actionUrl, actionLabel), cancellationToken);

    public Task<bool> SendContactEmailAsync(string recipientEmail, string senderName, string senderEmail,
        string country, string reason, string message, CancellationToken cancellationToken = default) =>
        SendAsync(recipientEmail, $"Mirage contact: {reason}",
            EmailTemplates.ContactSubmission(senderName, senderEmail, country, reason, message),
            cancellationToken, senderEmail);

    public Task<bool> SendAdminInformationRequestEmailAsync(string toEmail, string displayName, string message,
        string profileUrl, CancellationToken cancellationToken = default) =>
        SendAsync(toEmail, "Action needed: please update your Mirage profile",
            EmailTemplates.AdminInformationRequest(displayName, message, profileUrl), cancellationToken);

    public Task<bool> SendProfileWarningEmailAsync(string toEmail, string displayName, string message,
        DateTimeOffset? deadline, string profileUrl, CancellationToken cancellationToken = default) =>
        SendAsync(toEmail, deadline is null
                ? "Your Mirage account has been suspended"
                : "Action needed: please update your Mirage profile",
            EmailTemplates.ProfileWarning(displayName, message, deadline, profileUrl), cancellationToken);

    public Task<bool> SendConductWarningEmailAsync(string toEmail, string displayName, string message,
        DateTimeOffset? deadline, string profileUrl, CancellationToken cancellationToken = default) =>
        SendAsync(toEmail, deadline is null
                ? "Your Mirage account has been suspended"
                : "Your Mirage account is under review",
            EmailTemplates.ConductWarning(displayName, message, deadline, profileUrl), cancellationToken);

    public Task<bool> SendCelebrationEmailAsync(string toEmail, string displayName, CelebrationType type,
        string storyUrl, CancellationToken cancellationToken = default)
    {
        var subject = type == CelebrationType.Birthday
            ? $"🎉 Happy Birthday, {displayName}!"
            : $"💍 Happy Anniversary, {displayName}!";
        return SendAsync(toEmail, subject, EmailTemplates.Celebration(type, displayName, storyUrl), cancellationToken);
    }

    public Task<bool> SendReEngagementEmailAsync(string toEmail, string displayName, string title, string intro,
        string appUrl, IReadOnlyList<(string Heading, string Blurb, string Url)> highlights,
        CancellationToken cancellationToken = default) =>
        SendAsync(toEmail, title, EmailTemplates.ReEngagement(displayName, title, intro, appUrl, highlights),
            cancellationToken);

    public Task<bool> SendNewsletterAsync(string toEmail, string displayName, string subject, string title,
        string excerpt, string contentHtml, IReadOnlyList<string> imageUrls, string newsletterUrl,
        string unsubscribeUrl, string? thumbnailUrl = null, CancellationToken cancellationToken = default) =>
        SendAsync(toEmail, subject, NewsletterEmailTemplate.Render(displayName, title, excerpt, contentHtml,
            imageUrls, newsletterUrl, unsubscribeUrl,
            NewsletterEmailTemplate.SocialLinks(_config), thumbnailUrl, _brandLogoUrl), cancellationToken,
            fromName: NewsletterEmailTemplate.SenderName(_config));

    public Task<bool> SendPlatformManagerInviteAsync(string toEmail, string inviteUrl,
        CancellationToken cancellationToken = default) => SendAsync(toEmail,
            "You're invited to manage Mirage newsletters",
            NewsletterEmailTemplate.PlatformManagerInvite(inviteUrl, NewsletterEmailTemplate.SocialLinks(_config),
                _brandLogoUrl),
            cancellationToken);

    private async Task<bool> SendAsync(string to, string subject, string html, CancellationToken cancellationToken,
        string? replyTo = null, string? fromName = null)
    {
        html = ApplyBranding(html);
        var from = EmailSender.WithDisplayName(
            _config["AmazonSes:From"] ?? "Mirage <noreply@themiragehub.com>", fromName);
        var request = new SendEmailRequest
        {
            FromEmailAddress = from,
            Destination = new Destination
            {
                ToAddresses = [to]
            },
            Content = new EmailContent
            {
                Simple = new Message
                {
                    Subject = new Content { Data = subject, Charset = "UTF-8" },
                    Body = new Body
                    {
                        Html = new Content { Data = html, Charset = "UTF-8" }
                    }
                }
            },
            ReplyToAddresses = string.IsNullOrWhiteSpace(replyTo) ? [] : [replyTo]
        };

        try
        {
            var response = await _ses.SendEmailAsync(request, cancellationToken);
            _logger.LogInformation(
                "Email accepted by Amazon SES for {To} — subject: {Subject}, message ID: {MessageId}",
                to, subject, response.MessageId);
            return true;
        }
        catch (AmazonSimpleEmailServiceV2Exception ex)
        {
            _logger.LogError(ex,
                "Amazon SES rejected email to {To} — subject: {Subject}, AWS error: {ErrorCode}. " +
                "Verify the identity, region, credentials, IAM permissions, and SES production access.",
                to, subject, ex.ErrorCode);
            return false;
        }
        catch (AmazonClientException ex)
        {
            _logger.LogError(ex,
                "Amazon SES client failed before sending email to {To} — subject: {Subject}. " +
                "Check the AWS credential chain and network connectivity.",
                to, subject);
            return false;
        }
    }

    private string ApplyBranding(string html)
    {
        var appUrl = _config["Frontend:BaseUrl"]?.Trim().TrimEnd('/') is { Length: > 0 } configuredAppUrl
            ? configuredAppUrl
            : DefaultAppUrl;
        html = html.Replace("{{APP_URL}}", WebUtility.HtmlEncode(appUrl), StringComparison.Ordinal);
        var safeLogoUrl = WebUtility.HtmlEncode(_brandLogoUrl);
        if (html.Contains("{{BRAND_LOGO_URL}}", StringComparison.Ordinal))
            return html.Replace("{{BRAND_LOGO_URL}}", safeLogoUrl, StringComparison.Ordinal);

        // A few operational emails intentionally use compact standalone markup instead of the
        // shared template layout. Inject the same brand header after <body> so those messages do
        // not silently drift from the transactional-email identity.
        var bodyStart = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
        var bodyTagEnd = bodyStart < 0 ? -1 : html.IndexOf('>', bodyStart);
        if (bodyTagEnd < 0) return html;

        var header = $"""
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0">
              <tr><td align="center" style="padding:24px 16px 0">
                <table role="presentation" cellpadding="0" cellspacing="0"><tr>
                  <td><img src="{safeLogoUrl}" width="36" height="36" alt="Mirage" style="display:block;width:36px;height:36px;object-fit:contain;border:0;outline:none"/></td>
                  <td style="padding-left:10px;font-family:Arial,sans-serif;font-size:18px;font-weight:700;color:#f4f0ff">Mirage</td>
                </tr></table>
              </td></tr>
            </table>
            """;
        return html.Insert(bodyTagEnd + 1, header);
    }

}
