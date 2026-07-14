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

    public static string EmailConfirmation(string displayName, string confirmUrl) =>
        TemplateEngine.RenderPage("email-confirmation", "Confirm your email to start liking, matching, and chatting on Mirage.",
            new Dictionary<string, string>
            {
                [DisplayNameToken] = displayName,
                ["CONFIRM_URL"] = confirmUrl
            });

    public static string PasswordReset(string displayName, string resetUrl) =>
        TemplateEngine.RenderPage("password-reset", "Reset your Mirage password — this link expires in 24 hours.",
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
        [NotificationType.ProfileVerified] = ("Verification", Teal)
    };

    public static bool HasTemplate(NotificationType type) => TypeLabels.ContainsKey(type);

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

    private static string Tint(string color) => color switch
    {
        Purple => "rgba(108,78,242,0.14)",
        Teal => "rgba(37,194,160,0.14)",
        Amber => "rgba(245,158,11,0.14)",
        _ => "rgba(108,78,242,0.14)"
    };
}
