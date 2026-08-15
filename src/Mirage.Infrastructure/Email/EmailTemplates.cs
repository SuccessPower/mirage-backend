using Mirage.Domain.Enums;
using System.Net;

namespace Mirage.Infrastructure.Email;

// Composes the .html files under Email/Templates (via TemplateEngine) into full email documents.
public static class EmailTemplates
{
    private const string DisplayNameToken = "DISPLAY_NAME";
    private const string Purple = "#6C4EF2";
    private const string Teal = "#25C2A0";
    private const string Amber = "#F59E0B";

    // confirmUrl is supplied when this welcome email also serves as the account's confirmation
    // email (see AuthEndpoints.Register) — it renders a "Confirm your email" button up top so the
    // one message covers both jobs instead of the user waiting on/receiving a second email.
    public static string Welcome(string displayName, string appUrl, string? confirmUrl = null)
    {
        var cta = confirmUrl is null ? "" : TemplateEngine.PrimaryButton(confirmUrl, "Confirm your email");
        var preheader = confirmUrl is null
            ? $"Welcome to Mirage, {displayName}. Your relationship journey starts now."
            : $"Welcome to Mirage, {displayName}. Confirm your email to start liking, matching, and chatting.";
        return TemplateEngine.RenderPage("welcome", preheader,
            new Dictionary<string, string> { [DisplayNameToken] = displayName },
            ctaBlock: cta).Replace("{{APP_URL}}", appUrl);
    }

    public static string EmailConfirmation(string displayName, string confirmUrl) =>
        TemplateEngine.RenderPage("email-confirmation", "Confirm your email to start liking, matching, and chatting on Mirage.",
            new Dictionary<string, string>
            {
                [DisplayNameToken] = displayName,
                ["CONFIRM_URL"] = confirmUrl
            });

    public static string PasswordReset(string displayName, string resetUrl) =>
        TemplateEngine.RenderPage("password-reset", "Reset your Mirage password. This link expires in 24 hours.",
            new Dictionary<string, string>
            {
                [DisplayNameToken] = displayName,
                ["RESET_URL"] = resetUrl
            });

    public static string AccountClosed(string displayName, bool permanent)
    {
        var title = permanent ? "Your account has been deleted" : "Your account has been deactivated";
        var body = permanent
            ? "your Mirage account and profile details have been deleted. You will no longer be able to sign in."
            : "your Mirage account has been deactivated and you will no longer be able to sign in. Contact support if you'd like it reactivated.";
        return TemplateEngine.RenderPage("account-closed", title,
            new Dictionary<string, string>
            {
                [DisplayNameToken] = displayName,
                ["TITLE"] = title,
                ["BODY"] = body
            });
    }

    public static string PasswordChanged(string displayName) =>
        TemplateEngine.RenderPage("password-changed", "Your Mirage password was changed successfully.",
            new Dictionary<string, string>
            {
                [DisplayNameToken] = displayName,
                ["DATE"] = DateTime.UtcNow.ToString("MMMM d, yyyy"),
                ["TIME"] = DateTime.UtcNow.ToString("HH:mm")
            });

    public static string PaymentConfirmed(string displayName, string description, decimal amount, string currency) =>
        TemplateEngine.RenderPage("payment-confirmed", $"Your payment of {currency} {amount:N2} for {description} was confirmed.",
            new Dictionary<string, string>
            {
                [DisplayNameToken] = displayName,
                ["DESCRIPTION"] = description,
                ["AMOUNT"] = amount.ToString("N2"),
                ["CURRENCY"] = currency
            });

