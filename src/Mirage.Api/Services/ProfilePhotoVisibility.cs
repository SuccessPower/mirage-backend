using System.Linq.Expressions;
using Mirage.Domain.Entities;
using Mirage.Domain.Enums;

namespace Mirage.Api.Services;

/// <summary>
/// The two-photo requirement was introduced after the platform already had members. Hiding every
/// one of those profiles the day the rule shipped would have emptied Discovery and punished people
/// for a rule that did not exist when they joined, so profiles created before a configured cutoff
/// stay visible on their avatar alone while they are emailed and prompted to add the rest.
/// Anyone joining after the cutoff is gated normally: no visibility until two photos are up.
/// Married members are exempt outright — the requirement exists to prove a dating/friendship
/// profile is a real person before anyone invests in it, which doesn't apply the same way to
/// someone browsing the Marriage/Friendship community, so a second photo stays optional for them.
/// </summary>
internal static class ProfilePhotoVisibility
{
    /// <summary>
    /// Profiles created before this instant are grandfathered. Set "Photos:GrandfatherCutoffUtc"
    /// to move it; the default is the date the requirement shipped. Set it to a past date once
    /// everyone has caught up to retire grandfathering entirely.
    /// </summary>
    public static DateTimeOffset Cutoff(IConfiguration configuration) =>
        DateTimeOffset.TryParse(configuration["Photos:GrandfatherCutoffUtc"], out var configured)
            ? configured
            : new DateTimeOffset(2026, 8, 8, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Composable EF predicate — chain it onto a profile query with <c>.Where(...)</c> rather than
    /// inlining, since an expression variable cannot be called from inside another lambda body.
    /// </summary>
    public static Expression<Func<UserProfile, bool>> IsVisible(DateTimeOffset cutoff) =>
        profile => profile.AvatarUrl != null && profile.AvatarUrl != ""
            && (profile.PhotoUrls.Length >= UserProfile.MinimumRequiredPhotos || profile.CreatedAt < cutoff
                || profile.RelationshipStatus == RelationshipStatus.Married);

    /// <summary>In-memory counterpart for a profile that has already been loaded.</summary>
    public static bool IsVisible(UserProfile profile, DateTimeOffset cutoff) =>
        !string.IsNullOrWhiteSpace(profile.AvatarUrl)
        && (profile.PhotoUrls.Length >= UserProfile.MinimumRequiredPhotos || profile.CreatedAt < cutoff
            || profile.RelationshipStatus == RelationshipStatus.Married);

    /// <summary>
    /// Whether a viewer may browse without the 2-profile budget. Grandfathered members and married
    /// members (photos are optional for them) keep full browsing; the throttle only applies to
    /// unmarried accounts created under the new rule that haven't yet met the photo requirement.
    /// </summary>
    public static bool IsUnthrottledViewer(UserProfile profile, DateTimeOffset cutoff) =>
        profile.PhotoUrls.Length >= UserProfile.MinimumRequiredPhotos || profile.CreatedAt < cutoff
        || profile.RelationshipStatus == RelationshipStatus.Married;
}
