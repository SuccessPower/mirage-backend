using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Domain.Entities;
using Mirage.Api.Security;
using Mirage.Application.Abstractions;
using Mirage.Domain.Enums;
using Mirage.Infrastructure.Identity;

namespace Mirage.Api.Endpoints;

// Cross-organisation event discovery — an Eventbrite-style public browse surface over the
// same OrgEvent/EventTicket data that OrganisationEndpoints exposes scoped to a single org.
// Approved mentors publish here too (see MentorEndpoints.CreateEvent), so a row's host is either
// an approved organisation or an approved mentor.
internal static class EventEndpoints
{
    public static RouteGroupBuilder MapEventEndpoints(this RouteGroupBuilder api)
    {
        var events = api.MapGroup("/events").WithTags("Events");
        events.MapGet("/", ListUpcoming);
        events.MapGet("/{id:guid}", GetById);
        // Host-neutral registration. The organisation-scoped route still exists for the church
        // admin surface, but a mentor's event has no organisation to scope to, so registering by
        // event id alone is the only route that works for every event on this feed.
        events.MapPost("/{id:guid}/register", Register).RequireAuthorization();
        // Whoever posted an event can take it down, whichever surface they posted it from. The
        // mentor-scoped DELETE /mentors/me/events/{id} still exists for the mentor dashboard, but
        // a church admin's event had no delete route at all, and neither route is reachable from
        // this shared feed — where the host is the one person most likely to be looking at it.
        events.MapDelete("/{id:guid}", Delete).RequireAuthorization();
        return api;
    }

    // Deleting is the host's own call: the account that posted it, a manager of the host church
    // (the poster may have left, or another manager may be clearing the calendar), or a platform
    // admin. Tickets cascade with the event, so nothing is left pointing at a deleted row.
    private static async Task<bool> CanDeleteAsync(Guid createdByUserId, Guid? organisationId,
        HttpContext context, IMirageDbContext db, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        if (createdByUserId == userId) return true;
        if (context.User.IsInRole(MirageRoles.PlatformAdmin)) return true;
        if (organisationId is not { } orgId) return false;
        return await db.Organisations.AsNoTracking()
                   .AnyAsync(x => x.Id == orgId && x.AdminUserId == userId, cancellationToken)
               || await db.OrganisationManagers.AsNoTracking()
                   .AnyAsync(x => x.OrganisationId == orgId && x.UserId == userId, cancellationToken);
    }

