using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Security;
using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;

namespace Mirage.Api.Endpoints;

internal static class MilestoneEndpoints
{
    public static RouteGroupBuilder MapMilestoneEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/milestones").WithTags("Milestones");
        group.MapGet("/{userId:guid}", GetForUser);
        group.MapGet("/me", GetMine).RequireAuthorization();
        group.MapPost("/", Log).RequireAuthorization();
        return api;
    }

    private static async Task<IResult> GetForUser(Guid userId, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var milestones = await db.MilestoneLogs.AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new { x.Id, x.Type, x.PartnerId, x.CreatedAt })
            .ToListAsync(cancellationToken);
        return ApiResults.Ok(context, milestones, "Milestones retrieved successfully.");
    }

    private static async Task<IResult> GetMine(HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var milestones = await db.MilestoneLogs.AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new { x.Id, x.Type, x.PartnerId, x.Note, x.CreatedAt })
            .ToListAsync(cancellationToken);
        return ApiResults.Ok(context, milestones, "Milestones retrieved successfully.");
    }

    private static async Task<IResult> Log(LogMilestoneRequest request, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();

        if (request.PartnerId.HasValue && request.PartnerId.Value == userId)
            return EndpointHelpers.ValidationProblem(context, ("partnerId", "Cannot set yourself as partner."));

        if (request.PartnerId.HasValue)
        {
            if (!await db.Profiles.AnyAsync(x => x.UserId == request.PartnerId.Value, cancellationToken))
                return EndpointHelpers.NotFound(context, "Partner profile was not found.");
        }

        var milestone = new MilestoneLog(userId, request.Type, request.PartnerId, request.Note);
        db.MilestoneLogs.Add(milestone);
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Created(context, $"/api/v1/milestones/me",
            new { milestone.Id, milestone.Type }, $"Milestone '{request.Type}' logged successfully.");
    }
}
