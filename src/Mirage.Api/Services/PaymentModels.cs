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

public sealed record BankOption(string Code, string Name);

public sealed record ResolvedBankAccount(string AccountName);

public sealed record PayoutSubmissionResult(string? ProviderTransferId, bool Completed);

// A refund the provider has accepted. Accepted is not the same as settled: the money reaches the
// payer's bank over the following days, so ProviderReference is what support quotes when a member
// asks where it is. FailureMessage carries the provider's own wording when Accepted is false.
public sealed record RefundResult(bool Accepted, string? ProviderReference, string? FailureMessage);