    // Every organisation this user speaks for, as its original admin or as an added manager.
    private static async Task<HashSet<Guid>> ManagedOrganisationIdsAsync(Guid userId, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var owned = await db.Organisations.AsNoTracking()
            .Where(x => x.AdminUserId == userId).Select(x => x.Id).ToListAsync(cancellationToken);
        var managed = await db.OrganisationManagers.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => x.OrganisationId).ToListAsync(cancellationToken);
        return owned.Concat(managed).ToHashSet();
    }

    private static async Task<IResult> Delete(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var evt = await db.OrgEvents.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (evt is null) return EndpointHelpers.NotFound(context, "Event was not found.");
        if (!await CanDeleteAsync(evt.CreatedByUserId, evt.OrganisationId, context, db, cancellationToken))
            return EndpointHelpers.Forbidden(context, "Only the host can delete this event.");

        db.OrgEvents.Remove(evt);
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { evt.Id }, "Event deleted successfully.");
    }

    private static async Task<IResult> Register(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var evt = await db.OrgEvents.AsNoTracking()
            .Where(x => x.Id == id && (x.Organisation!.Status == OrganisationStatus.Approved
                || (x.Mentor != null && x.Mentor.IsApproved)))
            .Select(x => new { x.Id, x.Capacity, x.EndsAt })
            .SingleOrDefaultAsync(cancellationToken);
        if (evt is null) return EndpointHelpers.NotFound(context, "Event was not found.");
        if (evt.EndsAt < DateTimeOffset.UtcNow)
            return EndpointHelpers.Conflict(context, "This event has already ended.");

        if (await db.EventTickets.AnyAsync(x => x.EventId == id && x.UserId == userId, cancellationToken))
            return EndpointHelpers.Conflict(context, "You are already registered for this event.");

        if (evt.Capacity.HasValue)
        {
            var issued = await db.EventTickets.CountAsync(x => x.EventId == id, cancellationToken);
            if (issued >= evt.Capacity.Value)
                return EndpointHelpers.Conflict(context, "This event is fully booked.");
        }

        var code = Convert.ToBase64String(RandomNumberGenerator.GetBytes(9))
            .Replace('+', 'A').Replace('/', 'B').Replace('=', 'C');
        var ticket = new EventTicket(id, userId, code);
        db.EventTickets.Add(ticket);
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Created(context, $"/api/v1/events/{id}", new { ticket.Id, ticket.Code },
            "Ticket issued successfully.");
    }

    private static async Task<IResult> ListUpcoming(HttpContext context, IMirageDbContext db,
        string? search, bool includePast = false, int page = 1, int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var currentUserId = context.User.TryGetUserId();
        var now = DateTimeOffset.UtcNow;

        var query = db.OrgEvents.AsNoTracking()
            .Where(x => x.Organisation!.Status == OrganisationStatus.Approved
                || (x.Mentor != null && x.Mentor.IsApproved));
        if (!includePast) query = query.Where(x => x.EndsAt >= now);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => EF.Functions.ILike(x.Title, $"%{term}%")
                || EF.Functions.ILike(x.Location, $"%{term}%")
                || (x.Organisation != null && EF.Functions.ILike(x.Organisation.Name, $"%{term}%"))
                || (x.Mentor != null && EF.Functions.ILike(x.Mentor.UserProfile.DisplayName, $"%{term}%")));
        }

        var paged = await query
            .OrderBy(x => x.StartsAt)
            .Select(x => new
            {
                x.Id,
                x.OrganisationId,
                OrganisationName = x.Organisation != null ? x.Organisation.Name : null,
                x.BranchId,
                x.MentorProfileId,
                MentorName = x.Mentor != null ? x.Mentor.UserProfile.DisplayName : null,
                MentorAvatarUrl = x.Mentor != null ? x.Mentor.UserProfile.AvatarUrl : null,
                x.Title,
                x.Description,
                x.ImageUrl,
                x.StartsAt,
                x.EndsAt,
                x.Location,
                x.Capacity,
                x.CreatedByUserId,
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

        // Resolved once for the page rather than per row: a manager looking at the feed would
        // otherwise cost one membership query per event.
        var managedOrganisationIds = currentUserId is null
            ? []
            : await ManagedOrganisationIdsAsync(currentUserId.Value, db, cancellationToken);
        var isPlatformAdmin = context.User.IsInRole(MirageRoles.PlatformAdmin);
        bool CanDelete(Guid createdByUserId, Guid? organisationId) =>
            currentUserId is not null
            && (createdByUserId == currentUserId
                || isPlatformAdmin
                || (organisationId is { } orgId && managedOrganisationIds.Contains(orgId)));

        var response = new Mirage.Application.Common.PagedResult<PublicEventResponse>(
            paged.Items.Select(x => new PublicEventResponse(
                x.Id, x.OrganisationId, x.OrganisationName, x.BranchId,
                x.BranchId.HasValue ? branchNames.GetValueOrDefault(x.BranchId.Value) : null,
                x.MentorProfileId, x.MentorName, x.MentorAvatarUrl,
                x.MentorProfileId is null ? "Organisation" : "Mentor",
                x.MentorProfileId is null ? x.OrganisationName ?? "Mirage" : x.MentorName ?? "A mentor",
                x.Title, x.Description, x.ImageUrl, x.StartsAt, x.EndsAt, x.Location, x.Capacity,
                x.TicketsIssued, registeredEventIds.Contains(x.Id),
                CanDelete(x.CreatedByUserId, x.OrganisationId)))
                .ToList(),
            paged.Page, paged.PageSize, paged.TotalCount);

        return ApiResults.Ok(context, response, "Events retrieved successfully.");
    }

    private static async Task<IResult> GetById(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var currentUserId = context.User.TryGetUserId();
        var evt = await db.OrgEvents.AsNoTracking()
            .Where(x => x.Id == id && (x.Organisation!.Status == OrganisationStatus.Approved
                || (x.Mentor != null && x.Mentor.IsApproved)))
            .Select(x => new
            {
                x.Id,
                x.OrganisationId,
                OrganisationName = x.Organisation != null ? x.Organisation.Name : null,
                x.BranchId,
                x.MentorProfileId,
                MentorName = x.Mentor != null ? x.Mentor.UserProfile.DisplayName : null,
                MentorAvatarUrl = x.Mentor != null ? x.Mentor.UserProfile.AvatarUrl : null,
                x.Title,
                x.Description,
                x.ImageUrl,
                x.StartsAt,
                x.EndsAt,
                x.Location,
                x.Capacity,
                x.CreatedByUserId,
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
            branchName, evt.MentorProfileId, evt.MentorName, evt.MentorAvatarUrl,
            evt.MentorProfileId is null ? "Organisation" : "Mentor",
            evt.MentorProfileId is null ? evt.OrganisationName ?? "Mirage" : evt.MentorName ?? "A mentor",
            evt.Title, evt.Description, evt.ImageUrl, evt.StartsAt, evt.EndsAt, evt.Location,
            evt.Capacity, evt.TicketsIssued, isRegistered,
            currentUserId is not null
            && await CanDeleteAsync(evt.CreatedByUserId, evt.OrganisationId, context, db, cancellationToken));
        return ApiResults.Ok(context, response, "Event retrieved successfully.");
    }
}
