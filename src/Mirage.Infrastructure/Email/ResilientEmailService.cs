using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Mirage.Application.Abstractions;
using Mirage.Domain.Enums;

namespace Mirage.Infrastructure.Email;

public sealed class ResilientEmailService : IEmailService
{
    private const string DefaultAppUrl = "https://www.themiragehub.com";
    private const string DefaultBrandLogoUrl =
        "https://res.cloudinary.com/dl2z33x6z/image/upload/v1785248851/Asset_3Mirage_obqm6m.png";
    private readonly IReadOnlyList<IEmailTransport> _transports;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ResilientEmailService> _logger;

    public ResilientEmailService(
        IEnumerable<IEmailTransport> transports,
        IConfiguration configuration,
        ILogger<ResilientEmailService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        var registered = transports.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        var order = configuration.GetSection("Email:ProviderOrder").Get<string[]>()
                    ?? ["ZeptoMail", "AmazonSes", "Mailjet"];
        _transports = order.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => registered.TryGetValue(name, out var transport)
                ? transport
                : throw new InvalidOperationException($"Unknown email provider '{name}'."))
            .ToArray();
    }

    public Task<bool> SendWelcomeEmailAsync(string toEmail, string displayName, string? confirmUrl = null,
        CancellationToken cancellationToken = default)
    {
        var appUrl = _configuration["Frontend:BaseUrl"] ?? "https://mirage-ui-iota.vercel.app";
        return SendAsync(toEmail, $"Welcome to Mirage, {displayName}!",
            EmailTemplates.Welcome(displayName, appUrl, confirmUrl), cancellationToken);
    }

    public async Task SendEmailConfirmationAsync(string toEmail, string displayName, string confirmUrl,
        CancellationToken cancellationToken = default) =>
        await SendAsync(toEmail, "Confirm your Mirage email address",
            EmailTemplates.EmailConfirmation(displayName, confirmUrl), cancellationToken);

    public async Task SendPasswordChangedEmailAsync(string toEmail, string displayName,
        CancellationToken cancellationToken = default) =>
        await SendAsync(toEmail, "Your Mirage password was changed",
            EmailTemplates.PasswordChanged(displayName), cancellationToken);

    public async Task SendAccountClosedEmailAsync(string toEmail, string displayName, bool permanent,
        CancellationToken cancellationToken = default) =>
        await SendAsync(toEmail,
            permanent ? "Your Mirage account has been deleted" : "Your Mirage account has been deactivated",
            EmailTemplates.AccountClosed(displayName, permanent), cancellationToken);

    public async Task SendPasswordResetEmailAsync(string toEmail, string displayName, string resetUrl,
        CancellationToken cancellationToken = default) =>
        await SendAsync(toEmail, "Reset your Mirage password",
            EmailTemplates.PasswordReset(displayName, resetUrl), cancellationToken);

    public async Task SendPaymentConfirmedEmailAsync(string toEmail, string displayName, string description,
        decimal amount, string currency, CancellationToken cancellationToken = default) =>
        await SendAsync(toEmail, $"Payment confirmed: {description}",
            EmailTemplates.PaymentConfirmed(displayName, description, amount, currency), cancellationToken);

    public bool HasNotificationTemplate(NotificationType type) => EmailTemplates.HasTemplate(type);

    public async Task SendProfileVisitEmailAsync(string toEmail, string displayName, string visitorName,
        string? visitorAvatarUrl, bool revealIdentity, string profileUrl,
        CancellationToken cancellationToken = default) =>
        await SendAsync(toEmail,
            revealIdentity ? $"{visitorName} viewed your Mirage profile" : "Someone viewed your Mirage profile",
            EmailTemplates.ProfileVisit(displayName, visitorName, visitorAvatarUrl, revealIdentity, profileUrl),
            cancellationToken);

    public async Task SendNotificationEmailAsync(string toEmail, string displayName, NotificationType type,
        string title, string body, string? actionUrl = null, string? actionLabel = null,
        CancellationToken cancellationToken = default) =>
        await SendAsync(toEmail, title,
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
        string unsubscribeUrl, string? authorName = null, string? authorAvatarUrl = null,
        string? thumbnailUrl = null, CancellationToken cancellationToken = default) =>
        SendAsync(toEmail, subject, NewsletterEmailTemplate.Render(displayName, title, excerpt, contentHtml,
            imageUrls, newsletterUrl, unsubscribeUrl,
            string.IsNullOrWhiteSpace(authorName) ? null : new NewsletterAuthor(authorName, authorAvatarUrl),
            NewsletterEmailTemplate.SocialLinks(_configuration), thumbnailUrl,
            NewsletterEmailTemplate.LogoUrl(_configuration)), cancellationToken,
            fromName: NewsletterEmailTemplate.SenderName(authorName, _configuration));

    public Task<bool> SendPlatformManagerInviteAsync(string toEmail, string inviteUrl,
        CancellationToken cancellationToken = default) => SendAsync(toEmail, "You're invited to manage Mirage newsletters",
            NewsletterEmailTemplate.PlatformManagerInvite(inviteUrl,
                NewsletterEmailTemplate.SocialLinks(_configuration),
                NewsletterEmailTemplate.LogoUrl(_configuration)), cancellationToken);

    private async Task<bool> SendAsync(string to, string subject, string html,
        CancellationToken cancellationToken, string? replyTo = null, string? fromName = null)
    {
        var message = new EmailTransportMessage(to, subject, ApplyBranding(html), replyTo, fromName);
        foreach (var transport in _transports)
        {
            if (!transport.IsConfigured)
            {
                _logger.LogDebug("Skipping unconfigured email provider {Provider}", transport.Name);
                continue;
            }

            try
            {
                if (await transport.SendAsync(message, cancellationToken)) return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Email provider {Provider} threw an unexpected error",
                    transport.Name);
            }

            _logger.LogWarning("Email provider {Provider} failed; trying the next configured provider",
                transport.Name);
        }

        _logger.LogError("All configured email providers failed to send email to {To}; subject: {Subject}",
            to, subject);
        return false;
    }

    private string ApplyBranding(string html)
    {
        html = EnsureColorSchemeMetadata(html);
        var appUrl = _configuration["Frontend:BaseUrl"]?.Trim().TrimEnd('/') is { Length: > 0 } configuredAppUrl
            ? configuredAppUrl
            : DefaultAppUrl;
        html = html.Replace("{{APP_URL}}", WebUtility.HtmlEncode(appUrl), StringComparison.Ordinal);
        var logoUrl = _configuration["Brand:LogoUrl"]?.Trim() is { Length: > 0 } configured
            ? configured
            : DefaultBrandLogoUrl;
        var safeLogoUrl = WebUtility.HtmlEncode(logoUrl);
        if (html.Contains("{{BRAND_LOGO_URL}}", StringComparison.Ordinal))
            html = html.Replace("{{BRAND_LOGO_URL}}", safeLogoUrl, StringComparison.Ordinal);

        // Self-branded templates already open with their own logo lockup — injecting this one on top of it
        // stacks two Mirage marks at the head of the message.
        var selfBranded = html.Contains(NewsletterEmailTemplate.SelfBrandedMarker, StringComparison.Ordinal);
        var bodyStart = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
        var bodyTagEnd = bodyStart < 0 ? -1 : html.IndexOf('>', bodyStart);
        if (bodyTagEnd >= 0 && !selfBranded && !html.Contains("email-wordmark", StringComparison.Ordinal))
        {
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
            html = html.Insert(bodyTagEnd + 1, header);
        }

        // Templates that ship their own footer opt out, otherwise a newsletter ends up with two of them stacked.
        if (selfBranded) return html;
        var bodyEnd = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return bodyEnd < 0 ? html : html.Insert(bodyEnd, BuildSocialFooter());
    }

    private static string EnsureColorSchemeMetadata(string html)
    {
        if (html.Contains("name=\"color-scheme\"", StringComparison.OrdinalIgnoreCase)) return html;

        var htmlTagStart = html.IndexOf("<html", StringComparison.OrdinalIgnoreCase);
        var htmlTagEnd = htmlTagStart < 0 ? -1 : html.IndexOf('>', htmlTagStart);
        if (htmlTagEnd < 0) return html;
        const string head = """
            <head>
              <meta name="viewport" content="width=device-width,initial-scale=1"/>
              <meta name="color-scheme" content="light dark"/>
              <meta name="supported-color-schemes" content="light dark"/>
              <style>:root{color-scheme:light dark;supported-color-schemes:light dark}</style>
            </head>
            """;
        return html.Insert(htmlTagEnd + 1, head);
    }

    /// <summary>The footer every other Mirage email carries. Same shape as the newsletter's — logo lockup,
    /// round badges, tagline — in the dark palette those templates use, so the whole estate reads as one sender.
    /// Badges are lettered rather than icon images because most clients block remote images by default.</summary>
    private string BuildSocialFooter()
    {
        var networks = new (string Label, string Glyph, string? Url)[]
        {
            ("Instagram", "IG", _configuration["SocialMedia:Instagram"]),
            ("Facebook", "f", _configuration["SocialMedia:Facebook"]),
            ("X", "X", _configuration["SocialMedia:X"]),
            ("LinkedIn", "in", _configuration["SocialMedia:LinkedIn"])
        };
        var links = networks
            .Where(x => Uri.TryCreate(x.Url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
            .Select(x => (x.Label, x.Glyph, Url: x.Url!))
            .ToList();

        var support = _configuration["SocialMedia:Email"]?.Trim() is { Length: > 0 } configuredSupport
            ? configuredSupport
            : "support@themiragehub.com";
        links.Add(("Email", "@", $"mailto:{support}"));

        var logoUrl = _configuration["Brand:LogoUrl"]?.Trim() is { Length: > 0 } configuredLogo
            ? configuredLogo
            : DefaultBrandLogoUrl;

        var badges = string.Concat(links.Select(x => $"""
            <td style="padding:0 5px;">
              <a href="{WebUtility.HtmlEncode(x.Url)}" title="{WebUtility.HtmlEncode(x.Label)}" style="text-decoration:none;">
                <table role="presentation" cellpadding="0" cellspacing="0" border="0" style="width:34px;height:34px;background:#1E1930;border:1px solid #38304F;border-radius:17px;">
                  <tr><td align="center" valign="middle" height="34" style="height:34px;text-align:center;font-family:-apple-system,'Helvetica Neue',Helvetica,Arial,sans-serif;font-size:12px;color:#C6B6FF;">{x.Glyph}</td></tr>
                </table>
              </a>
            </td>
            """));

        return $"""
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#0F0C16;">
              <tr><td align="center" style="padding:28px 12px 34px;">
                <table role="presentation" cellpadding="0" cellspacing="0" border="0" align="center" style="margin:0 auto 14px;"><tr>
                  <td valign="middle" style="line-height:0;padding-right:9px;">
                    <img src="{WebUtility.HtmlEncode(logoUrl)}" alt="Mirage" height="24" style="display:block;height:24px;width:auto;border:0;outline:none;text-decoration:none;" />
                  </td>
                  <td valign="middle" style="font:700 13px/1 Georgia,serif;color:#FFFFFF;letter-spacing:.2em;">MIRAGE</td>
                </tr></table>
                <table role="presentation" cellpadding="0" cellspacing="0" border="0" align="center" style="margin:0 auto;"><tr>
                  {badges}
                </tr></table>
                <p style="margin:14px 0 0;font-family:Georgia,serif;font-style:italic;font-size:12px;line-height:1.7;color:#AAA1BC;">A faith-integrated home for relationships worth building.</p>
              </td></tr>
            </table>
            """;
    }
}
