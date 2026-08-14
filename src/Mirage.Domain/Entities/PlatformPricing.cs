using Mirage.Domain.Common;

namespace Mirage.Domain.Entities;

/// <summary>
/// The bounds counsellor session fees must fall inside. Counsellors price themselves against
/// their own market, so Mirage does not pick a number for them — it only draws a floor and,
/// optionally, a ceiling, and both are admin's to move as the market does.
///
/// Both bounds are optional on purpose: with neither set, a counsellor prices freely. A fixed
/// ceiling shipped in code would be wrong within a year, so the default is "no ceiling".
///
/// A single row, addressed by <see cref="SingletonId"/> rather than "first row" so a stray insert
/// can never leave two competing bands in the table.
/// </summary>
public sealed class PlatformPricing : Entity
{
    public static readonly Guid SingletonId = new("9f1b0d42-6c1a-4f7e-9a3d-1c8e5b2a7f04");

    public const string DefaultCurrency = "NGN";

    private PlatformPricing() { }

    public static PlatformPricing CreateDefault()
    {
        var pricing = new PlatformPricing { Currency = DefaultCurrency };
        pricing.SetId(SingletonId);
        return pricing;
    }

    /// <summary>Null means no floor: a counsellor may charge any amount, including a token fee.</summary>
    public decimal? MinSessionFee { get; private set; }

    /// <summary>Null means no ceiling, which is the default — the market sets the top, not us.</summary>
    public decimal? MaxSessionFee { get; private set; }

    public string Currency { get; private set; } = DefaultCurrency;
    public Guid? UpdatedByUserId { get; private set; }

    public void Update(decimal? minSessionFee, decimal? maxSessionFee, string currency, Guid adminUserId)
    {
        if (minSessionFee is < 0) throw new InvalidOperationException("The minimum fee cannot be negative.");
        if (maxSessionFee is < 0) throw new InvalidOperationException("The maximum fee cannot be negative.");
        if (minSessionFee is { } min && maxSessionFee is { } max && max < min)
            throw new InvalidOperationException("The maximum fee must be at least the minimum fee.");
        var normalizedCurrency = currency.Trim().ToUpperInvariant();
        if (normalizedCurrency.Length != 3)
            throw new InvalidOperationException("Currency must be a three-letter code, for example NGN.");

        MinSessionFee = minSessionFee is { } lower ? decimal.Round(lower, 2) : null;
        MaxSessionFee = maxSessionFee is { } upper ? decimal.Round(upper, 2) : null;
        Currency = normalizedCurrency;
        UpdatedByUserId = adminUserId;
        Touch();
    }

    /// <summary>Null when the fee is allowed; otherwise the message to show whoever set it.</summary>
    public string? Reject(decimal amount, string currency)
    {
        if (!string.Equals(currency.Trim(), Currency, StringComparison.OrdinalIgnoreCase))
            return $"Sessions are priced in {Currency} on Mirage.";
        if (MinSessionFee is { } min && amount < min)
            return $"Session fees start at {min:N0} {Currency}.";
        if (MaxSessionFee is { } max && amount > max)
            return $"Session fees are capped at {max:N0} {Currency}. Contact support if your practice needs a higher rate.";
        return null;
    }

    private void SetId(Guid id) => Id = id;
}
