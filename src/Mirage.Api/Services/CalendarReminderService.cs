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
            .Select(x => new { x.Id, x.Title, x.ScheduledAt, x.MentorProfileId, x.MentorRequestId }).ToListAsync(ct);
        foreach (var meeting in mentorMeetings)
        {
            var mentorId = await db.Mentors.AsNoTracking().Where(x => x.Id == meeting.MentorProfileId)
                .Select(x => x.UserId).SingleAsync(ct);
            var memberIds = meeting.MentorRequestId.HasValue
                ? await db.MentorRequests.AsNoTracking().Where(x => x.Id == meeting.MentorRequestId.Value)
                    .Select(x => x.MenteeUserId).ToListAsync(ct)
                : await db.MentorRequests.AsNoTracking().Where(x => x.MentorProfileId == meeting.MentorProfileId &&
                      x.Status == MentorRequestStatus.Accepted).Select(x => x.MenteeUserId).ToListAsync(ct);
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
            var audience = await db.EventTickets.AsNoTracking().Where(x => x.EventId == evt.Id).Select(x => x.UserId)
                .Concat(db.OrganisationMembers.AsNoTracking().Where(x => x.OrganisationId == evt.OrganisationId &&
                    x.Status == OrganisationMemberStatus.Approved).Select(x => x.UserId))
                .ToListAsync(ct);
            items.Add(new("OrgEvent", evt.Id, evt.Title, evt.StartsAt,
                audience.Append(evt.CreatedByUserId).Distinct().ToArray()));
        }

        foreach (var item in items)
        foreach (var lead in item.StartsAt <= now.AddMinutes(15)
                     ? new[] { CalendarReminderLeadTime.OneDay, CalendarReminderLeadTime.FifteenMinutes }
                     : new[] { CalendarReminderLeadTime.OneDay })
            await SendOnce(item, lead, ct);
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
                var when = lead == CalendarReminderLeadTime.FifteenMinutes ? "in 15 minutes" : "within 24 hours";
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
