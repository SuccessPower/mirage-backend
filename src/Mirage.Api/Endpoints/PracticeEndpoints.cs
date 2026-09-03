using Microsoft.EntityFrameworkCore;
using Mirage.Api.Contracts;
using Mirage.Api.Security;
using Mirage.Application.Abstractions;
using Mirage.Application.Common;
using Mirage.Domain.Enums;

namespace Mirage.Api.Endpoints;

// Mentorship and counselling are different practices with different obligations, so each gets its
// own endpoint and its own page. Someone who holds both roles has two caseloads, not one merged
// list; nothing here reads across the two.
internal static class PracticeEndpoints
{
    public static RouteGroupBuilder MapPracticeEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/practice").WithTags("Practice").RequireAuthorization();
        group.MapGet("/mentorship", GetMentorshipPractice);
        group.MapGet("/counselling", GetCounsellingPractice);
        group.MapGet("/counselling/sessions", ListCounsellingSessions);
        return group;
    }

    private static readonly SessionStatus[] RequestedStatuses =
        [SessionStatus.Requested, SessionStatus.AwaitingPayment];

    private static readonly SessionStatus[] OngoingStatuses =
        [SessionStatus.Scheduled, SessionStatus.InProgress];

    private static readonly SessionStatus[] ClosedStatuses =
        [SessionStatus.Cancelled, SessionStatus.Declined];

    private static SessionStatus[] StatusesForBucket(string? bucket) => bucket?.ToLowerInvariant() switch
    {
        "ongoing" => OngoingStatuses,
        "completed" => [SessionStatus.Completed],
        "closed" => ClosedStatuses,
        _ => RequestedStatuses
    };

    // ------------------------------------------------------------------ mentorship

    private static async Task<IResult> GetMentorshipPractice(HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var mentorProfileId = await db.Mentors.AsNoTracking().Where(x => x.UserId == userId)
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(cancellationToken);
        if (mentorProfileId is null)
            return EndpointHelpers.Forbidden(context, "Only mentors have a mentorship practice.");

        var accepted = await db.MentorRequests.AsNoTracking()
            .Where(x => x.MentorProfileId == mentorProfileId && x.Status == MentorRequestStatus.Accepted)
            .Select(x => new { x.Id, x.MenteeUserId, Since = x.UpdatedAt })
            .ToListAsync(cancellationToken);

        var menteeIds = accepted.Select(x => x.MenteeUserId).Distinct().ToList();
        var profiles = await db.Profiles.AsNoTracking()
            .Where(p => menteeIds.Contains(p.UserId))
            .Select(p => new { p.UserId, p.DisplayName, p.AvatarUrl, p.RelationshipStatus, p.City, p.Country })
            .ToDictionaryAsync(p => p.UserId, cancellationToken);
        var badges = await db.GetOrgBadgesAsync(menteeIds, cancellationToken);
        var partners = await PartnersAsync(menteeIds, db, cancellationToken);

        var mentees = accepted
            .Select(x =>
            {
                profiles.TryGetValue(x.MenteeUserId, out var profile);
                return new PracticePersonResponse(
                    x.MenteeUserId,
                    profile?.DisplayName ?? "Unknown",
                    profile?.AvatarUrl,
                    profile?.RelationshipStatus,
                    profile?.City ?? string.Empty,
                    profile?.Country ?? string.Empty,
                    x.Since,
                    x.Id,
                    null,
                    0,
                    false,
                    partners.GetValueOrDefault(x.MenteeUserId),
                    badges.GetValueOrDefault(x.MenteeUserId)?.LogoUrl,
                    badges.GetValueOrDefault(x.MenteeUserId)?.OrganisationName);
            })
            .OrderByDescending(x => x.Since)
            .ToList();

        var pending = await db.MentorRequests.AsNoTracking()
            .Where(x => x.MentorProfileId == mentorProfileId && x.Status == MentorRequestStatus.Pending)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new PracticeRequestResponse(
                x.Id,
                x.MenteeUserId,
                db.Profiles.Where(p => p.UserId == x.MenteeUserId).Select(p => p.DisplayName).SingleOrDefault()
                    ?? "Unknown",
                db.Profiles.Where(p => p.UserId == x.MenteeUserId).Select(p => p.AvatarUrl).SingleOrDefault(),
                db.Profiles.Where(p => p.UserId == x.MenteeUserId)
                    .Select(p => (RelationshipStatus?)p.RelationshipStatus).SingleOrDefault(),
                x.Message,
                x.CreatedAt))
            .ToListAsync(cancellationToken);

        // Mentors run calls and video meetings with their group the same way counsellors run
        // sessions, so those are this practice's activity.
        var now = DateTimeOffset.UtcNow;
        var meetings = await db.MentorMeetings.AsNoTracking()
            .Where(x => x.MentorProfileId == mentorProfileId && x.MentorRequestId == null)
            .OrderBy(x => x.ScheduledAt)
            .Select(x => new PracticeMeetingResponse(x.Id, x.Title, x.MeetingLink, x.ScheduledAt,
                x.DurationMinutes, x.ScheduledAt < now))
            .ToListAsync(cancellationToken);
        var upcoming = meetings.Where(x => !x.IsPast).ToList();
        var past = meetings.Where(x => x.IsPast).OrderByDescending(x => x.ScheduledAt).ToList();

        var counts = new MentorshipCountsResponse(
            mentees.Count,
            pending.Count,
            CountStatus(mentees, RelationshipStatus.Single),
            CountStatus(mentees, RelationshipStatus.Married),
            mentees.Count(x => x.Partner is not null),
            mentees.Count(x => x.RelationshipStatus is not (RelationshipStatus.Single or RelationshipStatus.Married)),
            upcoming.Count,
            past.Count);

        return ApiResults.Ok(context,
            new MentorshipPracticeResponse(mentorProfileId.Value, counts, mentees, pending, upcoming, past),
            "Mentorship practice retrieved successfully.");
    }

    // ----------------------------------------------------------------- counselling

    private static async Task<IResult> GetCounsellingPractice(HttpContext context, IMirageDbContext db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.GetUserId();
        var counsellorProfileId = await db.Counsellors.AsNoTracking().Where(x => x.UserId == userId)
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(cancellationToken);
        if (counsellorProfileId is null)
            return EndpointHelpers.Forbidden(context, "Only counsellors have a counselling practice.");

        var sessions = await db.CounsellingSessions.AsNoTracking()
            .Where(x => x.CounsellorId == counsellorProfileId)
            .Select(x => new { x.Id, x.ClientUserId, x.ClientAnonymous, x.Status, x.ScheduledAt, x.CreatedAt })
            .ToListAsync(cancellationToken);

        var clientIds = sessions.Select(x => x.ClientUserId).Distinct().ToList();
        var profiles = await db.Profiles.AsNoTracking()
            .Where(p => clientIds.Contains(p.UserId))
            .Select(p => new { p.UserId, p.DisplayName, p.AvatarUrl, p.RelationshipStatus, p.City, p.Country })
            .ToDictionaryAsync(p => p.UserId, cancellationToken);
        var badges = await db.GetOrgBadgesAsync(clientIds, cancellationToken);
        var partners = await PartnersAsync(clientIds, db, cancellationToken);

        var clients = clientIds
            .Select(clientId =>
            {
                profiles.TryGetValue(clientId, out var profile);
                var theirs = sessions.Where(x => x.ClientUserId == clientId).ToList();

                // A client who booked anonymously stays anonymous on this page too. Relationship
                // status is deliberately still shown: it is context for the session, not an
                // identity, and counselling a married person differs from counselling a single one.
                var anonymous = theirs.All(x => x.ClientAnonymous);

                return new PracticePersonResponse(
                    clientId,
                    anonymous ? "Anonymous client" : profile?.DisplayName ?? "Unknown",
                    anonymous ? null : profile?.AvatarUrl,
                    profile?.RelationshipStatus,
                    anonymous ? string.Empty : profile?.City ?? string.Empty,
                    anonymous ? string.Empty : profile?.Country ?? string.Empty,
                    theirs.Min(x => x.CreatedAt),
                    null,
                    theirs.OrderByDescending(x => x.ScheduledAt).Select(x => (Guid?)x.Id).First(),
                    theirs.Count,
                    anonymous,
                    // An anonymous client's spouse would identify them, so the couple is withheld
                    // alongside the name and avatar.
                    anonymous ? null : partners.GetValueOrDefault(clientId),
                    anonymous ? null : badges.GetValueOrDefault(clientId)?.LogoUrl,
                    anonymous ? null : badges.GetValueOrDefault(clientId)?.OrganisationName);
            })
            .OrderByDescending(x => x.Since)
            .ToList();

        var requested = await SessionsAsync(counsellorProfileId.Value, RequestedStatuses, db, cancellationToken);
        var ongoing = await SessionsAsync(counsellorProfileId.Value, OngoingStatuses, db, cancellationToken);

        var counts = new CounsellingCountsResponse(
            clients.Count,
            requested.Count,
            ongoing.Count,
            sessions.Count(x => x.Status == SessionStatus.Completed),
            CountStatus(clients, RelationshipStatus.Single),
            CountStatus(clients, RelationshipStatus.Married),
            clients.Count(x => x.Partner is not null),
            clients.Count(x => x.RelationshipStatus is not (RelationshipStatus.Single or RelationshipStatus.Married)));

        return ApiResults.Ok(context,
            new CounsellingPracticeResponse(counsellorProfileId.Value, counts, clients, requested, ongoing),
            "Counselling practice retrieved successfully.");
    }

    // Completed sessions are the one unbounded list, so they page rather than riding along with
    // the counselling practice payload.
    private static async Task<IResult> ListCounsellingSessions(HttpContext context, IMirageDbContext db,
        string? bucket = null, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var userId = context.User.GetUserId();
        var counsellorProfileId = await db.Counsellors.AsNoTracking().Where(x => x.UserId == userId)
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(cancellationToken);
        if (counsellorProfileId is null)
            return EndpointHelpers.Forbidden(context, "Only counsellors have a counselling practice.");

        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 100 ? 20 : pageSize;
        var statuses = StatusesForBucket(bucket);

        var query = db.CounsellingSessions.AsNoTracking()
            .Where(x => x.CounsellorId == counsellorProfileId && statuses.Contains(x.Status));
        var total = await query.CountAsync(cancellationToken);
        var rows = await ProjectSessions(query, db)
            .OrderByDescending(x => x.ScheduledAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return ApiResults.Ok(context,
            new PagedResult<PracticeSessionResponse>(rows, page, pageSize, total),
            "Sessions retrieved successfully.");
    }

    // ---------------------------------------------------------------------- shared

    private static int CountStatus(List<PracticePersonResponse> people, RelationshipStatus status) =>
        people.Count(x => x.RelationshipStatus == status);

    // A practitioner works with the marriage, not just the individual, so a roster person who is
    // in an approved couple is shown with their spouse. Looked up once for the whole roster
    // rather than per person.
    private static async Task<Dictionary<Guid, PracticePartnerResponse>> PartnersAsync(
        List<Guid> personIds, IMirageDbContext db, CancellationToken cancellationToken)
    {
        if (personIds.Count == 0) return [];

        var couples = await db.Couples.AsNoTracking()
            .Where(c => c.Status == CoupleStatus.Approved
                && (personIds.Contains(c.User1Id) || personIds.Contains(c.User2Id)))
            .Select(c => new { c.Id, c.User1Id, c.User2Id })
            .ToListAsync(cancellationToken);
        if (couples.Count == 0) return [];

        var partnerIds = couples.SelectMany(c => new[] { c.User1Id, c.User2Id }).Distinct().ToList();
        var partnerProfiles = await db.Profiles.AsNoTracking()
            .Where(p => partnerIds.Contains(p.UserId))
            .Select(p => new { p.UserId, p.DisplayName, p.AvatarUrl })
            .ToDictionaryAsync(p => p.UserId, cancellationToken);

        var byPerson = new Dictionary<Guid, PracticePartnerResponse>();
        foreach (var couple in couples)
        {
            foreach (var (personId, partnerId) in new[]
                     { (couple.User1Id, couple.User2Id), (couple.User2Id, couple.User1Id) })
            {
                if (!personIds.Contains(personId) || byPerson.ContainsKey(personId)) continue;
                partnerProfiles.TryGetValue(partnerId, out var partnerProfile);
                byPerson[personId] = new PracticePartnerResponse(
                    partnerId, partnerProfile?.DisplayName ?? "Their spouse",
                    partnerProfile?.AvatarUrl, couple.Id);
            }
        }
        return byPerson;
    }

    private static async Task<List<PracticeSessionResponse>> SessionsAsync(Guid counsellorProfileId,
        SessionStatus[] statuses, IMirageDbContext db, CancellationToken cancellationToken) =>
        await ProjectSessions(
                db.CounsellingSessions.AsNoTracking()
                    .Where(x => x.CounsellorId == counsellorProfileId && statuses.Contains(x.Status)), db)
            .OrderBy(x => x.ScheduledAt)
            .ToListAsync(cancellationToken);

    private static IQueryable<PracticeSessionResponse> ProjectSessions(
        IQueryable<Domain.Entities.CounsellingSession> query, IMirageDbContext db) =>
        query.Select(x => new PracticeSessionResponse(
            x.Id,
            x.ClientUserId,
            x.ClientAnonymous
                ? "Anonymous client"
                : db.Profiles.Where(p => p.UserId == x.ClientUserId).Select(p => p.DisplayName).SingleOrDefault()
                  ?? "Client",
            x.ClientAnonymous
                ? null
                : db.Profiles.Where(p => p.UserId == x.ClientUserId).Select(p => p.AvatarUrl).SingleOrDefault(),
            db.Profiles.Where(p => p.UserId == x.ClientUserId)
                .Select(p => (RelationshipStatus?)p.RelationshipStatus).SingleOrDefault(),
            x.Type,
            x.Status,
            x.ScheduledAt,
            x.Topic,
            x.ClientAnonymous,
            x.PartnerUserId,
            x.PartnerAccepted,
            x.CreatedAt));
}
