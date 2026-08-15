using Mirage.Domain.Common;
using Mirage.Domain.Enums;

namespace Mirage.Domain.Entities;

// A birthday or wedding-anniversary celebration, created once per year per celebrant by
// CelebrationPostService. Entries are permanent (shown on the member's profile forever);
// the home-page banner only surfaces those created in the last 24 hours. A shared
// anniversary features both spouses on one entry — UserId/PartnerUserId use the same
// canonical GUID ordering as Couple so a couple can never get two entries for one year.
public sealed class CelebrationEntry : Entity
{
    private CelebrationEntry() { }

    public CelebrationEntry(Guid userId, Guid? partnerUserId, CelebrationType type, int year,
        string title, string body)
    {
        if (partnerUserId is not null && type != CelebrationType.Anniversary)
            throw new ArgumentException("Only anniversary celebrations can feature a partner.", nameof(partnerUserId));
        if (partnerUserId is not null && partnerUserId.Value.CompareTo(userId) < 0)
            (userId, partnerUserId) = (partnerUserId.Value, userId);
        UserId = userId;
        PartnerUserId = partnerUserId;
        Type = type;
        Year = year;
        Title = title.Trim();
        Body = body.Trim();
    }

    public Guid UserId { get; private set; }
    public Guid? PartnerUserId { get; private set; }
    public CelebrationType Type { get; private set; }
    public int Year { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public DateTimeOffset? EmailSentAt { get; private set; }
    public DateTimeOffset? PartnerEmailSentAt { get; private set; }

    public void MarkEmailSent(Guid recipientUserId)
    {
        if (recipientUserId == UserId) EmailSentAt = DateTimeOffset.UtcNow;
        else if (recipientUserId == PartnerUserId) PartnerEmailSentAt = DateTimeOffset.UtcNow;
        else throw new ArgumentException("User is not featured on this celebration.", nameof(recipientUserId));
        Touch();
    }
}

// A congratulatory message left on a celebration entry — the Mirage Team seeds the first wish
// when the entry is published, then any member can add their own from the celebrant's profile.
public sealed class CelebrationWish : Entity
{
    private CelebrationWish() { }

    public CelebrationWish(Guid celebrationEntryId, Guid authorUserId, string body)
    {
        CelebrationEntryId = celebrationEntryId;
        AuthorUserId = authorUserId;
        Body = body.Trim();
    }

    public Guid CelebrationEntryId { get; private set; }
    public Guid AuthorUserId { get; private set; }
    public string Body { get; private set; } = string.Empty;
}

// A reaction to a wish — lets a member cheer someone else's birthday message without writing
// their own reply. One row per (wish, user); toggled on/off from CelebrationEndpoints.
public sealed class CelebrationWishLike : Entity
{
    private CelebrationWishLike() { }

    public CelebrationWishLike(Guid celebrationWishId, Guid userId)
    {
        CelebrationWishId = celebrationWishId;
        UserId = userId;
    }

    public Guid CelebrationWishId { get; private set; }
    public Guid UserId { get; private set; }
}
