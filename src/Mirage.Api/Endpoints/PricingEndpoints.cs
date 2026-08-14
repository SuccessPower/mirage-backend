using Mirage.Api.Contracts;
using Mirage.Api.Services;
using Mirage.Domain.Entities;

namespace Mirage.Api.Endpoints;

internal static class PricingEndpoints
{
    /// <summary>
    /// Public, unauthenticated: the published pricing page reads this so the range it shows is
    /// always the range counsellors are actually held to. Payment processors review that page,
    /// so the two must never be able to disagree.
    /// </summary>
    public static RouteGroupBuilder MapPricingEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/pricing", GetPricing).WithTags("Pricing").AllowAnonymous();
        return api;
    }

    private static async Task<IResult> GetPricing(HttpContext context, PricingService pricing,
        CancellationToken cancellationToken)
    {
        var band = await pricing.GetAsync(cancellationToken);
        var observed = await pricing.ObservedRangeAsync(cancellationToken);
        return ApiResults.Ok(context, new
        {
            // The bounds admin enforces. Either end may be null, meaning "no limit at that end".
            band.MinSessionFee,
            band.MaxSessionFee,
            band.Currency,
            CommissionPercent = Payment.PlatformCommissionRate * 100m,
            // What counsellors charge today. This is what the public page quotes, so the figures
            // move with the market instead of being copy that has to be remembered and edited.
            ObservedLow = observed.Low,
            ObservedHigh = observed.High,
            observed.CounsellorCount,
            band.UpdatedAt,
        }, "Pricing retrieved successfully.");
    }
}
