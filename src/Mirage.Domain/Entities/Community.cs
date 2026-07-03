using Mirage.Domain.Common;
using Mirage.Domain.Enums;

namespace Mirage.Domain.Entities;

public sealed class Community : Entity
{
    private Community() { }

    public Community(Guid createdByUserId, string name, string category, string description)
    {
        CreatedByUserId = createdByUserId;
        Name = name.Trim();
        Category = category.Trim();
        Description = description.Trim();
    }

    public Guid CreatedByUserId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Category { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public CommunityStatus Status { get; private set; } = CommunityStatus.Active;
    public List<CommunityMember> Members { get; private set; } = [];
    public List<CommunityPost> Posts { get; private set; } = [];

    public void Archive()
    {
        Status = CommunityStatus.Archived;
        Touch();
    }
}

public sealed class CommunityMember : Entity
{
    private CommunityMember() { }

    public CommunityMember(Guid communityId, Guid userId, CommunityMemberRole role = CommunityMemberRole.Member)
    {
        CommunityId = communityId;
        UserId = userId;
        Role = role;
    }

    public Guid CommunityId { get; private set; }
    public Guid UserId { get; private set; }
    public CommunityMemberRole Role { get; private set; } = CommunityMemberRole.Member;
    public DateTimeOffset? LeftAt { get; private set; }
    public Community Community { get; private set; } = null!;

    public void Leave()
    {
        LeftAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public void Rejoin()
    {
        LeftAt = null;
        Touch();
    }
}

public sealed class CommunityPost : Entity
{
    private CommunityPost() { }

    public CommunityPost(Guid communityId, Guid authorUserId, string body)
    {
        CommunityId = communityId;
        AuthorUserId = authorUserId;
        Body = body.Trim();
    }

    public Guid CommunityId { get; private set; }
    public Guid AuthorUserId { get; private set; }
    public string Body { get; private set; } = string.Empty;
    public Community Community { get; private set; } = null!;
}
