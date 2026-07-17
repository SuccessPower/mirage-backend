using Mirage.Domain.Common;
using Mirage.Domain.Enums;

namespace Mirage.Domain.Entities;

public sealed class UserProfile : Entity
{
    private UserProfile() { }

    public UserProfile(Guid userId, string displayName, DateOnly dateOfBirth, string city, string country,
        string denomination, RelationshipIntent intent, string bio, Sex? sex = null,
        RelationshipStatus? relationshipStatus = null, string? occupation = null, string? signupIpAddress = null)
    {
        UserId = userId;
        DisplayName = displayName.Trim();
        DateOfBirth = dateOfBirth;
        City = city.Trim();
        Country = country.Trim();
        Denomination = denomination.Trim();
        Intent = intent;
        Bio = bio.Trim();
        Sex = sex;
        RelationshipStatus = relationshipStatus;
        Occupation = occupation?.Trim();
        SignupIpAddress = signupIpAddress;
        IsProfileComplete = true;
    }

    // Minimal profile created from a Google sign-in — Google only ever gives us a name, email,
    // and picture, so the required fields (DOB, city, country, denomination, intent, bio) start
    // blank/placeholder and IsProfileComplete stays false until CompleteProfile() runs.
    public UserProfile(Guid userId, string displayName, string? avatarUrl, string? signupIpAddress = null)
    {
        UserId = userId;
        DisplayName = displayName.Trim();
        DateOfBirth = default;
        City = string.Empty;
        Country = string.Empty;
        Denomination = string.Empty;
        Intent = RelationshipIntent.Friendship;
        Bio = string.Empty;
        AvatarUrl = avatarUrl?.Trim();
        SignupIpAddress = signupIpAddress;
        IsProfileComplete = false;
    }

    public Guid UserId { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public DateOnly DateOfBirth { get; private set; }
    public string City { get; private set; } = string.Empty;
    public string Country { get; private set; } = string.Empty;
    public string Denomination { get; private set; } = string.Empty;
    public RelationshipIntent Intent { get; private set; }
    public string Bio { get; private set; } = string.Empty;
    public bool IsVerified { get; private set; }
    public bool AnonymityEnabled { get; private set; }
    public SubscriptionTier SubscriptionTier { get; private set; } = SubscriptionTier.Free;
    public string[] Interests { get; private set; } = [];
    public string? AvatarUrl { get; private set; }
    public string[] PhotoUrls { get; private set; } = [];
    public Sex? Sex { get; private set; }
    public RelationshipStatus? RelationshipStatus { get; private set; }
    public int? HeightInches { get; private set; }
    public SkinTone? SkinTone { get; private set; }
    public string? PreferredLanguage { get; private set; }
    public string? Occupation { get; private set; }
    public string? SignupIpAddress { get; private set; }
    public bool IsProfileComplete { get; private set; }

    public void Update(string displayName, string city, string country, string denomination,
        RelationshipIntent intent, string bio, bool anonymityEnabled, string[] interests, string? avatarUrl = null,
        Sex? sex = null, RelationshipStatus? relationshipStatus = null, int? heightInches = null,
        SkinTone? skinTone = null, string? preferredLanguage = null, string? occupation = null)
    {
        DisplayName = displayName.Trim();
        City = city.Trim();
        Country = country.Trim();
        Denomination = denomination.Trim();
        Intent = intent;
        Bio = bio.Trim();
        AnonymityEnabled = anonymityEnabled;
        Interests = interests.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (avatarUrl is not null) AvatarUrl = avatarUrl;
        if (sex is not null) Sex = sex;
        if (relationshipStatus is not null) RelationshipStatus = relationshipStatus;
        if (heightInches is not null) HeightInches = heightInches;
        if (skinTone is not null) SkinTone = skinTone;
        if (preferredLanguage is not null) PreferredLanguage = preferredLanguage.Trim();
        if (occupation is not null) Occupation = occupation.Trim();
        Touch();
    }

    // One-time completion for a minimal Google sign-in profile — fills in the fields normal
    // registration collects up front (DOB is otherwise never settable after account creation).
    public void CompleteProfile(DateOnly dateOfBirth, string city, string country, string denomination,
        RelationshipIntent intent, string bio, Sex? sex, RelationshipStatus? relationshipStatus, string? occupation)
    {
        DateOfBirth = dateOfBirth;
        City = city.Trim();
        Country = country.Trim();
        Denomination = denomination.Trim();
        Intent = intent;
        Bio = bio.Trim();
        if (sex is not null) Sex = sex;
        if (relationshipStatus is not null) RelationshipStatus = relationshipStatus;
        if (occupation is not null) Occupation = occupation.Trim();
        IsProfileComplete = true;
        Touch();
    }

    public const int MaxPhotos = 6;

    public void SetPhotos(string[] photoUrls)
    {
        var cleaned = photoUrls.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().ToArray();
        if (cleaned.Length > MaxPhotos)
            throw new InvalidOperationException($"A profile can have at most {MaxPhotos} photos.");
        PhotoUrls = cleaned;
        Touch();
    }

    public void Verify() { IsVerified = true; Touch(); }

    public void MarkMarried() { RelationshipStatus = Mirage.Domain.Enums.RelationshipStatus.Married; Touch(); }

    // Self-service "delete my account" — a hard delete of the underlying ApplicationUser isn't
    // safe (matches, messages, payments, and counselling sessions all Restrict-FK to the user),
    // so deletion instead scrubs everything personally identifying from the profile while leaving
    // the row (and other users' historical records referencing this UserId) intact. Combined with
    // ApplicationUser.IsActive = false, the account disappears from Discovery and can't log back in.
    public void ScrubPersonalData()
    {
        DisplayName = "Deleted user";
        Bio = string.Empty;
        AvatarUrl = null;
        PhotoUrls = [];
        Interests = [];
        Occupation = null;
        PreferredLanguage = null;
        AnonymityEnabled = true;
        Touch();
    }
}
