using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;

namespace Mirage.Api.Services;

// Talks to Paystack's REST API. Card payments use the standard hosted-checkout
// "Initialize Transaction" endpoint; bank transfers use Paystack's Charge API with a
// bank_transfer channel, which allocates a one-time virtual account for the exact amount and
// auto-expires — this is Paystack's "Pay with Transfer" feature.
// NOTE: verify these field names against Paystack's current API docs/sandbox before going live;
// they're implemented from the documented shape but haven't been exercised against a live sandbox.
public sealed class PaystackService(HttpClient http, IConfiguration configuration)
{
    private const string BaseUrl = "https://api.paystack.co";

    private string SecretKey =>
        configuration["Paystack:SecretKey"] ?? throw new InvalidOperationException("Paystack:SecretKey is not configured.");

    // subaccountCode, when present, auto-splits the transaction at Paystack's end: the
    // subaccount's configured percentage_charge portion (our platform commission) stays with
    // the main account, the rest settles directly to the counsellor's own bank account.
    // Payments for a counsellor with no subaccount yet fall back to 100% landing in the
    // platform's own account (see PaymentEndpoints.Initialize).
    public async Task<PaymentCheckoutResult> InitializeAsync(Payment payment, string payerEmail, PaymentMethod method,
        string? subaccountCode, string callbackUrl, CancellationToken cancellationToken)
    {
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SecretKey);
        var amountInMinorUnits = (long)Math.Round(payment.Amount * 100);

