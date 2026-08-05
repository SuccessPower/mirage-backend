using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Security;
using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;

namespace Mirage.Api.Endpoints;

internal static class CelebrationEndpoints
{
    public static RouteGroupBuilder MapCelebrationEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/celebrations").WithTags("Celebrations").RequireAuthorization();
        group.MapGet("/active", ListActive);
        group.MapGet("/user/{userId:guid}", ListForUser);
        return api;
    }

    // The home-page banner: only celebrations from the last 24 hours. The entries themselves are
    // permanent — ListForUser (the profile page) never filters by age.
    private static async Task<IResult> ListActive(HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
        var entries = await db.CelebrationEntries.AsNoTracking()
            .Where(x => x.CreatedAt >= cutoff)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        var result = await ProjectAsync(entries, db, cancellationToken);
        return ApiResults.Ok(context, result, "Active celebrations retrieved successfully.");
    }

    private static async Task<IResult> ListForUser(Guid userId, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var entries = await db.CelebrationEntries.AsNoTracking()
            .Where(x => x.UserId == userId || x.PartnerUserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        var result = await ProjectAsync(entries, db, cancellationToken);
        return ApiResults.Ok(context, result, "Celebrations retrieved successfully.");
    }

    private static async Task<IReadOnlyList<CelebrationResponse>> ProjectAsync(
        IReadOnlyList<CelebrationEntry> entries, IMirageDbContext db, CancellationToken cancellationToken)
    {
        if (entries.Count == 0) return [];
        var featuredIds = entries.SelectMany(x => new[] { x.UserId, x.PartnerUserId ?? x.UserId })
            .Distinct().ToList();
        var profiles = await db.Profiles.AsNoTracking()
            .Where(p => featuredIds.Contains(p.UserId))
            .Select(p => new { p.UserId, p.DisplayName, p.AvatarUrl })
            .ToDictionaryAsync(p => p.UserId, cancellationToken);

        return entries.Select(x =>
        {
            profiles.TryGetValue(x.UserId, out var user);
            var partner = x.PartnerUserId is not null && profiles.TryGetValue(x.PartnerUserId.Value, out var p)
                ? p : null;
            return new CelebrationResponse(
                x.Id, x.Type, x.Title, x.Body,
                x.UserId, user?.DisplayName ?? "Mirage member", user?.AvatarUrl,
                x.PartnerUserId, partner?.DisplayName, partner?.AvatarUrl,
                x.CreatedAt);
        }).ToList();
    }
}
