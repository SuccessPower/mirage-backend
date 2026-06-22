using Mirage.Domain.Common;
using Mirage.Domain.Enums;

namespace Mirage.Domain.Entities;

public sealed class UserLike : Entity
{
    private UserLike() { }
    public UserLike(Guid sourceUserId, Guid targetUserId, LikeType type)
    {
        SourceUserId = sourceUserId;
        TargetUserId = targetUserId;
        Type = type;
    }
    public Guid SourceUserId { get; private set; }
    public Guid TargetUserId { get; private set; }
    public LikeType Type { get; private set; }
}

public sealed class Match : Entity
{
    private Match() { }
    public Match(Guid user1Id, Guid user2Id)
    {
        if (user1Id.CompareTo(user2Id) < 0) { User1Id = user1Id; User2Id = user2Id; }
        else { User1Id = user2Id; User2Id = user1Id; }
        MatchedAt = DateTimeOffset.UtcNow;
    }
    public Guid User1Id { get; private set; }
    public Guid User2Id { get; private set; }
    public MatchStatus Status { get; private set; } = MatchStatus.Active;
    public DateTimeOffset MatchedAt { get; private set; }
    public DateTimeOffset? LastActivityAt { get; private set; }
}
