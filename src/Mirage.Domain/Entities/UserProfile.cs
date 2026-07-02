using Mirage.Domain.Common;
using Mirage.Domain.Enums;

namespace Mirage.Domain.Entities;

public sealed class UserProfile : Entity
{
    private UserProfile() { }

    public UserProfile(Guid userId, string displayName, DateOnly dateOfBirth, string city, string country,
        string denomination, RelationshipIntent intent, string bio, Sex? sex = null,
        RelationshipStatus? relationshipStatus = null, string? occupation = null)
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
    public Sex? Sex { get; private set; }
    public RelationshipStatus? RelationshipStatus { get; private set; }
    public int? HeightInches { get; private set; }
    public SkinTone? SkinTone { get; private set; }
    public string? PreferredLanguage { get; private set; }
    public string? Occupation { get; private set; }

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

    public void Verify() { IsVerified = true; Touch(); }

    public void MarkMarried() { RelationshipStatus = Mirage.Domain.Enums.RelationshipStatus.Married; Touch(); }
}
