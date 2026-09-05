using Microsoft.EntityFrameworkCore;
using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;
using Mirage.Infrastructure.Persistence;

namespace Mirage.Api.Services;

/// <summary>
/// Who a broadcast reaches. Resolved at dispatch time rather than at scheduling time, so a mentee
/// who joined after the broadcast was written still hears it, and one who left does not.
/// </summary>
public static class BroadcastAudience
{
    // Same rule as CounsellorGroupEndpoints: a declined or cancelled session is not a working
    // relationship, so it puts nobody in the group.
    private static readonly SessionStatus[] LiveSessionStatuses =
    [
        SessionStatus.Requested, SessionStatus.Scheduled, SessionStatus.InProgress,
        SessionStatus.Completed, SessionStatus.AwaitingPayment,
    ];

    public static Task<List<Guid>> MentorRecipientsAsync(IMirageDbContext db, Guid mentorProfileId,
        MentorAudience audience, CancellationToken cancellationToken)
    {
        var query = db.MentorRequests.AsNoTracking()
            .Where(x => x.MentorProfileId == mentorProfileId && x.Status == MentorRequestStatus.Accepted);
        if (audience == MentorAudience.FreeMentees) query = query.Where(x => x.Tier == MentorshipTier.Free);
        else if (audience == MentorAudience.PaidMentees) query = query.Where(x => x.Tier == MentorshipTier.Paid);
        return query.Select(x => x.MenteeUserId).Distinct().ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Every client with a live session, plus the spouse who accepted it — the couple is what is
    /// being counselled, so both belong in the group.
    /// </summary>
    public static async Task<List<Guid>> CounsellorRecipientsAsync(IMirageDbContext db, Guid counsellorProfileId,
        CancellationToken cancellationToken)
    {
        var rows = await db.CounsellingSessions.AsNoTracking()
            .Where(x => x.CounsellorId == counsellorProfileId && LiveSessionStatuses.Contains(x.Status))
            .Select(x => new { x.ClientUserId, x.PartnerUserId, x.PartnerAccepted })
            .ToListAsync(cancellationToken);

        return rows
            .SelectMany(x => x.PartnerAccepted && x.PartnerUserId is { } partner
                ? new[] { x.ClientUserId, partner }
                : [x.ClientUserId])
            .Distinct()
            .ToList();
    }

    public static Task<List<Guid>> ForAsync(IMirageDbContext db, ProfessionalBroadcast broadcast,
        CancellationToken cancellationToken) =>
        broadcast.MentorProfileId is { } mentorId
            ? MentorRecipientsAsync(db, mentorId, broadcast.Audience, cancellationToken)
            : CounsellorRecipientsAsync(db, broadcast.CounsellorProfileId!.Value, cancellationToken);
}

/// <summary>
/// Turns a due <see cref="ProfessionalBroadcast"/> into the thing its audience actually sees — a
/// group post, or a private event — and notifies them.
/// </summary>
public sealed class BroadcastDispatchService(MirageDbContext db, NotificationService notifications,
    ILogger<BroadcastDispatchService> logger)
{
    private const int BatchSize = 25;

    /// <summary>Dispatches every broadcast whose moment has arrived. Safe to re-run.</summary>
    public async Task RunDueAsync(CancellationToken cancellationToken)
    {
        var dueIds = await db.ProfessionalBroadcasts.AsNoTracking()
            .Where(x => x.Status == BroadcastStatus.Scheduled && x.ScheduledFor <= DateTimeOffset.UtcNow)
            .OrderBy(x => x.ScheduledFor)
            .Select(x => x.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var id in dueIds) await DispatchAsync(id, cancellationToken);
    }

    /// <summary>
    /// Dispatches one broadcast. The status guard in the claim is what stops two API instances
    /// posting the same broadcast twice: the second sees zero rows updated and gives up.
    /// </summary>
    public async Task DispatchAsync(Guid broadcastId, CancellationToken cancellationToken)
    {
        // Retries replay the whole delegate, so the tracker starts clean on every attempt —
        // anything a failed attempt left behind would otherwise be written a second time.
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            var claimed = await db.ProfessionalBroadcasts
                .Where(x => x.Id == broadcastId && x.Status == BroadcastStatus.Scheduled)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, BroadcastStatus.Sent), cancellationToken);
            if (claimed == 0) return;

            var broadcast = await db.ProfessionalBroadcasts
                .SingleOrDefaultAsync(x => x.Id == broadcastId, cancellationToken);
            if (broadcast is null) return;

