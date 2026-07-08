using Mirage.Domain.Enums;

namespace Mirage.Infrastructure.Email;

// Composes the .html files under Email/Templates (via TemplateEngine) into full email documents.
public static class EmailTemplates
{
    private const string DisplayNameToken = "DISPLAY_NAME";
    private const string Purple = "#6C4EF2";
    private const string Teal = "#25C2A0";
    private const string Amber = "#F59E0B";

    public static string Welcome(string displayName, string appUrl) =>
        TemplateEngine.RenderPage("welcome", $"Welcome to Mirage, {displayName} — your relationship journey starts now.",
            new Dictionary<string, string> { [DisplayNameToken] = displayName },
            ctaBlock: "").Replace("{{APP_URL}}", appUrl);

    public static string PasswordReset(string displayName, string resetUrl) =>
        TemplateEngine.RenderPage("password-reset", "Reset your Mirage password — this link expires in 24 hours.",
            new Dictionary<string, string>
            {
                [DisplayNameToken] = displayName,
                ["RESET_URL"] = resetUrl
            });

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

    // One dedicated, purpose-styled template per in-app NotificationType (icon + accent color vary
    // by type; headline/body stay dynamic since callers already compose type-specific copy).
    private static readonly Dictionary<NotificationType, (string Template, string Color)> TypeTemplates = new()
    {
        [NotificationType.NewLike] = ("new-like", Purple),
        [NotificationType.NewMatch] = ("new-match", Teal),
        [NotificationType.DateRequestAccepted] = ("date-request-accepted", Purple),
        [NotificationType.DateRequestSelected] = ("date-request-selected", Amber),
        [NotificationType.MentorRequestReceived] = ("mentor-request-received", Purple),
        [NotificationType.MentorRequestAccepted] = ("mentor-request-accepted", Teal),
        [NotificationType.MentorRequestDeclined] = ("mentor-request-declined", Amber),
        [NotificationType.SessionBooked] = ("session-booked", Purple),
        [NotificationType.SessionAccepted] = ("session-accepted", Teal),
        [NotificationType.SessionDeclined] = ("session-declined", Amber),
        [NotificationType.NewMessage] = ("new-message", Purple),
        [NotificationType.ChatRequestReceived] = ("chat-request-received", Purple),
        [NotificationType.ChatRequestApproved] = ("chat-request-approved", Teal),
        [NotificationType.OrganisationApproved] = ("organisation-approved", Teal),
        [NotificationType.OrganisationRejected] = ("organisation-rejected", Amber),
        [NotificationType.CounsellorApproved] = ("counsellor-approved", Teal),
        [NotificationType.MembershipApproved] = ("membership-approved", Teal),
        [NotificationType.MembershipRejected] = ("membership-rejected", Amber),
        [NotificationType.Mention] = ("mention", Purple),
        [NotificationType.CounsellorApplicationReceived] = ("counsellor-application-received", Purple),
        [NotificationType.GatheringInviteReceived] = ("gathering-invite-received", Purple),
        [NotificationType.GatheringInviteAccepted] = ("gathering-invite-accepted", Teal),
        [NotificationType.GatheringInviteDeclined] = ("gathering-invite-declined", Amber)
    };

    public static bool HasTemplate(NotificationType type) => TypeTemplates.ContainsKey(type);

    public static string Notification(NotificationType type, string displayName, string title, string body,
        string? actionUrl, string? actionLabel)
    {
        if (!TypeTemplates.TryGetValue(type, out var meta))
            throw new ArgumentOutOfRangeException(nameof(type), type, "No email template is registered for this notification type.");

        var cta = actionUrl is null ? "" : TemplateEngine.PrimaryButton(actionUrl, actionLabel ?? "View in Mirage", meta.Color);
        return TemplateEngine.RenderPage(meta.Template, body,
            new Dictionary<string, string>
            {
                [DisplayNameToken] = displayName,
                ["TITLE"] = title,
                ["BODY"] = body
            }, cta);
    }
}
