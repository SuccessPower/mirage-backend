using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Security;
using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;

namespace Mirage.Api.Endpoints;

internal static class MatchingEndpoints
{
    public static RouteGroupBuilder MapMatchingEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/matching").WithTags("Matching").RequireAuthorization();
        group.MapPost("/likes", Like);
        group.MapGet("/matches", GetMatches);
        group.MapGet("/matches/{id:guid}", GetMatch);
        group.MapDelete("/matches/{id:guid}", CloseMatch);
        return api;
    }

    private static async Task<IResult> Like(LikeProfileRequest request, HttpContext context,
        IMirageDbContext db, CancellationToken cancellationToken)
    {
        var sourceUserId = context.User.GetUserId();
        if (sourceUserId == request.TargetUserId)
            return EndpointHelpers.ValidationProblem(context, ("targetUserId", "A user cannot like themselves."));
        if (!await db.Profiles.AnyAsync(x => x.UserId == request.TargetUserId, cancellationToken))
            return EndpointHelpers.NotFound(context, "Target profile was not found.");
        if (await db.Likes.AnyAsync(x => x.SourceUserId == sourceUserId && x.TargetUserId == request.TargetUserId,
                cancellationToken))
            return EndpointHelpers.Conflict(context, "Like already recorded.");

        db.Likes.Add(new UserLike(sourceUserId, request.TargetUserId, request.Type));
        var mutual = await db.Likes.AnyAsync(x => x.SourceUserId == request.TargetUserId && x.TargetUserId == sourceUserId,
            cancellationToken);
        Match? match = null;
        if (mutual)
        {
            match = new Match(sourceUserId, request.TargetUserId);
            db.Matches.Add(match);
        }
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { isMatch = match is not null, matchId = match?.Id },
            match is null ? "Like recorded successfully." : "It is a match.");
    }

    private static async Task<IResult> GetMatches(HttpContext context, IMirageDbContext db, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var matches = await db.Matches.AsNoTracking()
            .Where(x => x.User1Id == userId || x.User2Id == userId)
            .OrderByDescending(x => x.MatchedAt).ToListAsync(cancellationToken);
        return ApiResults.Ok(context, matches, "Matches retrieved successfully.");
    }

    private static async Task<IResult> GetMatch(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var match = await db.Matches.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == id && (x.User1Id == userId || x.User2Id == userId), cancellationToken);
        return match is null
            ? EndpointHelpers.NotFound(context, "Match was not found.")
            : ApiResults.Ok(context, match, "Match retrieved successfully.");
    }

    private static async Task<IResult> CloseMatch(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var match = await db.Matches.SingleOrDefaultAsync(
            x => x.Id == id && (x.User1Id == userId || x.User2Id == userId), cancellationToken);
        if (match is null) return EndpointHelpers.NotFound(context, "Match was not found.");
        if (match.Status != Mirage.Domain.Enums.MatchStatus.Active)
            return EndpointHelpers.Conflict(context, "Match is already closed.");
        match.Close();
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { match.Id, match.Status }, "Match closed successfully.");
    }
}
