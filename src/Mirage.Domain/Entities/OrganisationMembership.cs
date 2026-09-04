using Mirage.Domain.Common;
using Mirage.Domain.Enums;

namespace Mirage.Domain.Entities;

public sealed class OrganisationBranch : Entity
{
    private OrganisationBranch() { }

    public OrganisationBranch(Guid organisationId, string name, string city, string country, string? address)
    {
        OrganisationId = organisationId;
        Name = name.Trim();
        City = city.Trim();
        Country = country.Trim();
        Address = address?.Trim();
    }

    public Guid OrganisationId { get; private set; }
    public Organisation? Organisation { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string City { get; private set; } = string.Empty;
    public string Country { get; private set; } = string.Empty;
    public string? Address { get; private set; }
}

// A user granted management rights over an organisation, beyond the single original
// Organisation.AdminUserId owner. BranchId null means org-wide (same rights as the owner);
// a set BranchId is a manager scoped to that branch, invited by an org-wide manager.
public sealed class OrganisationManager : Entity
{
    private OrganisationManager() { }

    public OrganisationManager(Guid organisationId, Guid userId, Guid? branchId)
    {
        OrganisationId = organisationId;
        UserId = userId;
        BranchId = branchId;
    }

    public Guid OrganisationId { get; private set; }
    public Organisation? Organisation { get; private set; }
    public Guid UserId { get; private set; }
    public Guid? BranchId { get; private set; }
}

public sealed class OrganisationMember : Entity
{
    private OrganisationMember() { }

    public OrganisationMember(Guid organisationId, Guid userId, Guid? branchId, string? description = null)
    {
        OrganisationId = organisationId;
        UserId = userId;
        BranchId = branchId;
        Description = description?.Trim();
    }

    public Guid OrganisationId { get; private set; }
    public Organisation? Organisation { get; private set; }
    public Guid UserId { get; private set; }
    public Guid? BranchId { get; private set; }
    public string? Description { get; private set; }
    public OrganisationMemberStatus Status { get; private set; } = OrganisationMemberStatus.Pending;
    public Guid? AssignedMentorUserId { get; private set; }
    public Guid? AssignedCounsellorUserId { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }

    public void Approve()
    {
        Status = OrganisationMemberStatus.Approved;
        ReviewedAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public void Reject()
    {
        Status = OrganisationMemberStatus.Rejected;
        ReviewedAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public void Remove()
    {
        Status = OrganisationMemberStatus.Removed;
        ReviewedAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public void Assign(Guid? mentorUserId, Guid? counsellorUserId)
    {
        if (mentorUserId is not null) AssignedMentorUserId = mentorUserId;
        if (counsellorUserId is not null) AssignedCounsellorUserId = counsellorUserId;
        Touch();
    }

    /// <summary>
    /// Puts a previously removed or rejected member back into review for the same organisation.
    /// </summary>
    /// <remarks>
    /// (organisation_id, user_id) is unique, so rejoining a church someone was once removed from
    /// has to revive the existing row — inserting a second one throws at the database.
    /// </remarks>
    public void Reapply(Guid? branchId)
    {
        Status = OrganisationMemberStatus.Pending;
        BranchId = branchId;
        ReviewedAt = null;
        Touch();
    }
}

// An event on the public /events feed. Historically only a church could publish one, so it was
// organisation-owned; approved mentors publish here too, and a mentor's event carries a
// MentorProfileId instead of an OrganisationId. Exactly one of the two is set.
public sealed class OrgEvent : Entity
{
    private OrgEvent() { }

    public OrgEvent(Guid organisationId, Guid? branchId, Guid createdByUserId, string title, string? description,
        string? imageUrl, DateTimeOffset startsAt, DateTimeOffset endsAt, string location, int? capacity)
        : this(createdByUserId, title, description, imageUrl, startsAt, endsAt, location, capacity)
    {
        OrganisationId = organisationId;
        BranchId = branchId;
    }

    /// <summary>An event published by a mentor rather than a church.</summary>
    public static OrgEvent ForMentor(Guid mentorProfileId, Guid createdByUserId, string title, string? description,
        string? imageUrl, DateTimeOffset startsAt, DateTimeOffset endsAt, string location, int? capacity,
        MentorAudience audience) =>
        new(createdByUserId, title, description, imageUrl, startsAt, endsAt, location, capacity)
        {
            MentorProfileId = mentorProfileId,
            Audience = audience,
        };

    private OrgEvent(Guid createdByUserId, string title, string? description, string? imageUrl,
        DateTimeOffset startsAt, DateTimeOffset endsAt, string location, int? capacity)
    {
        CreatedByUserId = createdByUserId;
        Title = title.Trim();
        Description = description?.Trim();
        ImageUrl = imageUrl?.Trim();
        StartsAt = startsAt;
        EndsAt = endsAt;
        Location = location.Trim();
        Capacity = capacity;
    }

    public Guid? OrganisationId { get; private set; }
    public Organisation? Organisation { get; private set; }

    public Guid? MentorProfileId { get; private set; }
    public MentorProfile? Mentor { get; private set; }

    // Which of the mentor's groups was told about this event first. The event itself is public
    // either way — this only drives who gets the notification. Ignored on a church event.
    public MentorAudience Audience { get; private set; } = MentorAudience.Everyone;

    public Guid? BranchId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string? ImageUrl { get; private set; }
    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset EndsAt { get; private set; }
    public string Location { get; private set; } = string.Empty;
    public int? Capacity { get; private set; }
}

public sealed class EventTicket : Entity
{
    private EventTicket() { }

    public EventTicket(Guid eventId, Guid userId, string code)
    {
        EventId = eventId;
        UserId = userId;
        Code = code;
    }

    public Guid EventId { get; private set; }
    public OrgEvent? Event { get; private set; }
    public Guid UserId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public DateTimeOffset? CheckedInAt { get; private set; }

    public void CheckIn() => CheckedInAt = DateTimeOffset.UtcNow;
}
