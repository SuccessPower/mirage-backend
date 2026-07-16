using Mirage.Domain.Common;
using Mirage.Domain.Enums;

namespace Mirage.Domain.Entities;

public sealed class Community : Entity
{
    // Church categories are reserved for the auto-created communities below — one General and,
    // once a member marries, one Married community per Organisation (see OrganisationId).
    public const string ChurchGeneralCategory = "General";
    public const string ChurchMarriedCategory = "Married";

    private Community() { }

    public Community(Guid createdByUserId, string name, string category, string description,
        string? avatarUrl = null, string? avatarKey = null, Guid? organisationId = null)
    {
        CreatedByUserId = createdByUserId;
        Name = name.Trim();
        Category = category.Trim();
        Description = description.Trim();
        AvatarUrl = avatarUrl?.Trim();
        AvatarKey = avatarKey?.Trim();
        OrganisationId = organisationId;
    }

    public Guid CreatedByUserId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Category { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string? AvatarUrl { get; private set; }
    public string? AvatarKey { get; private set; }
    public Guid? OrganisationId { get; private set; }
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

    public void ChangeRole(CommunityMemberRole role)
    {
        Role = role;
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
    public bool IsHidden { get; private set; }
    public DateTimeOffset? HiddenAt { get; private set; }
    public Community Community { get; private set; } = null!;
    public List<CommunityPostComment> Comments { get; private set; } = [];
    public List<CommunityPostLike> Likes { get; private set; } = [];
    public List<CommunityPostVote> Votes { get; private set; } = [];

    // Auto-triggered once downvotes cross CommunityVoteScoring.HideThreshold — see
    // CommunityEndpoints.CastPostVote. A community Owner/Moderator or PlatformAdmin can Unhide().
    public void Hide()
    {
        if (IsHidden) return;
        IsHidden = true;
        HiddenAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public void Unhide()
    {
        IsHidden = false;
        HiddenAt = null;
        Touch();
    }
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

// Upvote/downvote — a distinct signal from Likes above (kept separate: Likes stay a simple
// like/unlike toggle, Votes is the new signed up/down meter shown alongside it).
public sealed class CommunityPostVote : Entity
{
    private CommunityPostVote() { }

    public CommunityPostVote(Guid postId, Guid userId, sbyte value)
    {
        PostId = postId;
        UserId = userId;
        Value = value;
    }

    public Guid PostId { get; private set; }
    public Guid UserId { get; private set; }
    public sbyte Value { get; private set; }
    public CommunityPost Post { get; private set; } = null!;

    public void ChangeValue(sbyte value)
    {
        Value = value;
        Touch();
    }
}

public sealed class CommunityPostComment : Entity
{
    private CommunityPostComment() { }

    public CommunityPostComment(Guid postId, Guid authorUserId, string body, Guid? parentCommentId = null,
        Guid[]? mentionedUserIds = null)
    {
        PostId = postId;
        AuthorUserId = authorUserId;
        Body = body.Trim();
        ParentCommentId = parentCommentId;
        MentionedUserIds = mentionedUserIds ?? [];
    }

    public Guid PostId { get; private set; }
    public Guid AuthorUserId { get; private set; }
    public Guid? ParentCommentId { get; private set; }
    public string Body { get; private set; } = string.Empty;
    public Guid[] MentionedUserIds { get; private set; } = [];
    public bool IsEdited { get; private set; }
    public bool IsDeleted { get; private set; }
    public bool IsHidden { get; private set; }
    public DateTimeOffset? HiddenAt { get; private set; }
    public CommunityPost Post { get; private set; } = null!;
    public CommunityPostComment? ParentComment { get; private set; }
    public List<CommunityPostComment> Replies { get; private set; } = [];
    public List<CommunityPostCommentLike> Likes { get; private set; } = [];
    public List<CommunityPostCommentVote> Votes { get; private set; } = [];

    public void Edit(string body)
    {
        Body = body.Trim();
        IsEdited = true;
        Touch();
    }

    public void SoftDelete()
    {
        Body = string.Empty;
        IsDeleted = true;
        Touch();
    }

    public void Hide()
    {
        if (IsHidden) return;
        IsHidden = true;
        HiddenAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public void Unhide()
    {
        IsHidden = false;
        HiddenAt = null;
        Touch();
    }
}

public sealed class CommunityPostCommentLike : Entity
{
    private CommunityPostCommentLike() { }

    public CommunityPostCommentLike(Guid commentId, Guid userId)
    {
        CommentId = commentId;
        UserId = userId;
    }

    public Guid CommentId { get; private set; }
    public Guid UserId { get; private set; }
    public CommunityPostComment Comment { get; private set; } = null!;
}

public sealed class CommunityPostCommentVote : Entity
{
    private CommunityPostCommentVote() { }

    public CommunityPostCommentVote(Guid commentId, Guid userId, sbyte value)
    {
        CommentId = commentId;
        UserId = userId;
        Value = value;
    }

    public Guid CommentId { get; private set; }
    public Guid UserId { get; private set; }
    public sbyte Value { get; private set; }
    public CommunityPostComment Comment { get; private set; } = null!;

    public void ChangeValue(sbyte value)
    {
        Value = value;
        Touch();
    }
}