        if (method == PaymentMethod.Card)
        {
            var response = await http.PostAsJsonAsync($"{BaseUrl}/transaction/initialize", new
            {
                email = payerEmail,
                amount = amountInMinorUnits,
                currency = payment.Currency,
                reference = payment.ProviderReference,
                subaccount = subaccountCode,
                callback_url = callbackUrl,
            }, cancellationToken);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
            var url = body.GetProperty("data").GetProperty("authorization_url").GetString();
            return new PaymentCheckoutResult(url, null, null, null, null);
        }

        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(30);
        var chargeResponse = await http.PostAsJsonAsync($"{BaseUrl}/charge", new
        {
            email = payerEmail,
            amount = amountInMinorUnits,
            currency = payment.Currency,
            reference = payment.ProviderReference,
            subaccount = subaccountCode,
            bank_transfer = new { account_expires_at = expiresAt },
        }, cancellationToken);
        chargeResponse.EnsureSuccessStatusCode();
        var chargeBody = await chargeResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        var data = chargeBody.GetProperty("data");
        var auth = data.TryGetProperty("authorization", out var authProp) ? authProp : default;
        string? Get(string name) =>
            auth.ValueKind != JsonValueKind.Undefined && auth.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        return new PaymentCheckoutResult(null, Get("account_number"), Get("bank"), Get("account_name"), expiresAt);
    }

    // Paystack refunds are asked for against the transaction reference we sent at checkout, and
    // settle back to the payer's original method over the following days. The API answers
    // immediately with a pending refund record; there is nothing to poll for our purposes, since
    // the session is already cancelled and the member has been told the money is on its way.
    public async Task<RefundResult> RefundAsync(Payment payment, decimal amount, string? note,
        CancellationToken cancellationToken)
    {
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SecretKey);
        var response = await http.PostAsJsonAsync($"{BaseUrl}/refund", new
        {
            transaction = payment.ProviderReference,
            amount = (long)Math.Round(amount * 100),
            currency = payment.Currency,
            merchant_note = note,
        }, cancellationToken);

        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) return new RefundResult(false, null, ProviderMessage(raw));

        using var doc = JsonDocument.Parse(raw);
        var data = doc.RootElement.TryGetProperty("data", out var d) ? d : default;
        var reference = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("id", out var id)
            ? id.ToString()
            : null;
        return new RefundResult(true, reference, null);
    }

    private static string? ProviderMessage(string rawBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            return doc.RootElement.TryGetProperty("message", out var message) ? message.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<BankOption>> ListBanksAsync(CancellationToken cancellationToken)
    {
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SecretKey);
        var response = await http.GetAsync($"{BaseUrl}/bank?country=nigeria&currency=NGN", cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        return body.GetProperty("data").EnumerateArray()
            .Select(b => new BankOption(b.GetProperty("code").GetString() ?? "", b.GetProperty("name").GetString() ?? ""))
            .Where(b => b.Code.Length > 0)
            .ToList();
    }

    public async Task<ResolvedBankAccount?> ResolveAccountAsync(string bankCode, string accountNumber,
        CancellationToken cancellationToken)
    {
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SecretKey);
        var response = await http.GetAsync(
            $"{BaseUrl}/bank/resolve?account_number={Uri.EscapeDataString(accountNumber)}&bank_code={Uri.EscapeDataString(bankCode)}",
            cancellationToken);
        if (!response.IsSuccessStatusCode) return null;
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        var name = body.GetProperty("data").GetProperty("account_name").GetString();
        return name is null ? null : new ResolvedBankAccount(name);
    }

    // percentage_charge is the share Paystack routes to the MAIN (platform) account on every
    // split transaction against this subaccount — the remainder settles to the counsellor.
    public async Task<string> CreateSubaccountAsync(string businessName, string bankCode, string accountNumber,
        decimal platformCommissionPercentage, CancellationToken cancellationToken)
    {
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SecretKey);
        var response = await http.PostAsJsonAsync($"{BaseUrl}/subaccount", new
        {
            business_name = businessName,
            settlement_bank = bankCode,
            account_number = accountNumber,
            percentage_charge = platformCommissionPercentage,
        }, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        return body.GetProperty("data").GetProperty("subaccount_code").GetString()
            ?? throw new InvalidOperationException("Paystack did not return a subaccount code.");
    }

    public async Task<string> CreateTransferRecipientAsync(string name, string bankCode, string accountNumber,
        string currency, CancellationToken cancellationToken)
    {
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SecretKey);
        var response = await http.PostAsJsonAsync($"{BaseUrl}/transferrecipient", new
        {
            type = currency.Equals("NGN", StringComparison.OrdinalIgnoreCase) ? "nuban" : "basa",
            name,
            bank_code = bankCode,
            account_number = accountNumber,
            currency,
        }, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        return body.GetProperty("data").GetProperty("recipient_code").GetString()
            ?? throw new InvalidOperationException("Paystack did not return a transfer recipient code.");
    }

    public async Task<PayoutSubmissionResult> InitiateTransferAsync(Payment payment, string recipientCode,
        CancellationToken cancellationToken)
    {
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SecretKey);
        var response = await http.PostAsJsonAsync($"{BaseUrl}/transfer", new
        {
            source = "balance",
            amount = (long)Math.Round(payment.CounsellorAmount * 100),
            recipient = recipientCode,
            reference = payment.PayoutReference,
            reason = $"Mirage counselling session {payment.CounsellingSessionId:N}",
            currency = payment.Currency,
        }, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        var data = body.GetProperty("data");
        var status = data.GetProperty("status").GetString();
        var transferCode = data.TryGetProperty("transfer_code", out var code) ? code.GetString() : null;
        return new PayoutSubmissionResult(transferCode, status == "success");
    }

    public PaymentWebhookResult ParseTransferWebhook(string rawBody)
    {
        using var doc = JsonDocument.Parse(rawBody);
        var root = doc.RootElement;
        var eventName = root.TryGetProperty("event", out var e) ? e.GetString() : null;
        if (!root.TryGetProperty("data", out var data)) return new PaymentWebhookResult(null, false, null);
        var reference = data.TryGetProperty("reference", out var r) ? r.GetString() : null;
        var transferCode = data.TryGetProperty("transfer_code", out var code) ? code.GetString() : null;
        return new PaymentWebhookResult(reference, eventName == "transfer.success", transferCode);
    }

    public bool VerifySignature(string rawBody, string? signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader)) return false;
        var computed = Convert.ToHexString(HMACSHA512.HashData(Encoding.UTF8.GetBytes(SecretKey), Encoding.UTF8.GetBytes(rawBody)))
            .ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(computed),
            Encoding.UTF8.GetBytes(signatureHeader.ToLowerInvariant()));
    }

    public PaymentWebhookResult ParseWebhook(string rawBody)
    {
        using var doc = JsonDocument.Parse(rawBody);
        var root = doc.RootElement;
        var eventName = root.TryGetProperty("event", out var e) ? e.GetString() : null;
        if (!root.TryGetProperty("data", out var data))
            return new PaymentWebhookResult(null, false, null);

        var reference = data.TryGetProperty("reference", out var r) ? r.GetString() : null;
        var status = data.TryGetProperty("status", out var s) ? s.GetString() : null;
        var successful = eventName == "charge.success" && status == "success";
        var txId = data.TryGetProperty("id", out var id) ? id.ToString() : reference;
        return new PaymentWebhookResult(reference, successful, txId);
    }
}
