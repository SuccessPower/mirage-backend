using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Services;
using Mirage.Api.Security;
using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;

namespace Mirage.Api.Endpoints;

// A mentor's or counsellor's broadcast desk: write something once, choose who hears it and when,
// and let the dispatcher deliver it. Posting to a group was already possible, but only in the
// moment — so preparing a week of material meant coming back and posting it every day.
//
// One route group serves both practices rather than one under /mentors and another under
// /counsellors, because it is one page: a professional who is both writes their broadcasts in one
// place and picks the practice per broadcast. The caller's own mentor/counsellor profile is
// resolved from their user id, so nothing here takes a profile id from the client.
internal static class BroadcastEndpoints
{
    public static RouteGroupBuilder MapBroadcastEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/broadcasts").WithTags("Broadcasts").RequireAuthorization();
        group.MapGet("/audience", GetAudience);
        group.MapGet("/", ListBroadcasts);
        group.MapPost("/", CreateBroadcast);
        group.MapPut("/{id:guid}", UpdateBroadcast);
        group.MapPost("/{id:guid}/send-now", SendNow);
        group.MapDelete("/{id:guid}", CancelBroadcast);
        return api;
    }

    private sealed record Practices(Guid? MentorProfileId, Guid? CounsellorProfileId);

    // An unapproved mentor or counsellor has no group yet, so they have nobody to broadcast to.
    private static async Task<Practices> PracticesAsync(Guid userId, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var mentorId = await db.Mentors.AsNoTracking()
            .Where(x => x.UserId == userId && x.IsApproved)
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(cancellationToken);
        var counsellorId = await db.Counsellors.AsNoTracking()
            .Where(x => x.UserId == userId && x.IsApproved)
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(cancellationToken);
        return new Practices(mentorId, counsellorId);
    }

    private static async Task<IResult> GetAudience(HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var practices = await PracticesAsync(userId, db, cancellationToken);

        var free = practices.MentorProfileId is { } m1
            ? (await BroadcastAudience.MentorRecipientsAsync(db, m1, MentorAudience.FreeMentees, cancellationToken)).Count
            : 0;
        var paid = practices.MentorProfileId is { } m2
            ? (await BroadcastAudience.MentorRecipientsAsync(db, m2, MentorAudience.PaidMentees, cancellationToken)).Count
            : 0;
        var counselees = practices.CounsellorProfileId is { } c
            ? (await BroadcastAudience.CounsellorRecipientsAsync(db, c, cancellationToken)).Count
            : 0;

        return ApiResults.Ok(context, new BroadcastAudienceResponse(
            practices.MentorProfileId is not null, practices.CounsellorProfileId is not null,
            free, paid, counselees), "Broadcast audience retrieved successfully.");
    }

    private static async Task<IResult> ListBroadcasts(HttpContext context, IMirageDbContext db,
        BroadcastStatus? status, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var query = db.ProfessionalBroadcasts.AsNoTracking().Where(x => x.AuthorUserId == userId);
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);

        // A professional's own broadcasts are a short list, so it is fetched whole and ordered in
        // memory — Map is a plain method and would not survive translation into SQL anyway.
        //
        // Scheduled first and soonest-first: the ones still to go out are what the author came
        // here to check. Everything already dealt with falls in behind them, newest first.
        var rows = await query.ToListAsync(cancellationToken);
        var broadcasts = rows
            .OrderBy(x => x.Status == BroadcastStatus.Scheduled ? 0 : 1)
            .ThenBy(x => x.Status == BroadcastStatus.Scheduled ? x.ScheduledFor : DateTimeOffset.MaxValue)
            .ThenByDescending(x => x.ScheduledFor)
            .Select(Map)
            .ToList();
        return ApiResults.Ok(context, broadcasts, "Broadcasts retrieved successfully.");
    }

    private static async Task<IResult> CreateBroadcast(SaveBroadcastRequest request, HttpContext context,
        IMirageDbContext db, BroadcastDispatchService dispatcher, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var practices = await PracticesAsync(userId, db, cancellationToken);
        if (Validate(context, request, practices) is { } problem) return problem;

        var mentorId = request.Practice == BroadcastPractice.Mentorship ? practices.MentorProfileId : null;
        var counsellorId = request.Practice == BroadcastPractice.Counselling ? practices.CounsellorProfileId : null;
        // Counselling has no free/paid split, so a counsellor's broadcast always addresses their
        // whole group whatever the composer sent.
        var audience = counsellorId is not null ? MentorAudience.Everyone : request.Audience;

        var broadcast = request.Kind == BroadcastKind.Event
            ? ProfessionalBroadcast.Event(mentorId, counsellorId, userId, request.Title!, request.Content,
                request.ImageUrl, request.Location!, request.StartsAt!.Value, request.EndsAt!.Value,
                request.Capacity, audience, request.ScheduledFor)
            : ProfessionalBroadcast.Message(mentorId, counsellorId, userId, request.Content!, request.ImageUrl,
                audience, request.ScheduledFor);
        db.ProfessionalBroadcasts.Add(broadcast);
        await db.SaveChangesAsync(cancellationToken);

        // Scheduling for a time that has already passed means "send it now" — the worker would
        // pick it up on its next tick anyway, so going straight there saves the wait.
        if (broadcast.ScheduledFor <= DateTimeOffset.UtcNow)
            await dispatcher.DispatchAsync(broadcast.Id, cancellationToken);

        return ApiResults.Created(context, $"/api/v1/broadcasts/{broadcast.Id}", new { broadcast.Id },
            "Broadcast scheduled successfully.");
    }

    private static async Task<IResult> UpdateBroadcast(Guid id, SaveBroadcastRequest request, HttpContext context,
        IMirageDbContext db, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var broadcast = await db.ProfessionalBroadcasts
            .SingleOrDefaultAsync(x => x.Id == id && x.AuthorUserId == userId, cancellationToken);
        if (broadcast is null) return EndpointHelpers.NotFound(context, "Broadcast was not found.");
        if (broadcast.Status != BroadcastStatus.Scheduled)
            return EndpointHelpers.Conflict(context, "This broadcast has already gone out and cannot be edited.");
        if (broadcast.Kind != request.Kind)
            return EndpointHelpers.Conflict(context, "A broadcast cannot change between a message and an event.");

        var practices = await PracticesAsync(userId, db, cancellationToken);
        if (Validate(context, request, practices) is { } problem) return problem;

        var audience = broadcast.CounsellorProfileId is not null ? MentorAudience.Everyone : request.Audience;
        if (request.Kind == BroadcastKind.Event)
            broadcast.EditEvent(request.Title!, request.Content, request.ImageUrl, request.Location!,
                request.StartsAt!.Value, request.EndsAt!.Value, request.Capacity, audience, request.ScheduledFor);
        else
            broadcast.EditMessage(request.Content!, request.ImageUrl, audience, request.ScheduledFor);

        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { broadcast.Id }, "Broadcast updated successfully.");
    }

    private static async Task<IResult> SendNow(Guid id, HttpContext context, IMirageDbContext db,
        BroadcastDispatchService dispatcher, CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var broadcast = await db.ProfessionalBroadcasts
            .SingleOrDefaultAsync(x => x.Id == id && x.AuthorUserId == userId, cancellationToken);
        if (broadcast is null) return EndpointHelpers.NotFound(context, "Broadcast was not found.");
        if (broadcast.Status != BroadcastStatus.Scheduled)
            return EndpointHelpers.Conflict(context, "This broadcast has already gone out.");

        broadcast.SendNow();
        await db.SaveChangesAsync(cancellationToken);
        await dispatcher.DispatchAsync(broadcast.Id, cancellationToken);
        return ApiResults.Ok(context, new { broadcast.Id }, "Broadcast sent successfully.");
    }

    private static async Task<IResult> CancelBroadcast(Guid id, HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var broadcast = await db.ProfessionalBroadcasts
            .SingleOrDefaultAsync(x => x.Id == id && x.AuthorUserId == userId, cancellationToken);
        if (broadcast is null) return EndpointHelpers.NotFound(context, "Broadcast was not found.");
        if (broadcast.Status != BroadcastStatus.Scheduled)
            return EndpointHelpers.Conflict(context, "Only a broadcast that has not gone out yet can be cancelled.");

        broadcast.Cancel();
        await db.SaveChangesAsync(cancellationToken);
        return ApiResults.Ok(context, new { broadcast.Id }, "Broadcast cancelled successfully.");
    }

    private static IResult? Validate(HttpContext context, SaveBroadcastRequest request, Practices practices)
    {
        var practiceId = request.Practice == BroadcastPractice.Mentorship
            ? practices.MentorProfileId
            : practices.CounsellorProfileId;
        if (practiceId is null)
            return EndpointHelpers.Forbidden(context, request.Practice == BroadcastPractice.Mentorship
                ? "You need an approved mentor profile to broadcast to mentees."
                : "You need an approved counsellor profile to broadcast to the people you counsel.");

        if (request.Kind == BroadcastKind.Event)
        {
            if (string.IsNullOrWhiteSpace(request.Title))
                return EndpointHelpers.ValidationProblem(context, ("title", "An event title is required."));
            if (string.IsNullOrWhiteSpace(request.Location))
                return EndpointHelpers.ValidationProblem(context, ("location", "An event location is required."));
            if (request.StartsAt is null || request.EndsAt is null)
                return EndpointHelpers.ValidationProblem(context, ("startsAt", "Event start and end times are required."));
            if (request.EndsAt <= request.StartsAt)
                return EndpointHelpers.ValidationProblem(context, ("endsAt", "The event must end after it starts."));
            if (request.Capacity is <= 0)
                return EndpointHelpers.ValidationProblem(context, ("capacity", "Capacity must be greater than zero."));
        }
        else if (string.IsNullOrWhiteSpace(request.Content))
        {
            return EndpointHelpers.ValidationProblem(context, ("content", "There is nothing to broadcast."));
        }

        // A broadcast can be scheduled for a year out but not for last year: a far-past date is
        // almost always a mistyped year, and it would go out the instant it was saved.
        if (request.ScheduledFor < DateTimeOffset.UtcNow.AddDays(-1))
            return EndpointHelpers.ValidationProblem(context, ("scheduledFor", "Pick a time that has not already passed."));

        return null;
    }

    private static BroadcastResponse Map(ProfessionalBroadcast x) =>
        new(x.Id,
            x.MentorProfileId is not null ? BroadcastPractice.Mentorship : BroadcastPractice.Counselling,
            x.Kind, x.Status, x.Audience, x.ScheduledFor, x.Content, x.ImageUrl, x.Title, x.Location,
            x.StartsAt, x.EndsAt, x.Capacity, x.RecipientCount, x.SentAt, x.PublishedEntityId,
            x.FailureReason, x.CreatedAt);
}
