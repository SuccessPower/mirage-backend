using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Security;
using Mirage.Application.Abstractions;
using Mirage.Domain.Enums;

namespace Mirage.Api.Endpoints;

// Cross-organisation event discovery — an Eventbrite-style public browse surface over the
// same OrgEvent/EventTicket data that OrganisationEndpoints exposes scoped to a single org.
internal static class EventEndpoints
{
    public static RouteGroupBuilder MapEventEndpoints(this RouteGroupBuilder api)
    {
        var events = api.MapGroup("/events").WithTags("Events");
        events.MapGet("/", ListUpcoming);
        events.MapGet("/{id:guid}", GetById);
        return api;
    }

    private static async Task<IResult> ListUpcoming(HttpContext context, IMirageDbContext db,
        string? search, bool includePast = false, int page = 1, int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var currentUserId = context.User.TryGetUserId();
        var now = DateTimeOffset.UtcNow;

        var query = db.OrgEvents.AsNoTracking()
            .Where(x => x.Organisation!.Status == OrganisationStatus.Approved);
        if (!includePast) query = query.Where(x => x.EndsAt >= now);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => EF.Functions.ILike(x.Title, $"%{term}%")
                || EF.Functions.ILike(x.Location, $"%{term}%")
                || EF.Functions.ILike(x.Organisation!.Name, $"%{term}%"));
        }

        var paged = await query
            .OrderBy(x => x.StartsAt)
            .Select(x => new
            {
                x.Id,
                x.OrganisationId,
                OrganisationName = x.Organisation!.Name,
                x.BranchId,
                x.Title,
                x.Description,
                x.ImageUrl,
                x.StartsAt,
                x.EndsAt,
                x.Location,
                x.Capacity,
                TicketsIssued = db.EventTickets.Count(t => t.EventId == x.Id),
            })
            .ToPagedResultAsync(page, pageSize, cancellationToken);

        var branchIds = paged.Items.Where(x => x.BranchId.HasValue).Select(x => x.BranchId!.Value).Distinct().ToArray();
        var branchNames = await db.OrganisationBranches.AsNoTracking()
            .Where(x => branchIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var eventIds = paged.Items.Select(x => x.Id).ToArray();
        var registeredEventIds = currentUserId is null
            ? []
            : await db.EventTickets.AsNoTracking()
                .Where(x => x.UserId == currentUserId && eventIds.Contains(x.EventId))
                .Select(x => x.EventId)
                .ToListAsync(cancellationToken);

        var response = new Mirage.Application.Common.PagedResult<PublicEventResponse>(
            paged.Items.Select(x => new PublicEventResponse(
                x.Id, x.OrganisationId, x.OrganisationName, x.BranchId,
                x.BranchId.HasValue ? branchNames.GetValueOrDefault(x.BranchId.Value) : null,
                x.Title, x.Description, x.ImageUrl, x.StartsAt, x.EndsAt, x.Location, x.Capacity,
                x.TicketsIssued, registeredEventIds.Contains(x.Id)))
                .ToList(),
            paged.Page, paged.PageSize, paged.TotalCount);

        return ApiResults.Ok(context, response, "Events retrieved successfully.");
    }

    private static async Task<IResult> GetById(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var currentUserId = context.User.TryGetUserId();
        var evt = await db.OrgEvents.AsNoTracking()
            .Where(x => x.Id == id && x.Organisation!.Status == OrganisationStatus.Approved)
            .Select(x => new
            {
                x.Id,
                x.OrganisationId,
                OrganisationName = x.Organisation!.Name,
                x.BranchId,
                x.Title,
                x.Description,
                x.ImageUrl,
                x.StartsAt,
                x.EndsAt,
                x.Location,
                x.Capacity,
                TicketsIssued = db.EventTickets.Count(t => t.EventId == x.Id),
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (evt is null) return EndpointHelpers.NotFound(context, "Event was not found.");

        var branchName = evt.BranchId is null
            ? null
            : await db.OrganisationBranches.AsNoTracking()
                .Where(x => x.Id == evt.BranchId).Select(x => x.Name).SingleOrDefaultAsync(cancellationToken);
        var isRegistered = currentUserId is not null &&
            await db.EventTickets.AsNoTracking().AnyAsync(x => x.EventId == id && x.UserId == currentUserId, cancellationToken);

        var response = new PublicEventResponse(evt.Id, evt.OrganisationId, evt.OrganisationName, evt.BranchId,
            branchName, evt.Title, evt.Description, evt.ImageUrl, evt.StartsAt, evt.EndsAt, evt.Location,
            evt.Capacity, evt.TicketsIssued, isRegistered);
        return ApiResults.Ok(context, response, "Event retrieved successfully.");
    }
}
