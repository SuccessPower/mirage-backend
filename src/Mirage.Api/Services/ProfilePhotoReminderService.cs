using Microsoft.EntityFrameworkCore;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;
using Mirage.Infrastructure.Persistence;

namespace Mirage.Api.Services;

public sealed class ProfilePhotoReminderService(MirageDbContext db, NotificationService notifications,
    IConfiguration configuration)
{
    public async Task RunBatchAsync(int batchSize, CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
        var candidates = await db.Profiles.AsNoTracking()
            .Where(profile => profile.PhotoUrls.Length < UserProfile.MinimumRequiredPhotos
                && profile.RelationshipStatus != RelationshipStatus.Married
                && db.Users.Any(user => user.Id == profile.UserId && user.IsActive)
                && !db.Notifications.Any(notification => notification.UserId == profile.UserId
                    && notification.Type == NotificationType.ProfilePhotosRequired
                    && notification.CreatedAt >= cutoff))
            .OrderBy(profile => profile.CreatedAt)
            .Select(profile => profile.UserId)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        // "Frontend:BaseUrl" is the configured key; the flat "FrontendUrl" this used isn't set
        // anywhere, so every reminder link fell through to the hardcoded default.
        var frontendUrl = configuration["Frontend:BaseUrl"] ?? "https://www.themiragehub.com";
        foreach (var userId in candidates)
            await notifications.NotifyAsync(userId, NotificationType.ProfilePhotosRequired,
                ProfilePhotoMessages.ReminderTitle, ProfilePhotoMessages.ReminderBody,
                cancellationToken: cancellationToken, actionUrl: $"{frontendUrl}/profile/edit",
                actionLabel: "Upload photos");
    }
}
