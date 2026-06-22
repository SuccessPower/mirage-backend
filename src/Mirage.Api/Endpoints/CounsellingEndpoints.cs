using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Security;
using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;

namespace Mirage.Api.Endpoints;

internal static class CounsellingEndpoints
{
    public static RouteGroupBuilder MapCounsellingEndpoints(this RouteGroupBuilder api)
    {
        var counsellors = api.MapGroup("/counsellors").WithTags("Counselling");
        counsellors.MapGet("/", ListCounsellors);

        var sessions = api.MapGroup("/sessions").WithTags("Counselling").RequireAuthorization();
        sessions.MapGet("/", ListSessions);
        sessions.MapPost("/", Book);
        sessions.MapPost("/{id:guid}/trust-unlock", ConsentToTrustUnlock);
        return api;
    }

    private static async Task<IResult> ListCounsellors(HttpContext context, IMirageDbContext db,
        string? specialisation,
        string? language, bool freeOnly = false, int page = 1, int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var query = db.Counsellors.AsNoTracking().Where(x => x.IsApproved);
        if (freeOnly) query = query.Where(x => x.AcceptsFreeSessions);
        if (!string.IsNullOrWhiteSpace(specialisation))
            query = query.Where(x => x.Specialisations.Any(value => EF.Functions.ILike(value, $"%{specialisation}%")));
        if (!string.IsNullOrWhiteSpace(language))
            query = query.Where(x => x.Languages.Any(value => EF.Functions.ILike(value, $"%{language}%")));
        var result = query.OrderByDescending(x => x.YearsExperience).Select(x => new
        {
            x.Id,
            DisplayName = x.IsAnonymous ? MaskName(x.UserProfile.DisplayName) : x.UserProfile.DisplayName,
            x.UserProfile.Denomination,
            Organisation = x.Organisation.Name,
            x.YearsExperience,
            x.IsAnonymous,
            x.AcceptsFreeSessions,
            x.Specialisations,
            x.Languages
        });
        return ApiResults.Ok(context,
            await result.ToPagedResultAsync(page, pageSize, cancellationToken),
            "Counsellors retrieved successfully.");
    }

    private static async Task<IResult> ListSessions(HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var sessions = await db.CounsellingSessions.AsNoTracking()
            .Where(x => x.ClientUserId == userId || x.Counsellor.UserId == userId)
            .OrderByDescending(x => x.ScheduledAt)
            .Select(x => new
            {
                x.Id, x.Type, x.ScheduledAt, x.Status, x.Topic, x.CounsellorAnonymous,
                x.ClientAnonymous, x.TrustUnlockStatus
            }).ToListAsync(cancellationToken);
        return ApiResults.Ok(context, sessions, "Counselling sessions retrieved successfully.");
    }

    private static async Task<IResult> Book(BookSessionRequest request, HttpContext context,
        IMirageDbContext db, CancellationToken cancellationToken)
    {
        if (request.ScheduledAt <= DateTimeOffset.UtcNow)
            return EndpointHelpers.ValidationProblem(context,
                ("scheduledAt", "Session must be scheduled in the future."));
        if (!await db.Counsellors.AnyAsync(x => x.Id == request.CounsellorId && x.IsApproved, cancellationToken))
            return EndpointHelpers.NotFound(context, "Approved counsellor was not found.");
        var session = new CounsellingSession(request.CounsellorId, context.User.GetUserId(), request.Type,
            request.ScheduledAt, request.Topic, request.CounsellorAnonymous, request.ClientAnonymous);
        db.CounsellingSessions.Add(session);
        db.AnonymityAuditLogs.Add(new AnonymityAuditLog(session.Id, context.User.GetUserId(),
            $"Session requested; clientAnonymous={request.ClientAnonymous}; counsellorAnonymous={request.CounsellorAnonymous}"));
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Created(context, $"/api/v1/sessions/{session.Id}",
            new { session.Id, session.Status }, "Counselling session requested successfully.");
    }

    private static async Task<IResult> ConsentToTrustUnlock(Guid id, HttpContext context,
        IMirageDbContext db, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var session = await db.CounsellingSessions.Include(x => x.Counsellor)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (session is null) return EndpointHelpers.NotFound(context, "Counselling session was not found.");
        var isClient = session.ClientUserId == userId;
        if (!isClient && session.Counsellor.UserId != userId) return EndpointHelpers.Forbidden(context);
        session.ConsentToReveal(isClient);
        db.AnonymityAuditLogs.Add(new AnonymityAuditLog(id, userId, "ConsentedToTrustUnlock"));
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { session.TrustUnlockStatus },
            "Trust unlock consent recorded successfully.");
    }

    private static string MaskName(string name) => string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => part.Length <= 2 ? $"{part[0]}*" : $"{part[0]}***{part[^1]}"));
}
