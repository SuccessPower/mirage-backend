using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mirage.Application.Abstractions;
using Mirage.Infrastructure.Persistence;

namespace Mirage.Api.Services;

// Sends the welcome email to any user who signed up before the email pipeline existed (or whose
// real-time send at registration failed) and records WelcomeEmailSentAt so they're never emailed
// twice. Driven by WelcomeEmailBackfillWorker on a timer, and by the admin "backfill" endpoint
// for an on-demand run.
public sealed class WelcomeEmailBackfillService(MirageDbContext db, IEmailService email,
    ILogger<WelcomeEmailBackfillService> logger)
{
    public async Task<int> RunBatchAsync(int batchSize, CancellationToken cancellationToken)
    {
        var users = await db.Users
            .Where(x => x.WelcomeEmailSentAt == null && x.Email != null)
            .OrderBy(x => x.CreatedAt)
            .Take(batchSize)
            .Select(x => new { x.Id, x.Email })
            .ToListAsync(cancellationToken);

        if (users.Count == 0) return 0;

        var sentCount = 0;
        foreach (var user in users)
        {
            var displayName = await db.Profiles.AsNoTracking()
                .Where(p => p.UserId == user.Id)
                .Select(p => p.DisplayName)
                .FirstOrDefaultAsync(cancellationToken) ?? "there";

            var sent = await email.SendWelcomeEmailAsync(user.Email!, displayName, cancellationToken);
            if (!sent)
            {
                logger.LogWarning("Welcome email backfill: send failed for UserId {UserId} — will retry next run.", user.Id);
                continue;
            }

            await db.Users.Where(x => x.Id == user.Id)
                .ExecuteUpdateAsync(x => x.SetProperty(u => u.WelcomeEmailSentAt, DateTimeOffset.UtcNow), cancellationToken);
            sentCount++;
        }

        logger.LogInformation("Welcome email backfill: sent {SentCount}/{TotalCount} in this batch.", sentCount, users.Count);
        return sentCount;
    }
}
