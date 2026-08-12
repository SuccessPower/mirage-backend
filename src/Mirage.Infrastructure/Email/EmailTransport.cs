using System.Net.Mail;

namespace Mirage.Infrastructure.Email;

public sealed record EmailTransportMessage(
    string To,
    string Subject,
    string Html,
    string? ReplyTo = null,
    /// <summary>Overrides only the display name on the From header — "Ada from The Mirage Journal" — while the address
    /// itself stays the configured, domain-verified mailbox. Changing the address would break DKIM/SPF alignment
    /// and land the send in spam.</summary>
    string? FromName = null);

public static class EmailSender
{
    /// <summary>Rebuilds a configured "Name &lt;address&gt;" sender with a different display name.</summary>
    public static string WithDisplayName(string configuredFrom, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return configuredFrom;
        try
        {
            var address = new MailAddress(configuredFrom).Address;
            return new MailAddress(address, displayName.Trim()).ToString();
        }
        catch (FormatException)
        {
            return configuredFrom;
        }
    }
}

public interface IEmailTransport
{
    string Name { get; }
    bool IsConfigured { get; }
    Task<bool> SendAsync(EmailTransportMessage message, CancellationToken cancellationToken);
}