            try
            {
                await PublishAsync(broadcast, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Broadcast {BroadcastId} failed to dispatch.", broadcastId);
                // The claim already flipped the row to Sent, so the failure has to be written back
                // over it — otherwise a broadcast nobody received would sit on the page claiming
                // it went out.
                db.ChangeTracker.Clear();
                var reason = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
                await db.ProfessionalBroadcasts.Where(x => x.Id == broadcastId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(x => x.Status, BroadcastStatus.Failed)
                        .SetProperty(x => x.FailureReason, reason),
                        cancellationToken);
            }
        });
    }

    private async Task PublishAsync(ProfessionalBroadcast broadcast, CancellationToken cancellationToken)
    {
        var recipients = (await BroadcastAudience.ForAsync(db, broadcast, cancellationToken))
            .Where(x => x != broadcast.AuthorUserId)
            .ToList();

        var authorName = await db.Profiles.AsNoTracking()
            .Where(x => x.UserId == broadcast.AuthorUserId)
            .Select(x => x.DisplayName)
            .SingleOrDefaultAsync(cancellationToken)
            ?? (broadcast.MentorProfileId is not null ? "Your mentor" : "Your counsellor");

        var published = broadcast.Kind == BroadcastKind.Event
            ? await PublishEventAsync(broadcast, authorName, recipients, cancellationToken)
            : await PublishMessageAsync(broadcast, authorName, recipients, cancellationToken);

        broadcast.MarkSent(published, recipients.Count);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Broadcast {BroadcastId} delivered to {RecipientCount} members.",
            broadcast.Id, recipients.Count);
    }

    // A message broadcast becomes an ordinary group post, so it lands in the same feed the group
    // already reads rather than in a parallel place only broadcasts appear.
    private async Task<Guid> PublishMessageAsync(ProfessionalBroadcast broadcast, string authorName,
        List<Guid> recipients, CancellationToken cancellationToken)
    {
        Guid postId;
        if (broadcast.MentorProfileId is { } mentorId)
        {
            var post = new MentorPost(mentorId, broadcast.Content, broadcast.ImageUrl, broadcast.Audience);
            db.MentorPosts.Add(post);
            postId = post.Id;
        }
        else
        {
            var post = new CounsellorPost(broadcast.CounsellorProfileId!.Value, broadcast.Content, broadcast.ImageUrl);
            db.CounsellorPosts.Add(post);
            postId = post.Id;
        }
        await db.SaveChangesAsync(cancellationToken);

        var preview = broadcast.Content.Length > 120
            ? broadcast.Content[..120].TrimEnd() + "…"
            : broadcast.Content;
        var referenceType = broadcast.MentorProfileId is not null ? "MentorProfile" : "CounsellorProfile";
        var referenceId = broadcast.MentorProfileId ?? broadcast.CounsellorProfileId!.Value;
        foreach (var recipient in recipients)
            await notifications.NotifyAsync(recipient, NotificationType.MentorGroupPost,
                $"{authorName} posted to your group", preview, referenceId, referenceType, cancellationToken);

        return postId;
    }

    // A private event is a real OrgEvent so it can be registered for and shows on a member's
    // calendar — it is simply kept off the public feed (OrgEvent.IsPrivate).
    private async Task<Guid> PublishEventAsync(ProfessionalBroadcast broadcast, string authorName,
        List<Guid> recipients, CancellationToken cancellationToken)
    {
        var orgEvent = broadcast.MentorProfileId is { } mentorId
            ? OrgEvent.ForMentor(mentorId, broadcast.AuthorUserId, broadcast.Title!, broadcast.Content,
                broadcast.ImageUrl, broadcast.StartsAt!.Value, broadcast.EndsAt!.Value, broadcast.Location!,
                broadcast.Capacity, broadcast.Audience, isPrivate: true)
            : OrgEvent.ForCounsellor(broadcast.CounsellorProfileId!.Value, broadcast.AuthorUserId,
                broadcast.Title!, broadcast.Content, broadcast.ImageUrl, broadcast.StartsAt!.Value,
                broadcast.EndsAt!.Value, broadcast.Location!, broadcast.Capacity);
        db.OrgEvents.Add(orgEvent);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var recipient in recipients)
            await notifications.NotifyAsync(recipient, NotificationType.PrivateEventPublished,
                $"{authorName} invited you to an event",
                $"{broadcast.Title} — {broadcast.StartsAt:MMM d, h:mm tt} at {broadcast.Location}.",
                orgEvent.Id, "OrgEvent", cancellationToken, $"/events/{orgEvent.Id}");

        return orgEvent.Id;
    }
}
