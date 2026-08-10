using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Mirage.Infrastructure.Email;

public sealed class MailjetEmailTransport(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<MailjetEmailTransport> logger) : IEmailTransport
{
    public string Name => "Mailjet";
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(configuration["Mailjet:ApiKey"]) &&
        !string.IsNullOrWhiteSpace(configuration["Mailjet:SecretKey"]);

    public async Task<bool> SendAsync(EmailTransportMessage email, CancellationToken cancellationToken)
    {
        if (!IsConfigured) return false;

        using var request = new HttpRequestMessage(HttpMethod.Post, "v3.1/send");
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $"{configuration["Mailjet:ApiKey"]}:{configuration["Mailjet:SecretKey"]}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Content = JsonContent.Create(new
        {
            Messages = new[]
            {
                new
                {
                    From = ParseAddress(EmailSender.WithDisplayName(
                        configuration["Mailjet:From"] ?? "Mirage <noreply@themiragehub.com>", email.FromName)),
                    To = new[] { ParseAddress(email.To) },
                    ReplyTo = string.IsNullOrWhiteSpace(email.ReplyTo) ? null : ParseAddress(email.ReplyTo),
                    email.Subject,
                    HTMLPart = email.Html
                }
            }
        });

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("Email accepted by {Provider} for {To}", Name, email.To);
                return true;
            }

            logger.LogWarning("{Provider} rejected email to {To}; status: {StatusCode}",
                Name, email.To, (int)response.StatusCode);
            return false;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "{Provider} failed to send email to {To}", Name, email.To);
            return false;
        }
    }

    private static object ParseAddress(string value)
    {
        var start = value.LastIndexOf('<');
        var end = value.LastIndexOf('>');
        return start >= 0 && end > start
            ? new { Email = value[(start + 1)..end].Trim(), Name = value[..start].Trim() }
            : new { Email = value.Trim(), Name = string.Empty };
    }
}