    // All in-app NotificationTypes render through the single "notification" template now —
    // they only ever differed by an eyebrow label and an accent color, so one shared file
    // (see Templates/notification.html) replaces what used to be ~23 near-duplicate files.
    private static readonly Dictionary<NotificationType, (string Label, string Color)> TypeLabels = new()
    {
        [NotificationType.NewLike] = ("New like", Purple),
        [NotificationType.NewMatch] = ("New match", Teal),
        [NotificationType.DateRequestAccepted] = ("Date request", Purple),
        [NotificationType.DateRequestSelected] = ("You've been selected", Amber),
        [NotificationType.MentorRequestReceived] = ("Mentorship", Purple),
        [NotificationType.MentorRequestAccepted] = ("Mentorship", Teal),
        [NotificationType.MentorRequestDeclined] = ("Mentorship", Amber),
        [NotificationType.SessionBooked] = ("Counselling session", Purple),
        [NotificationType.SessionAccepted] = ("Counselling session", Teal),
        [NotificationType.SessionDeclined] = ("Counselling session", Amber),
        [NotificationType.NewMessage] = ("New message", Purple),
        [NotificationType.ChatRequestReceived] = ("Chat request", Purple),
        [NotificationType.ChatRequestApproved] = ("Chat request", Teal),
        [NotificationType.OrganisationApproved] = ("Organisation", Teal),
        [NotificationType.OrganisationRejected] = ("Organisation", Amber),
        [NotificationType.CounsellorApproved] = ("Counsellor", Teal),
        [NotificationType.MembershipApproved] = ("Membership", Teal),
        [NotificationType.MembershipRejected] = ("Membership", Amber),
        [NotificationType.Mention] = ("Mention", Purple),
        [NotificationType.CounsellorApplicationReceived] = ("Counsellor application", Purple),
        [NotificationType.GatheringInviteReceived] = ("Gathering invite", Purple),
        [NotificationType.GatheringInviteAccepted] = ("Gathering invite", Teal),
        [NotificationType.GatheringInviteDeclined] = ("Gathering invite", Amber),
        [NotificationType.ProfileVerified] = ("Verification", Teal),
        [NotificationType.DateOfBirthInvalid] = ("Profile", Amber),
        [NotificationType.ProfilePhotosRequired] = ("Profile photos required", Amber),
        [NotificationType.ProfilePhotosComplete] = ("Profile photos approved", Teal)
    };

    public static bool HasTemplate(NotificationType type) => TypeLabels.ContainsKey(type);

    public static string ProfileVisit(string displayName, string visitorName, string? visitorAvatarUrl,
        bool revealIdentity, string profileUrl)
    {
        var safeName = WebUtility.HtmlEncode(displayName);
        var safeVisitor = WebUtility.HtmlEncode(revealIdentity ? visitorName : "Someone");
        var image = string.IsNullOrWhiteSpace(visitorAvatarUrl)
            ? $"<div style=\"width:96px;height:96px;border-radius:24px;background:#241d3d;color:#8b70ff;display:flex;align-items:center;justify-content:center;font-size:34px;font-weight:800\">{safeVisitor[..1]}</div>"
            : $"<img src=\"{WebUtility.HtmlEncode(visitorAvatarUrl)}\" alt=\"Profile visitor\" width=\"96\" height=\"96\" style=\"width:96px;height:96px;border-radius:24px;object-fit:cover;{(revealIdentity ? "" : "filter:blur(12px);transform:scale(1.12);")}\" />";
        var body = revealIdentity
            ? $"<strong>{safeVisitor}</strong> visited your profile. This visitor is included in your first 10 free identity reveals."
            : "Someone visited your profile. You have used your 10 free visitor identity reveals, so their photo and identity are hidden.";

        return $"""
            <!doctype html><html><body style="margin:0;background:#0f0c16;color:#f4f0ff;font-family:Arial,sans-serif">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr><td align="center" style="padding:32px 16px">
                <table role="presentation" width="560" cellpadding="0" cellspacing="0" style="max-width:560px;background:#191525;border:1px solid #332b47;border-radius:24px">
                  <tr><td style="padding:32px">
                    <div style="color:#8b70ff;font-size:13px;font-weight:800;letter-spacing:.12em;text-transform:uppercase">Profile activity</div>
                    <h1 style="font-size:26px;margin:12px 0 8px">Hi {safeName}, you caught someone’s eye</h1>
                    <p style="color:#beb5cf;line-height:1.6;margin:0 0 24px">{body}</p>
                    <div style="overflow:hidden;width:96px;height:96px;border-radius:24px;margin-bottom:24px">{image}</div>
                    {TemplateEngine.PrimaryButton(profileUrl, revealIdentity ? "View their profile" : "Open Mirage")}
                  </td></tr>
                </table>
              </td></tr></table>
            </body></html>
            """;
    }

