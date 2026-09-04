using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Security;
using Mirage.Application.Abstractions;
using Mirage.Domain.Enums;

namespace Mirage.Api.Endpoints;

// Aggregates every scheduled thing a user is part of — mentor meetings and events, counselling
// sessions, and org events they hold a ticket for — into one unified list for a calendar view.
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
        var acceptedMemberships = await db.MentorRequests.AsNoTracking()
            .Where(x => x.MenteeUserId == userId && x.Status == MentorRequestStatus.Accepted)
            .Select(x => new { x.Id, x.MentorProfileId, x.Tier })
            .ToListAsync(cancellationToken);

        // A mentee only has the free group's or the paid group's meetings on their calendar,
        // never the other group's. Everyone-audience meetings land on both.
        var freeGroupIds = acceptedMemberships.Where(x => x.Tier == MentorshipTier.Free)
            .Select(x => x.MentorProfileId).Distinct().ToList();
        var paidGroupIds = acceptedMemberships.Where(x => x.Tier == MentorshipTier.Paid)
            .Select(x => x.MentorProfileId).Distinct().ToList();
        var acceptedMentorRequestIds = acceptedMemberships.Select(x => x.Id).ToList();

        var meetings = await db.MentorMeetings.AsNoTracking()
            .Where(x => (x.MentorRequestId == null
                    && ((x.Audience != MentorAudience.PaidMentees && freeGroupIds.Contains(x.MentorProfileId))
                        || (x.Audience != MentorAudience.FreeMentees && paidGroupIds.Contains(x.MentorProfileId))))
                || (x.MentorRequestId != null && acceptedMentorRequestIds.Contains(x.MentorRequestId.Value))
                || (ownMentorProfileId != null && x.MentorProfileId == ownMentorProfileId.Value))
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

        // Organisation events are community dates: an approved church/organisation member should
        // see them without first having to issue themselves a ticket.
        var memberOrganisationIds = await db.OrganisationMembers.AsNoTracking()
            .Where(x => x.UserId == userId && x.Status == OrganisationMemberStatus.Approved)
            .Select(x => x.OrganisationId).ToListAsync(cancellationToken);
        var communityEvents = await db.OrgEvents.AsNoTracking()
            .Where(x => x.OrganisationId != null && memberOrganisationIds.Contains(x.OrganisationId.Value))
            .Select(e => new CalendarItemResponse("OrgEvent", e.Id, e.Title, e.StartsAt, e.EndsAt, null, e.Location, null))
            .ToListAsync(cancellationToken);

        // A mentor's public events belong on their mentees' calendars the way a church's belong on
        // its members'. The event is public either way; the audience decides which group carries
        // it, so a free mentee never sees a paid-group event on their calendar.
        var mentorEvents = await db.OrgEvents.AsNoTracking()
            .Where(x => x.MentorProfileId != null
                && ((x.Audience != MentorAudience.PaidMentees && freeGroupIds.Contains(x.MentorProfileId.Value))
                    || (x.Audience != MentorAudience.FreeMentees && paidGroupIds.Contains(x.MentorProfileId.Value))
                    || (ownMentorProfileId != null && x.MentorProfileId == ownMentorProfileId.Value)))
            .Select(e => new CalendarItemResponse("OrgEvent", e.Id, e.Title, e.StartsAt, e.EndsAt, null, e.Location, null))
            .ToListAsync(cancellationToken);

        // A gathering someone was invited to and accepted, where accepting the invite did not also
        // write an acceptance row — without this it sat on the inviter's calendar but not theirs.
        var invitedGatherings = await db.GatheringInvites.AsNoTracking()
            .Where(x => x.InviteeUserId == userId && x.Kind == GatheringInviteKind.DateRequest
                && x.Status == GatheringInviteStatus.Accepted)
            .Join(db.DateRequests.AsNoTracking(), i => i.TargetId, d => d.Id, (i, d) => d)
            .Where(d => d.Status != DateRequestStatus.Cancelled && d.Status != DateRequestStatus.Expired)
            .Select(d => new CalendarItemResponse("DateRequest", d.Id, d.Activity, d.StartsAt, d.EndsAt, null,
                d.LocationArea, null))
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

        var items = meetings.Concat(sessions).Concat(events).Concat(createdEvents).Concat(communityEvents)
            .Concat(mentorEvents).Concat(invitedGatherings)
            .Concat(counsellingMeetings).Concat(dateRequests)
            .GroupBy(x => new { x.Source, x.SourceId })
            .Select(x => x.First())
            .OrderBy(x => x.StartsAt)
            .ToList();

        return ApiResults.Ok(context, items, "Calendar retrieved successfully.");
    }
}
