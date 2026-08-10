using System.Security.Authentication;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;

namespace Mirage.Infrastructure.Email;

public sealed class ZeptoMailEmailTransport(
    IConfiguration configuration,
    ILogger<ZeptoMailEmailTransport> logger) : IEmailTransport
{
    public string Name => "ZeptoMail";
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(configuration["ZeptoMail:Password"]);

    public async Task<bool> SendAsync(EmailTransportMessage email, CancellationToken cancellationToken)
    {
        if (!IsConfigured) return false;

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(EmailSender.WithDisplayName(
            configuration["ZeptoMail:From"] ?? "Mirage <noreply@themiragehub.com>", email.FromName)));
        message.To.Add(MailboxAddress.Parse(email.To));
        message.Subject = email.Subject;
        message.Body = new TextPart("html") { Text = email.Html };
        if (!string.IsNullOrWhiteSpace(email.ReplyTo))
            message.ReplyTo.Add(MailboxAddress.Parse(email.ReplyTo));

        try
        {
            using var client = new SmtpClient
            {
                SslProtocols = SslProtocols.Tls12,
                Timeout = configuration.GetValue("ZeptoMail:TimeoutMilliseconds", 15_000)
            };
            await client.ConnectAsync(
                configuration["ZeptoMail:Host"] ?? "smtp.zeptomail.com",
                configuration.GetValue("ZeptoMail:Port", 587),
                SecureSocketOptions.StartTls,
                cancellationToken);
            await client.AuthenticateAsync(
                configuration["ZeptoMail:Username"] ?? "emailapikey",
                configuration["ZeptoMail:Password"]!,
                cancellationToken);
            var response = await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
            logger.LogInformation("Email accepted by {Provider} for {To}; response: {Response}",
                Name, email.To, response);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "{Provider} failed to send email to {To}", Name, email.To);
            return false;
        }
    }
}