    public static string ContactSubmission(string senderName, string senderEmail, string country,
        string reason, string message)
    {
        var safeName = WebUtility.HtmlEncode(senderName);
        var safeEmail = WebUtility.HtmlEncode(senderEmail);
        var safeCountry = WebUtility.HtmlEncode(country);
        var safeReason = WebUtility.HtmlEncode(reason);
        var safeMessage = WebUtility.HtmlEncode(message).Replace("\r\n", "<br>").Replace("\n", "<br>");

        return $"""
            <!doctype html><html><body style="margin:0;background:#0f0c16;color:#f4f0ff;font-family:Arial,sans-serif">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr><td align="center" style="padding:32px 16px">
                <table role="presentation" width="620" cellpadding="0" cellspacing="0" style="max-width:620px;background:#191525;border:1px solid #332b47;border-radius:24px">
                  <tr><td style="padding:32px">
                    <div style="color:#8b70ff;font-size:13px;font-weight:800;letter-spacing:.12em;text-transform:uppercase">Contact form</div>
                    <h1 style="font-size:26px;margin:12px 0 24px">New {safeReason} enquiry</h1>
                    <table role="presentation" width="100%" cellpadding="8" cellspacing="0" style="color:#beb5cf">
                      <tr><td style="width:110px">From</td><td style="color:#fff"><strong>{safeName}</strong></td></tr>
                      <tr><td>Email</td><td style="color:#fff">{safeEmail}</td></tr>
                      <tr><td>Country</td><td style="color:#fff">{safeCountry}</td></tr>
                    </table>
                    <div style="margin-top:24px;padding:20px;background:#120f1b;border-radius:16px;color:#eee8fa;line-height:1.65">{safeMessage}</div>
                    <p style="color:#827991;font-size:12px;margin:20px 0 0">Reply to this email to respond directly to {safeName}.</p>
                  </td></tr>
                </table>
              </td></tr></table>
            </body></html>
            """;
    }

    public static string AdminInformationRequest(string displayName, string message, string profileUrl)
    {
        var safeName = WebUtility.HtmlEncode(displayName);
        var safeMessage = WebUtility.HtmlEncode(message).Replace("\r\n", "<br>").Replace("\n", "<br>");
        return $"""
            <!doctype html><html><body style="margin:0;background:#0f0c16;color:#f4f0ff;font-family:Arial,sans-serif">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr><td align="center" style="padding:32px 16px">
                <table role="presentation" width="600" cellpadding="0" cellspacing="0" style="max-width:600px;background:#191525;border:1px solid #332b47;border-radius:24px">
                  <tr><td style="padding:32px">
                    <div style="color:#8b70ff;font-size:13px;font-weight:800;letter-spacing:.12em;text-transform:uppercase">Profile information request</div>
                    <h1 style="font-size:26px;margin:12px 0 10px">Hi {safeName}, we need a little more information</h1>
                    <p style="color:#beb5cf;line-height:1.6">A Mirage administrator reviewed your account and left this request:</p>
                    <div style="margin:22px 0;padding:20px;background:#120f1b;border-left:3px solid #8b70ff;border-radius:12px;color:#eee8fa;line-height:1.65">{safeMessage}</div>
                    {TemplateEngine.PrimaryButton(profileUrl, "Complete your profile")}
                    <p style="color:#827991;font-size:12px;margin:20px 0 0">If you believe this was sent in error, contact Mirage support through the Contact page.</p>
                  </td></tr>
                </table>
              </td></tr></table>
            </body></html>
            """;
    }

    private static readonly Dictionary<CelebrationType, (string Emoji, string Label, string Color)> CelebrationMeta = new()
    {
        [CelebrationType.Birthday] = ("🎉", "Happy Birthday", Purple),
        [CelebrationType.Anniversary] = ("💍", "Happy Anniversary", Amber)
    };

