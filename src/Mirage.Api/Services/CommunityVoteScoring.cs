using Mirage.Domain.Enums;

namespace Mirage.Api.Services;

// Single source of truth for the vote color meter and the auto-hide threshold, so the two stay
// consistent (red is deliberately tied to the same downvote count that triggers hiding).
internal static class CommunityVoteScoring
{
    public const int HideThreshold = 10;
    private const int AmberThreshold = 6;
    private const int GreenNetThreshold = 5;

    public static CommunityVoteColor ColorFor(int upvotes, int downvotes)
    {
        if (downvotes >= HideThreshold) return CommunityVoteColor.Red;
        if (upvotes - downvotes > GreenNetThreshold) return CommunityVoteColor.Green;
        if (downvotes >= AmberThreshold) return CommunityVoteColor.Amber;
        return CommunityVoteColor.White;
    }

    public static bool ShouldHide(int downvotes) => downvotes >= HideThreshold;
}
