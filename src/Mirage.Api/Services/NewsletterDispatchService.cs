using Microsoft.EntityFrameworkCore;
using Mirage.Application.Abstractions;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;
using Mirage.Infrastructure.Persistence;

namespace Mirage.Api.Services;

public sealed class NewsletterDispatchService(MirageDbContext db, IEmailService email, IConfiguration configuration,
    ILogger<NewsletterDispatchService> logger)
{
    private const int BatchSize = 25;

    public async Task RunAsync(CancellationToken ct)
    {
        await ClaimDueNewslettersAsync(ct);
        await SendNextBatchAsync(ct);
    }

    // Turns "Scheduled and the moment has arrived" into "Sending" plus one delivery row per subscriber.
    // The status guard inside ExecuteUpdate is the claim: a second instance sees 0 rows updated and skips.
    private async Task ClaimDueNewslettersAsync(CancellationToken ct)
    {
        var dueIds = await db.Newsletters.AsNoTracking()
            .Where(x => x.Status == NewsletterStatus.Scheduled && x.ScheduledFor <= DateTimeOffset.UtcNow)
            .OrderBy(x => x.ScheduledFor).Select(x => x.Id).Take(5).ToListAsync(ct);
        foreach (var id in dueIds)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var claimed = await db.Newsletters.Where(x => x.Id == id && x.Status == NewsletterStatus.Scheduled)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, NewsletterStatus.Sending), ct);
            if (claimed == 0) { await transaction.RollbackAsync(ct); continue; }

            // Re-resolved at send time against the edition's stored filter, so late subscribers are included and
            // anyone who unsubscribed after scheduling is not mailed.
            var audience = await db.Newsletters.AsNoTracking().Where(x => x.Id == id)
                .Select(x => new { x.AudienceSex, x.AudienceRelationshipStatuses }).SingleAsync(ct);
            var recipients = await NewsletterAudience
                .Filtered(db, audience.AudienceSex, audience.AudienceRelationshipStatuses)
                .Select(x => new { x.Id, Email = x.Email! }).ToListAsync(ct);
            db.NewsletterDeliveries.AddRange(recipients.Select(x => new NewsletterDelivery(id, x.Id, x.Email)));
            await db.Newsletters.Where(x => x.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RecipientCount, recipients.Count), ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            logger.LogInformation("Newsletter {NewsletterId} claimed for delivery to {RecipientCount} subscribers.", id, recipients.Count);
        }
    }

    // Sends one batch. The whole batch runs inside a transaction holding a Postgres advisory lock keyed on the
    // newsletter, so two API instances can never work the same pending rows and double-send to a subscriber.
    private async Task SendNextBatchAsync(CancellationToken ct)
    {
        var sendingId = await db.Newsletters.AsNoTracking().Where(x => x.Status == NewsletterStatus.Sending)
            .OrderBy(x => x.ScheduledFor).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        if (sendingId is null) return;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var lockKey = BitConverter.ToInt64(sendingId.Value.ToByteArray(), 0);
            var acquired = await db.Database.SqlQueryRaw<bool>(
                "SELECT pg_try_advisory_xact_lock({0}) AS \"Value\"", lockKey).SingleAsync(ct);
            if (!acquired) { await transaction.RollbackAsync(ct); return; }

            var sending = await db.Newsletters.SingleAsync(x => x.Id == sendingId.Value, ct);
            // Resolved once per batch: the byline is the same for every recipient of an edition.
            var author = await db.Profiles.AsNoTracking().Where(x => x.UserId == sending.AuthorUserId)
                .Select(x => new { x.DisplayName, x.AvatarUrl }).FirstOrDefaultAsync(ct);
            var pending = await db.NewsletterDeliveries
                .Where(x => x.NewsletterId == sending.Id && x.Status == NewsletterDeliveryStatus.Pending)
                .OrderBy(x => x.CreatedAt).Take(BatchSize).ToListAsync(ct);
            var appUrl = (configuration["Frontend:BaseUrl"] ?? "https://www.themiragehub.com").TrimEnd('/');
            var names = await db.Profiles.AsNoTracking().Where(x => pending.Select(p => p.UserId).Contains(x.UserId))
                .ToDictionaryAsync(x => x.UserId, x => x.DisplayName, ct);

            foreach (var delivery in pending)
            {
                var displayName = names.GetValueOrDefault(delivery.UserId) ?? "Friend";
                try
                {
                    var sent = await email.SendNewsletterAsync(delivery.Email, displayName, sending.Subject,
                        sending.Title, sending.Excerpt, sending.ContentHtml, sending.ImageUrls,
                        $"{appUrl}/newsletters/{sending.Id}",
                        NewsletterUnsubscribe.BuildUrl(appUrl, delivery.UserId, configuration),
                        author?.DisplayName, author?.AvatarUrl, sending.ThumbnailUrl, ct);
                    if (sent) delivery.MarkSent(); else delivery.MarkFailed("All configured email providers rejected the message.");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Newsletter delivery failed for NewsletterId {NewsletterId}, UserId {UserId}", sending.Id, delivery.UserId);
                    delivery.MarkFailed("Email provider error.");
                }
            }
            await db.SaveChangesAsync(ct);

            if (!await db.NewsletterDeliveries.AnyAsync(x => x.NewsletterId == sending.Id && x.Status == NewsletterDeliveryStatus.Pending, ct))
            {
                var sent = await db.NewsletterDeliveries.CountAsync(x => x.NewsletterId == sending.Id && x.Status == NewsletterDeliveryStatus.Sent, ct);
                var failed = await db.NewsletterDeliveries.CountAsync(x => x.NewsletterId == sending.Id && x.Status == NewsletterDeliveryStatus.Failed, ct);
                sending.Complete(sent, failed);
                await db.SaveChangesAsync(ct);
                logger.LogInformation("Newsletter {NewsletterId} completed: {Sent} delivered, {Failed} failed.", sending.Id, sent, failed);
            }
            await transaction.CommitAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Newsletter dispatch run failed for NewsletterId {NewsletterId}", sendingId);
            await transaction.RollbackAsync(CancellationToken.None);
        }
    }
}

public sealed class NewsletterDispatchWorker(IServiceScopeFactory scopes, ILogger<NewsletterDispatchWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        do
        {
            try { using var scope = scopes.CreateScope(); await scope.ServiceProvider.GetRequiredService<NewsletterDispatchService>().RunAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Newsletter scheduler iteration failed."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
