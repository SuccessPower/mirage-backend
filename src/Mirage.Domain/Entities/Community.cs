using Mirage.Domain.Common;
using Mirage.Domain.Enums;

namespace Mirage.Domain.Entities;

public sealed class Community : Entity
{
    private Community() { }

    public Community(Guid createdByUserId, string name, string category, string description,
        string? avatarUrl = null, string? avatarKey = null)
    {
        CreatedByUserId = createdByUserId;
        Name = name.Trim();
        Category = category.Trim();
        Description = description.Trim();
        AvatarUrl = avatarUrl?.Trim();
        AvatarKey = avatarKey?.Trim();
    }

    public Guid CreatedByUserId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Category { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string? AvatarUrl { get; private set; }
    public string? AvatarKey { get; private set; }
    public CommunityStatus Status { get; private set; } = CommunityStatus.Active;
    public List<CommunityMember> Members { get; private set; } = [];
    public List<CommunityPost> Posts { get; private set; } = [];

    public void UpdateAvatar(string? avatarUrl, string? avatarKey)
    {
        AvatarUrl = avatarUrl?.Trim();
        AvatarKey = avatarKey?.Trim();
        Touch();
    }

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

    public CommunityPost(Guid communityId, Guid authorUserId, string? body, string? imageUrl = null)
    {
        CommunityId = communityId;
        AuthorUserId = authorUserId;
        Body = body?.Trim() ?? string.Empty;
        ImageUrl = imageUrl?.Trim();
    }

    public Guid CommunityId { get; private set; }
    public Guid AuthorUserId { get; private set; }
    public string Body { get; private set; } = string.Empty;
    public string? ImageUrl { get; private set; }
    public Community Community { get; private set; } = null!;
    public List<CommunityPostComment> Comments { get; private set; } = [];
    public List<CommunityPostLike> Likes { get; private set; } = [];
}

public sealed class CommunityPostLike : Entity
{
    private CommunityPostLike() { }

    public CommunityPostLike(Guid postId, Guid userId)
    {
        PostId = postId;
        UserId = userId;
    }

    public Guid PostId { get; private set; }
    public Guid UserId { get; private set; }
    public CommunityPost Post { get; private set; } = null!;
}

public sealed class CommunityPostComment : Entity
{
    private CommunityPostComment() { }

    public CommunityPostComment(Guid postId, Guid authorUserId, string body, Guid? parentCommentId = null)
    {
        PostId = postId;
        AuthorUserId = authorUserId;
        Body = body.Trim();
        ParentCommentId = parentCommentId;
    }

    public Guid PostId { get; private set; }
    public Guid AuthorUserId { get; private set; }
    public Guid? ParentCommentId { get; private set; }
    public string Body { get; private set; } = string.Empty;
    public CommunityPost Post { get; private set; } = null!;
    public CommunityPostComment? ParentComment { get; private set; }
    public List<CommunityPostComment> Replies { get; private set; } = [];
}
