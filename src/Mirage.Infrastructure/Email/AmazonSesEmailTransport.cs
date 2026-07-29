using Amazon.Runtime;
using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Mirage.Infrastructure.Email;

public sealed class AmazonSesEmailTransport(
    IAmazonSimpleEmailServiceV2 ses,
    IConfiguration configuration,
    ILogger<AmazonSesEmailTransport> logger) : IEmailTransport
{
    public string Name => "AmazonSes";
    public bool IsConfigured => true; // Credentials are resolved by the AWS credential provider chain.

    public async Task<bool> SendAsync(EmailTransportMessage message, CancellationToken cancellationToken)
    {
        var request = new SendEmailRequest
        {
            FromEmailAddress = configuration["AmazonSes:From"] ?? "Mirage <noreply@themiragehub.com>",
            Destination = new Destination { ToAddresses = [message.To] },
            Content = new EmailContent
            {
                Simple = new Message
                {
                    Subject = new Content { Data = message.Subject, Charset = "UTF-8" },
                    Body = new Body { Html = new Content { Data = message.Html, Charset = "UTF-8" } }
                }
            },
            ReplyToAddresses = string.IsNullOrWhiteSpace(message.ReplyTo) ? [] : [message.ReplyTo]
        };

        try
        {
            var response = await ses.SendEmailAsync(request, cancellationToken);
            logger.LogInformation(
                "Email accepted by {Provider} for {To}; message ID: {MessageId}",
                Name, message.To, response.MessageId);
            return true;
        }
        catch (AmazonSimpleEmailServiceV2Exception ex)
        {
            logger.LogWarning(ex, "{Provider} rejected email to {To}; error: {ErrorCode}",
                Name, message.To, ex.ErrorCode);
            return false;
        }
        catch (AmazonClientException ex)
        {
            logger.LogWarning(ex, "{Provider} failed to send email to {To}", Name, message.To);
            return false;
        }
    }
}
