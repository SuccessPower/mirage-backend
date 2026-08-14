using Microsoft.EntityFrameworkCore;
using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;

namespace Mirage.Api.Services;

/// <summary>What counsellors are actually charging right now. Null on both ends when nobody has
/// set a fee yet.</summary>
public sealed record ObservedFeeRange(decimal? Low, decimal? High, int CounsellorCount);

/// <summary>
/// Reads and writes the one <see cref="PlatformPricing"/> row, and reports the live spread of
/// counsellor fees. The published pricing page quotes the live spread rather than a number typed
/// into copy, so what members read tracks the market instead of going stale.
///
/// The row is created on first read rather than by a data migration, so a database seeded before
/// this feature existed still answers instead of failing.
/// </summary>
public sealed class PricingService(IMirageDbContext db)
{
    public async Task<PlatformPricing> GetAsync(CancellationToken cancellationToken)
    {
        var pricing = await db.PlatformPricing
            .SingleOrDefaultAsync(x => x.Id == PlatformPricing.SingletonId, cancellationToken);
        if (pricing is not null) return pricing;

        pricing = PlatformPricing.CreateDefault();
        db.PlatformPricing.Add(pricing);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two first requests can race to create the singleton; the loser just reads the winner's.
            return await db.PlatformPricing.SingleAsync(x => x.Id == PlatformPricing.SingletonId, cancellationToken);
        }
        return pricing;
    }

    /// <summary>Only approved counsellors count: a rate nobody can book yet is not a market price.</summary>
    public async Task<ObservedFeeRange> ObservedRangeAsync(CancellationToken cancellationToken)
    {
        var fees = await db.Counsellors.AsNoTracking()
            .Where(x => x.IsApproved && x.PriceAmount != null && x.PriceAmount > 0)
            .Select(x => x.PriceAmount!.Value)
            .ToListAsync(cancellationToken);

        return fees.Count == 0
            ? new ObservedFeeRange(null, null, 0)
            : new ObservedFeeRange(fees.Min(), fees.Max(), fees.Count);
    }
}
