using Microsoft.EntityFrameworkCore;
using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;

namespace Mirage.Api.Services;

public sealed class CalendarReminderService(IMirageDbContext db, NotificationService notifications,
    IConfiguration configuration, ILogger<CalendarReminderService> logger)
{
    private sealed record DueItem(string Source, Guid SourceId, string Title, DateTimeOffset StartsAt,
        IReadOnlyCollection<Guid> UserIds);

    public async Task RunAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var horizon = now.AddHours(24);
        var items = new List<DueItem>();

        var mentorMeetings = await db.MentorMeetings.AsNoTracking()
            .Where(x => x.ScheduledAt > now && x.ScheduledAt <= horizon)
            .Select(x => new { x.Id, x.Title, x.ScheduledAt, x.MentorProfileId, x.MentorRequestId, x.Audience })
            .ToListAsync(ct);
        foreach (var meeting in mentorMeetings)
        {
            var mentorId = await db.Mentors.AsNoTracking().Where(x => x.Id == meeting.MentorProfileId)
                .Select(x => x.UserId).SingleAsync(ct);
            var memberIds = meeting.MentorRequestId.HasValue
                ? await db.MentorRequests.AsNoTracking().Where(x => x.Id == meeting.MentorRequestId.Value)
                    .Select(x => x.MenteeUserId).ToListAsync(ct)
                // A group meeting is held for one of the mentor's two groups, so only that group
                // is reminded — reminding the other about a call they cannot join is worse than
                // saying nothing.
                : await MenteesInAudienceAsync(meeting.MentorProfileId, meeting.Audience, ct);
            items.Add(new("MentorMeeting", meeting.Id, meeting.Title, meeting.ScheduledAt,
                memberIds.Append(mentorId).Distinct().ToArray()));
        }

        var counselling = await db.CounsellingMeetings.AsNoTracking()
            .Where(x => x.ScheduledAt > now && x.ScheduledAt <= horizon)
            .Join(db.CounsellingSessions.AsNoTracking(), x => x.SessionId, s => s.Id,
                (x, s) => new { x.Id, x.Title, x.ScheduledAt, s.ClientUserId, CounsellorUserId = s.Counsellor.UserId,
                    s.PartnerUserId, s.PartnerAccepted }).ToListAsync(ct);
        items.AddRange(counselling.Select(x => new DueItem("CounsellingMeeting", x.Id, x.Title, x.ScheduledAt,
            new Guid?[] { x.ClientUserId, x.CounsellorUserId, x.PartnerAccepted ? x.PartnerUserId : null }
                .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray())));

        var sessions = await db.CounsellingSessions.AsNoTracking()
            .Where(x => x.ScheduledAt > now && x.ScheduledAt <= horizon && x.Status != SessionStatus.Cancelled &&
                x.Status != SessionStatus.Declined)
            .Select(x => new { x.Id, x.Topic, x.ScheduledAt, x.ClientUserId,
                CounsellorUserId = x.Counsellor.UserId, x.PartnerUserId, x.PartnerAccepted }).ToListAsync(ct);
        items.AddRange(sessions.Select(x => new DueItem("CounsellingSession", x.Id, x.Topic, x.ScheduledAt,
            new Guid?[] { x.ClientUserId, x.CounsellorUserId, x.PartnerAccepted ? x.PartnerUserId : null }
                .Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToArray())));

        var dates = await db.DateRequests.AsNoTracking()
            .Where(x => x.StartsAt > now && x.StartsAt <= horizon && x.Status != DateRequestStatus.Cancelled &&
                x.Status != DateRequestStatus.Expired)
            .Select(x => new { x.Id, x.Activity, x.StartsAt, x.RequestorUserId }).ToListAsync(ct);
        foreach (var date in dates)
        {
            var guests = await db.DateRequestAcceptances.AsNoTracking()
                .Where(x => x.DateRequestId == date.Id && x.Status != DateAcceptanceStatus.Declined &&
                    x.Status != DateAcceptanceStatus.Withdrawn).Select(x => x.AcceptorUserId).ToListAsync(ct);
            items.Add(new("DateRequest", date.Id, date.Activity, date.StartsAt,
                guests.Append(date.RequestorUserId).Distinct().ToArray()));
        }

        var events = await db.OrgEvents.AsNoTracking().Where(x => x.StartsAt > now && x.StartsAt <= horizon).ToListAsync(ct);
        foreach (var evt in events)
        {
            // Everyone holding a ticket, plus the host's own people: an approved member of the
            // church that is hosting, or — for a mentor's event — the group it was addressed to.
            // The organisation branch cannot serve a mentor's event, whose OrganisationId is null.
            var audience = await db.EventTickets.AsNoTracking()
                .Where(x => x.EventId == evt.Id).Select(x => x.UserId).ToListAsync(ct);

            if (evt.OrganisationId is { } organisationId)
                audience.AddRange(await db.OrganisationMembers.AsNoTracking()
                    .Where(x => x.OrganisationId == organisationId &&
                        x.Status == OrganisationMemberStatus.Approved)
                    .Select(x => x.UserId).ToListAsync(ct));
            else if (evt.MentorProfileId is { } mentorProfileId)
                audience.AddRange(await MenteesInAudienceAsync(mentorProfileId, evt.Audience, ct));

            items.Add(new("OrgEvent", evt.Id, evt.Title, evt.StartsAt,
                audience.Append(evt.CreatedByUserId).Distinct().ToArray()));
        }

        foreach (var item in items)
        foreach (var lead in item.StartsAt <= now.AddMinutes(15)
                     ? new[] { CalendarReminderLeadTime.OneDay, CalendarReminderLeadTime.FifteenMinutes }
                     : new[] { CalendarReminderLeadTime.OneDay })
            await SendOnce(item, lead, ct);
    }

    /// <summary>
    /// The accepted mentees of one of a mentor's two groups, or of both when the audience is
    /// Everyone. Reminders follow the same split the meetings and posts do.
    /// </summary>
    private async Task<List<Guid>> MenteesInAudienceAsync(Guid mentorProfileId, MentorAudience audience,
        CancellationToken ct)
    {
        var query = db.MentorRequests.AsNoTracking()
            .Where(x => x.MentorProfileId == mentorProfileId && x.Status == MentorRequestStatus.Accepted);
        if (audience == MentorAudience.FreeMentees) query = query.Where(x => x.Tier == MentorshipTier.Free);
        else if (audience == MentorAudience.PaidMentees) query = query.Where(x => x.Tier == MentorshipTier.Paid);
        return await query.Select(x => x.MenteeUserId).ToListAsync(ct);
    }

    private async Task SendOnce(DueItem item, CalendarReminderLeadTime lead, CancellationToken ct)
    {
        var frontend = configuration["Frontend:BaseUrl"] ?? "https://mirage-ui-iota.vercel.app";
        foreach (var userId in item.UserIds)
        {
            if (await db.CalendarReminderDeliveries.AnyAsync(x => x.Source == item.Source && x.SourceId == item.SourceId &&
                    x.UserId == userId && x.LeadTime == lead, ct)) continue;
            db.CalendarReminderDeliveries.Add(new CalendarReminderDelivery(item.Source, item.SourceId, userId, lead));
            try
            {
                await db.SaveChangesAsync(ct);
                // "Within 24 hours" reads as wrong on a meeting that is 40 minutes away, which is
                // exactly when the day-ahead reminder fires for anything booked at short notice.
                var minutesAway = (item.StartsAt - DateTimeOffset.UtcNow).TotalMinutes;
                var when = lead == CalendarReminderLeadTime.FifteenMinutes
                    ? "in 15 minutes"
                    : minutesAway <= 90 ? "shortly" : "within 24 hours";
                await notifications.NotifyAsync(userId, NotificationType.CalendarReminder,
                    $"{item.Title} starts {when}", $"Scheduled for {item.StartsAt:ddd, MMM d 'at' h:mm tt}.",
                    item.SourceId, item.Source, ct, $"{frontend}/calendar", "Open calendar");
            }
            catch (DbUpdateException exception)
            {
                logger.LogDebug(exception, "Calendar reminder was already claimed by another worker instance.");
            }
        }
    }
}

public sealed class CalendarReminderWorker(IServiceScopeFactory scopeFactory,
    ILogger<CalendarReminderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<CalendarReminderService>().RunAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Calendar reminder worker run failed.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
