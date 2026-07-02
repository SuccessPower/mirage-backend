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

public sealed class OrganisationMember : Entity
{
    private OrganisationMember() { }

    public OrganisationMember(Guid organisationId, Guid userId, Guid? branchId)
    {
        OrganisationId = organisationId;
        UserId = userId;
        BranchId = branchId;
    }

    public Guid OrganisationId { get; private set; }
    public Organisation? Organisation { get; private set; }
    public Guid UserId { get; private set; }
    public Guid? BranchId { get; private set; }
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
}

public sealed class OrgEvent : Entity
{
    private OrgEvent() { }

    public OrgEvent(Guid organisationId, Guid? branchId, Guid createdByUserId, string title, string? description,
        string? imageUrl, DateTimeOffset startsAt, DateTimeOffset endsAt, string location, int? capacity)
    {
        OrganisationId = organisationId;
        BranchId = branchId;
        CreatedByUserId = createdByUserId;
        Title = title.Trim();
        Description = description?.Trim();
        ImageUrl = imageUrl?.Trim();
        StartsAt = startsAt;
        EndsAt = endsAt;
        Location = location.Trim();
        Capacity = capacity;
    }

    public Guid OrganisationId { get; private set; }
    public Organisation? Organisation { get; private set; }
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
