using Mirage.Domain.Common;

namespace Mirage.Domain.Entities;

// A personal feed-control vote, not a public score: a downvote (-1) permanently hides the
// target from the voter's home feed, an upvote (+1) boosts the target in the voter's ranking.
public sealed class ProfileVote : Entity
{
    private ProfileVote() { }

    public ProfileVote(Guid voterUserId, Guid targetUserId, sbyte value)
    {
        if (voterUserId == targetUserId)
            throw new InvalidOperationException("Users cannot vote on their own profile.");
        VoterUserId = voterUserId;
        TargetUserId = targetUserId;
        Value = value;
    }

    public Guid VoterUserId { get; private set; }
    public Guid TargetUserId { get; private set; }
    public sbyte Value { get; private set; }

    public void ChangeValue(sbyte value)
    {
        Value = value;
        Touch();
    }
}
