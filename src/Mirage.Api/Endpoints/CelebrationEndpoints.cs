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
        group.MapGet("/{id:guid}/wishes", ListWishes);
        group.MapPost("/{id:guid}/wishes", AddWish);
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

    private static async Task<IResult> ListWishes(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        if (!await db.CelebrationEntries.AsNoTracking().AnyAsync(x => x.Id == id, cancellationToken))
            return EndpointHelpers.NotFound(context, "Celebration was not found.");
        var wishes = await db.CelebrationWishes.AsNoTracking()
            .Where(x => x.CelebrationEntryId == id)
            .OrderBy(x => x.CreatedAt)
            .Select(x => new CelebrationWishResponse(x.Id, x.CelebrationEntryId, x.AuthorUserId,
                db.Profiles.Where(p => p.UserId == x.AuthorUserId).Select(p => p.DisplayName).FirstOrDefault() ?? "Mirage member",
                db.Profiles.Where(p => p.UserId == x.AuthorUserId).Select(p => p.AvatarUrl).FirstOrDefault(),
                x.Body, x.CreatedAt))
            .ToListAsync(cancellationToken);
        return ApiResults.Ok(context, wishes, "Wishes retrieved successfully.");
    }

    private static async Task<IResult> AddWish(Guid id, CreateCelebrationWishRequest request,
        HttpContext context, IMirageDbContext db, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Body) || request.Body.Trim().Length is < 2 or > 500)
            return EndpointHelpers.ValidationProblem(context, ("body", "A wish must be between 2 and 500 characters."));
        if (!await db.CelebrationEntries.AsNoTracking().AnyAsync(x => x.Id == id, cancellationToken))
            return EndpointHelpers.NotFound(context, "Celebration was not found.");

        var wish = new CelebrationWish(id, context.User.GetUserId(), request.Body);
        db.CelebrationWishes.Add(wish);
        await db.SaveChangesAsync(cancellationToken);

        var author = await db.Profiles.AsNoTracking().Where(x => x.UserId == wish.AuthorUserId)
            .Select(x => new { x.DisplayName, x.AvatarUrl }).SingleAsync(cancellationToken);
        var response = new CelebrationWishResponse(wish.Id, wish.CelebrationEntryId, wish.AuthorUserId,
            author.DisplayName, author.AvatarUrl, wish.Body, wish.CreatedAt);
        return ApiResults.Created(context, $"/api/v1/celebrations/{id}/wishes", response, "Wish posted.");
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
        var entryIds = entries.Select(x => x.Id).ToList();
        var wishCounts = await db.CelebrationWishes.AsNoTracking()
            .Where(x => entryIds.Contains(x.CelebrationEntryId))
            .GroupBy(x => x.CelebrationEntryId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);

        return entries.Select(x =>
        {
            profiles.TryGetValue(x.UserId, out var user);
            var partner = x.PartnerUserId is not null && profiles.TryGetValue(x.PartnerUserId.Value, out var p)
                ? p : null;
            return new CelebrationResponse(
                x.Id, x.Type, x.Title, x.Body,
                x.UserId, user?.DisplayName ?? "Mirage member", user?.AvatarUrl,
                x.PartnerUserId, partner?.DisplayName, partner?.AvatarUrl,
                x.CreatedAt, wishCounts.GetValueOrDefault(x.Id));
        }).ToList();
    }
}