    public static string Celebration(CelebrationType type, string displayName, string storyUrl)
    {
        var meta = CelebrationMeta[type];
        var body = type == CelebrationType.Birthday
            ? "wishing you a very happy birthday! May the year ahead be full of joy, growth, and beautiful moments."
            : "wishing you a very happy anniversary! Here's to many more years of love and partnership.";
        var title = $"{meta.Emoji} {meta.Label}, {displayName}!";
        var preheader = $"{meta.Label}, {displayName}! The whole Mirage team is thinking of you today.";
        var cta = TemplateEngine.PrimaryButton(storyUrl, "View your celebration", meta.Color);

        return TemplateEngine.RenderPage("celebration", preheader,
            new Dictionary<string, string>
            {
                [DisplayNameToken] = displayName,
                ["EMOJI"] = meta.Emoji,
                ["TITLE"] = title,
                ["BODY"] = body,
                ["COLOR"] = meta.Color,
                ["COLOR_TINT"] = Tint(meta.Color)
            }, cta);
    }

    public static string Notification(NotificationType type, string displayName, string title, string body,
        string? actionUrl, string? actionLabel)
    {
        if (!TypeLabels.TryGetValue(type, out var meta))
            throw new ArgumentOutOfRangeException(nameof(type), type, "No email template is registered for this notification type.");

        var cta = actionUrl is null ? "" : TemplateEngine.PrimaryButton(actionUrl, actionLabel ?? "View in Mirage", meta.Color);
        return TemplateEngine.RenderPage("notification", body,
            new Dictionary<string, string>
            {
                [DisplayNameToken] = displayName,
                ["TITLE"] = title,
                ["BODY"] = body,
                ["LABEL"] = meta.Label,
                ["COLOR"] = meta.Color,
                ["COLOR_TINT"] = Tint(meta.Color)
            }, cta);
    }

    /// <summary>
    /// The "we've missed you" email. <paramref name="highlights"/> is the tour of the app we want
    /// them to take — each entry becomes a linked row. Married members get the love-story prompt,
    /// which is why the caller composes the list rather than this method hardcoding it.
    /// </summary>
    public static string ReEngagement(string displayName, string title, string intro, string appUrl,
        IReadOnlyList<(string Heading, string Blurb, string Url)> highlights)
    {
        var rows = string.Concat(highlights.Select(HighlightRow));
        var body = rows + TemplateEngine.PrimaryButton(appUrl, "Open Mirage", Teal);
        return TemplateEngine.RenderPage("re-engagement", intro,
            new Dictionary<string, string>
            {
                [DisplayNameToken] = displayName,
                ["TITLE"] = title,
                ["INTRO"] = intro
            }, body);
    }

    // Inline styles only, and tables rather than flex — Outlook and Gmail still drop most of
    // modern CSS, and these rows have to survive both.
    private static string HighlightRow((string Heading, string Blurb, string Url) item) => $"""
        <tr><td style="padding:0 48px 12px;">
          <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#F8F7FC;border:1px solid #EDEAF5;border-radius:14px;">
            <tr><td style="padding:16px 18px;">
              <a href="{WebUtility.HtmlEncode(item.Url)}" style="text-decoration:none;">
                <span style="display:block;font-family:-apple-system,'Helvetica Neue',Helvetica,Arial,sans-serif;font-size:15px;font-weight:700;color:#1B1730;letter-spacing:-0.2px;margin-bottom:4px;">{WebUtility.HtmlEncode(item.Heading)}</span>
                <span style="display:block;font-family:-apple-system,'Helvetica Neue',Helvetica,Arial,sans-serif;font-size:13.5px;color:#6B6480;line-height:1.6;">{WebUtility.HtmlEncode(item.Blurb)}</span>
              </a>
            </td></tr>
          </table>
        </td></tr>
        """;

    private static string Tint(string color) => color switch
    {
        Purple => "rgba(108,78,242,0.14)",
        Teal => "rgba(37,194,160,0.14)",
        Amber => "rgba(245,158,11,0.14)",
        _ => "rgba(108,78,242,0.14)"
    };
}
