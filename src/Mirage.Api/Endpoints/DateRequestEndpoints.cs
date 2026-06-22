using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Security;
using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;

namespace Mirage.Api.Endpoints;

internal static class DateRequestEndpoints
{
    public static RouteGroupBuilder MapDateRequestEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/date-requests").WithTags("Date Requests").RequireAuthorization();
        group.MapGet("/", List);
        group.MapPost("/", Create);
        group.MapPost("/{id:guid}/accept", Accept);
        group.MapPost("/{id:guid}/select/{userId:guid}", Select);
        return api;
    }

    private static async Task<IResult> List(HttpContext context, IMirageDbContext db,
        string? location, int page = 1, int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var query = db.DateRequests.AsNoTracking().Where(x => x.Status == DateRequestStatus.Open && x.EndsAt > DateTimeOffset.UtcNow);
        if (!string.IsNullOrWhiteSpace(location))
            query = query.Where(x => EF.Functions.ILike(x.LocationArea, $"%{location.Trim()}%"));
        return ApiResults.Ok(context,
            await query.OrderBy(x => x.StartsAt).ToPagedResultAsync(page, pageSize, cancellationToken),
            "Date requests retrieved successfully.");
    }

    private static async Task<IResult> Create(CreateDateRequestRequest request, HttpContext context,
        IMirageDbContext db, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var eligible = await db.Profiles.AnyAsync(x => x.UserId == userId && x.IsVerified, cancellationToken) ||
                       await db.Recommendations.AnyAsync(x => x.RecommendedUserId == userId &&
                           x.Status == RecommendationStatus.Active, cancellationToken);
        if (!eligible)
            return EndpointHelpers.Forbidden(context, "Only verified or recommended users can post date requests.");
        if (request.StartsAt <= DateTimeOffset.UtcNow || request.EndsAt <= request.StartsAt)
            return EndpointHelpers.ValidationProblem(context, ("schedule", "Provide a valid future time window."));
        var entity = new DateRequest(userId, request.Activity, request.StartsAt, request.EndsAt,
            request.LocationArea, request.Note);
        db.DateRequests.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Created(context, $"/api/v1/date-requests/{entity.Id}", entity,
            "Date request created successfully.");
    }

    private static async Task<IResult> Accept(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var request = await db.DateRequests.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (request is null) return EndpointHelpers.NotFound(context, "Date request was not found.");
        if (request.RequestorUserId == userId || request.Status != DateRequestStatus.Open)
            return EndpointHelpers.Conflict(context, "The date request cannot be accepted.");
        if (await db.DateRequestAcceptances.AnyAsync(x => x.DateRequestId == id && x.AcceptorUserId == userId,
                cancellationToken))
            return EndpointHelpers.Conflict(context, "Date request already accepted.");
        db.DateRequestAcceptances.Add(new DateRequestAcceptance(id, userId));
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { dateRequestId = id }, "Date request accepted successfully.");
    }

    private static async Task<IResult> Select(Guid id, Guid userId, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var actor = context.User.GetUserId();
        var request = await db.DateRequests.Include(x => x.Acceptances)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (request is null) return EndpointHelpers.NotFound(context, "Date request was not found.");
        if (request.RequestorUserId != actor) return EndpointHelpers.Forbidden(context);
        if (!request.Acceptances.Any(x => x.AcceptorUserId == userId))
            return EndpointHelpers.NotFound(context, "Date request acceptance was not found.");
        request.Select(userId);
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { dateRequestId = id, selectedUserId = userId },
            "Date request participant selected successfully.");
    }
}
