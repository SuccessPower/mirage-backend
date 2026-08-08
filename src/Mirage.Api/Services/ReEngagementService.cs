using Microsoft.EntityFrameworkCore;
using Mirage.Application.Abstractions;
using Mirage.Domain.Enums;
using Mirage.Infrastructure.Persistence;

namespace Mirage.Api.Services;

public sealed class ReEngagementService(MirageDbContext db, IEmailService email,
    IConfiguration configuration, ILogger<ReEngagementService> logger)
{
    public async Task RunBatchAsync(int batchSize, CancellationToken cancellationToken)
    {
        var enabled = configuration.GetValue("ReEngagement:Enabled", true);
        if (!enabled) return;

        var now = DateTimeOffset.UtcNow;
        var inactiveDays = Math.Max(2, configuration.GetValue("ReEngagement:InactiveDays", 2));
        var repeatDays = Math.Max(1, configuration.GetValue("ReEngagement:RepeatDays", 7));
        var maxEmails = Math.Max(1, configuration.GetValue("ReEngagement:MaxEmailsPerInactivityPeriod", 3));
        var inactiveCutoff = now.AddDays(-inactiveDays);
        var repeatCutoff = now.AddDays(-repeatDays);

        var candidates = await db.Users
            .Where(user => user.IsActive && !user.IsDeleted && user.Email != null
                && user.LastLoginAt != null && user.LastLoginAt <= inactiveCutoff
                && user.ReEngagementEmailCount < maxEmails
                && (user.LastReEngagementEmailAt == null || user.LastReEngagementEmailAt <= repeatCutoff))
            .OrderBy(user => user.LastReEngagementEmailAt ?? user.LastLoginAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0) return;

        var userIds = candidates.Select(user => user.Id).ToArray();
        var profiles = await db.Profiles.AsNoTracking()
            .Where(profile => userIds.Contains(profile.UserId))
            .Select(profile => new { profile.UserId, profile.DisplayName, profile.RelationshipStatus })
            .ToDictionaryAsync(profile => profile.UserId, cancellationToken);
        var appUrl = (configuration["Frontend:BaseUrl"] ?? "https://www.themiragehub.com").TrimEnd('/');

        foreach (var user in candidates)
        {
            if (!profiles.TryGetValue(user.Id, out var profile)) continue;
            try
            {
                var highlights = BuildHighlights(appUrl, profile.RelationshipStatus == RelationshipStatus.Married);
                var title = user.ReEngagementEmailCount == 0 ? "See what's new on Mirage" : "There's more waiting for you on Mirage";
                var sent = await email.SendReEngagementEmailAsync(user.Email!, profile.DisplayName, title,
                    "It has been a little while. Come back and explore new stories, conversations and events.",
                    appUrl, highlights, cancellationToken);
                if (!sent)
                {
                    logger.LogWarning("Re-engagement email delivery failed for user {UserId}.", user.Id);
                    continue;
                }

                user.LastReEngagementEmailAt = now;
                user.ReEngagementEmailCount++;
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Re-engagement processing failed for user {UserId}.", user.Id);
            }
        }
    }

    public static IReadOnlyList<(string Heading, string Blurb, string Url)> BuildHighlights(
        string appUrl, bool isMarried)
    {
        var root = appUrl.TrimEnd('/');
        var highlights = new List<(string, string, string)>
        {
            ("Stories from the community", "Read member testimonials and celebrate what is happening on Mirage.", $"{root}/testimonials")
        };
        if (isMarried)
            highlights.Add(("Share your love story", "Encourage others by adding your journey to the testimonial page.", $"{root}/testimonials"));
        highlights.Add(("Companion and journals", "Detail your thoughts, answer meaningful prompts and create private journal entries.", $"{root}/companion"));
        highlights.Add(("Join a community", "Reconnect through conversations and communities that matter to you.", $"{root}/communities"));
        highlights.Add(("Discover upcoming events", "See gatherings and events you may want to attend.", $"{root}/events"));
        return highlights;
    }
}
