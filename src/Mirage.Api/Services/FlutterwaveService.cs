using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;

namespace Mirage.Api.Services;

// Talks to Flutterwave's REST API. Unlike Paystack, Flutterwave's standard "Payments" endpoint
// returns a single hosted checkout link regardless of method — for bank transfer, Flutterwave's
// own hosted page shows the dynamic virtual account details, so both Card and BankTransfer
// resolve to an AuthorizationUrl here rather than raw account fields.
// NOTE: verify field names against Flutterwave's current API docs/sandbox before going live.
public sealed class FlutterwaveService(HttpClient http, IConfiguration configuration)
{
    private const string BaseUrl = "https://api.flutterwave.com/v3";

    private string SecretKey =>
        configuration["Flutterwave:SecretKey"] ?? throw new InvalidOperationException("Flutterwave:SecretKey is not configured.");

    public async Task<PaymentCheckoutResult> InitializeAsync(Payment payment, string payerEmail, PaymentMethod method,
        string redirectUrl, CancellationToken cancellationToken)
    {
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SecretKey);
        var response = await http.PostAsJsonAsync($"{BaseUrl}/payments", new
        {
            tx_ref = payment.ProviderReference,
            amount = payment.Amount,
            currency = payment.Currency,
            redirect_url = redirectUrl,
            customer = new { email = payerEmail },
            payment_options = method == PaymentMethod.BankTransfer ? "banktransfer" : "card",
        }, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
        var link = body.GetProperty("data").GetProperty("link").GetString();
        return new PaymentCheckoutResult(link, null, null, null, null);
    }

    public bool VerifySignature(string? signatureHeader)
    {
        var expected = configuration["Flutterwave:WebhookSecretHash"];
        return !string.IsNullOrWhiteSpace(expected) && !string.IsNullOrWhiteSpace(signatureHeader)
            && expected == signatureHeader;
    }

    public PaymentWebhookResult ParseWebhook(string rawBody)
    {
        using var doc = JsonDocument.Parse(rawBody);
        var root = doc.RootElement;
        var eventName = root.TryGetProperty("event", out var e) ? e.GetString() : null;
        if (!root.TryGetProperty("data", out var data))
            return new PaymentWebhookResult(null, false, null);

        var reference = data.TryGetProperty("tx_ref", out var r) ? r.GetString() : null;
        var status = data.TryGetProperty("status", out var s) ? s.GetString() : null;
        var successful = eventName == "charge.completed" && status == "successful";
        var txId = data.TryGetProperty("id", out var id) ? id.ToString() : reference;
        return new PaymentWebhookResult(reference, successful, txId);
    }
}
