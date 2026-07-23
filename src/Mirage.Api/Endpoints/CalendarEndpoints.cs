using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Security;
using Mirage.Application.Abstractions;
using Mirage.Domain.Enums;

namespace Mirage.Api.Endpoints;

// Aggregates every scheduled thing a user is part of — mentor meetings, counselling sessions,
// and org events they hold a ticket for — into one unified list for a calendar view.
internal static class CalendarEndpoints
{
    public static RouteGroupBuilder MapCalendarEndpoints(this RouteGroupBuilder api)
    {
        api.MapGroup("/calendar").WithTags("Calendar").RequireAuthorization()
            .MapGet("/mine", ListMine);
        return api;
    }

    private static async Task<IResult> ListMine(HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();

        var ownMentorProfileId = await db.Mentors.AsNoTracking()
            .Where(x => x.UserId == userId).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(cancellationToken);
        var acceptedMentorProfileIds = await db.MentorRequests.AsNoTracking()
            .Where(x => x.MenteeUserId == userId && x.Status == MentorRequestStatus.Accepted)
            .Select(x => x.MentorProfileId)
            .ToListAsync(cancellationToken);
        var mentorGroupIds = acceptedMentorProfileIds.ToList();
        if (ownMentorProfileId.HasValue) mentorGroupIds.Add(ownMentorProfileId.Value);

        var meetings = await db.MentorMeetings.AsNoTracking()
            .Where(x => mentorGroupIds.Contains(x.MentorProfileId))
            .Select(x => new CalendarItemResponse("MentorMeeting", x.Id, x.Title, x.ScheduledAt,
                x.DurationMinutes != null ? x.ScheduledAt.AddMinutes(x.DurationMinutes.Value) : null,
                x.MeetingLink, null, x.MentorProfileId))
            .ToListAsync(cancellationToken);

        var sessions = await db.CounsellingSessions.AsNoTracking()
            .Where(x => (x.ClientUserId == userId || x.Counsellor.UserId == userId
                || (x.PartnerUserId == userId && x.PartnerAccepted))
                && x.Status != SessionStatus.Declined && x.Status != SessionStatus.Cancelled)
            .Select(x => new CalendarItemResponse("CounsellingSession", x.Id, x.Topic, x.ScheduledAt, null, null, null, null))
            .ToListAsync(cancellationToken);

        var events = await db.EventTickets.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Join(db.OrgEvents.AsNoTracking(), t => t.EventId, e => e.Id, (t, e) => e)
            .Select(e => new CalendarItemResponse("OrgEvent", e.Id, e.Title, e.StartsAt, e.EndsAt, null, e.Location, null))
            .ToListAsync(cancellationToken);

        var createdEvents = await db.OrgEvents.AsNoTracking()
            .Where(x => x.CreatedByUserId == userId)
            .Select(e => new CalendarItemResponse("OrgEvent", e.Id, e.Title, e.StartsAt, e.EndsAt, null, e.Location, null))
            .ToListAsync(cancellationToken);

        var counsellingMeetings = await db.CounsellingMeetings.AsNoTracking()
            .Where(x => db.CounsellingSessions.Any(s => s.Id == x.SessionId
                && (s.ClientUserId == userId || s.Counsellor.UserId == userId
                    || (s.PartnerUserId == userId && s.PartnerAccepted))))
            .Select(x => new CalendarItemResponse("CounsellingMeeting", x.Id, x.Title, x.ScheduledAt,
                x.DurationMinutes != null ? x.ScheduledAt.AddMinutes(x.DurationMinutes.Value) : null,
                null, null, x.SessionId))
            .ToListAsync(cancellationToken);

        var dateRequests = await db.DateRequests.AsNoTracking()
            .Where(x => x.RequestorUserId == userId ||
                        x.Acceptances.Any(a => a.AcceptorUserId == userId &&
                                               a.Status != DateAcceptanceStatus.Withdrawn &&
                                               a.Status != DateAcceptanceStatus.Declined))
            .Select(x => new CalendarItemResponse("DateRequest", x.Id, x.Activity, x.StartsAt, x.EndsAt, null, x.LocationArea, null))
            .ToListAsync(cancellationToken);

        var items = meetings.Concat(sessions).Concat(events).Concat(createdEvents).Concat(counsellingMeetings).Concat(dateRequests)
            .GroupBy(x => new { x.Source, x.SourceId })
            .Select(x => x.First())
            .OrderBy(x => x.StartsAt)
            .ToList();

        return ApiResults.Ok(context, items, "Calendar retrieved successfully.");
    }
}
