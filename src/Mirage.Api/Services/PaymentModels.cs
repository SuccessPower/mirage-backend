namespace Mirage.Api.Services;

// Common shape both provider services return from InitializeAsync, regardless of the
// underlying provider's response format — card payments populate AuthorizationUrl (a hosted
// checkout page to redirect to), bank transfer payments populate the virtual account details.
public sealed record PaymentCheckoutResult(
    string? AuthorizationUrl,
    string? AccountNumber,
    string? BankName,
    string? AccountName,
    DateTimeOffset? ExpiresAt);

public sealed record PaymentWebhookResult(string? ProviderReference, bool Successful, string? ProviderTransactionId);
