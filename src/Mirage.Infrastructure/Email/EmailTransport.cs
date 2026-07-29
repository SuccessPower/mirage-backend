namespace Mirage.Infrastructure.Email;

public sealed record EmailTransportMessage(
    string To,
    string Subject,
    string Html,
    string? ReplyTo = null);

public interface IEmailTransport
{
    string Name { get; }
    bool IsConfigured { get; }
    Task<bool> SendAsync(EmailTransportMessage message, CancellationToken cancellationToken);
}
