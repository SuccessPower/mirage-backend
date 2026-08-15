using Microsoft.EntityFrameworkCore;
using Mirage.Application.Abstractions;
using Mirage.Domain.Enums;
using Mirage.Infrastructure.Identity;
using Mirage.Infrastructure.Persistence;

namespace Mirage.Api.Services;

/// <summary>
/// Emails members who haven't signed in for a while, pointing them at the parts of Mirage they may
/// not have seen. Deliberately infrequent: the first nudge lands after two idle days, then weekly,
/// and stops after four in total so a permanently dormant account is never mailed forever.
/// Signing in resets both counters (see AuthEndpoints.IssueTokens), so someone who returns and
/// lapses again starts a fresh series rather than resuming a spent quota.
/// </summary>
public sealed class ReEngagementService(MirageDbContext db, IEmailService email, IConfiguration configuration,
    ILogger<ReEngagementService> logger)
{
    private static readonly TimeSpan IdleBeforeFirstEmail = TimeSpan.FromDays(2);
    private static readonly TimeSpan BetweenEmails = TimeSpan.FromDays(7);
    private const int MaxEmailsPerLapse = 4;

    public async Task RunBatchAsync(int batchSize, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var idleSince = now - IdleBeforeFirstEmail;
        var repeatSince = now - BetweenEmails;

        var candidates = await db.Users.AsNoTracking()
            .Where(user => user.IsActive && !user.IsDeleted && user.EmailConfirmed
                && user.Email != null && user.Email != SystemAccounts.MirageTeamEmail
                && user.ReEngagementEmailCount < MaxEmailsPerLapse
                && user.LastLoginAt != null && user.LastLoginAt <= idleSince
                // First in the series once they cross the idle threshold, then one a week.
                && (user.LastReEngagementEmailAt == null || user.LastReEngagementEmailAt <= repeatSince))
            // Longest-dormant first, so a backlog drains in a fair order rather than by user id.
            .OrderBy(user => user.LastLoginAt)
            .Select(user => new { user.Id, user.Email })
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0) return;

        var userIds = candidates.Select(x => x.Id).ToArray();
        var profiles = await db.Profiles.AsNoTracking()
            .Where(profile => userIds.Contains(profile.UserId))
            .Select(profile => new { profile.UserId, profile.DisplayName, profile.RelationshipStatus })
            .ToDictionaryAsync(profile => profile.UserId, cancellationToken);

        var appUrl = (configuration["Frontend:BaseUrl"] ?? "https://www.themiragehub.com").TrimEnd('/');

        foreach (var candidate in candidates)
        {
            var profile = profiles.GetValueOrDefault(candidate.Id);
            var displayName = string.IsNullOrWhiteSpace(profile?.DisplayName) ? "there" : profile.DisplayName;
            var isMarried = profile?.RelationshipStatus == RelationshipStatus.Married;

            // Read the current count rather than the projection's, so two overlapping runs can't
            // both send off the same stale value.
            var sent = await email.SendReEngagementEmailAsync(candidate.Email!, displayName,
                $"We've missed you, {displayName}", Intro(isMarried), appUrl,
                Highlights(appUrl, isMarried), cancellationToken);

            if (!sent)
            {
                // Leave the stamps alone so this user is picked up again next run instead of
                // silently burning one of their four emails on a send that never landed.
                logger.LogWarning("Re-engagement email to {UserId} was not sent; will retry.", candidate.Id);
                continue;
            }

            await db.Users.Where(user => user.Id == candidate.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(user => user.LastReEngagementEmailAt, DateTimeOffset.UtcNow)
                    .SetProperty(user => user.ReEngagementEmailCount, user => user.ReEngagementEmailCount + 1),
                    cancellationToken);
        }
    }

    private static string Intro(bool isMarried) => isMarried
        ? "it's been a little while since you were on Mirage, and plenty has been happening. Here are a few places worth a look, including one that only you can fill in."
        : "it's been a little while since you were on Mirage, and plenty has been happening. Here are a few places worth a look while you're back.";

    private static IReadOnlyList<(string Heading, string Blurb, string Url)> Highlights(string appUrl, bool isMarried)
    {
        var highlights = new List<(string, string, string)>
        {
            ("Testimonials", "Read how other members met, courted and married through Mirage.",
                $"{appUrl}/testimonials")
        };

        // Only married members have a love story to tell, so this row would be a dead end for
        // everyone else.
        if (isMarried)
            highlights.Add(("Share your love story",
                "Married through Mirage, or bringing your marriage with you? Add your story and encourage the members walking the road behind you.",
                $"{appUrl}/testimonials"));

        highlights.Add(("Companion",
            "Think out loud in private. Journal your reflections, work through prompts, and track how you're growing.",
            $"{appUrl}/companion"));
        highlights.Add(("Communities",
            "Join the conversations happening in your church and interest communities.",
            $"{appUrl}/communities"));
        highlights.Add(("Events",
            "See the gatherings, hangouts and meetups coming up near you.",
            $"{appUrl}/events"));

        return highlights;
    }
}
