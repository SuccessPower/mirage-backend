using Mirage.Domain.Entities;
using Mirage.Domain.Enums;
using Xunit;

namespace Mirage.Domain.Tests;

public sealed class DomainInvariantTests
{
    [Fact]
    public void Date_request_rejects_invalid_time_window()
    {
        var startsAt = DateTimeOffset.UtcNow.AddDays(1);
        Assert.Throws<ArgumentException>(() =>
            new DateRequest(Guid.NewGuid(), "Coffee", startsAt, startsAt, "Lagos", null));
    }

    [Fact]
    public void Match_orders_users_to_enforce_unique_pair()
    {
        var first = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var second = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var match = new Match(second, first);
        Assert.Equal(first, match.User1Id);
        Assert.Equal(second, match.User2Id);
    }

    [Fact]
    public void Trust_unlock_requires_both_parties()
    {
        var session = new CounsellingSession(Guid.NewGuid(), Guid.NewGuid(), SessionType.Personal,
            DateTimeOffset.UtcNow.AddDays(1), "Communication", true, true);
        session.ConsentToReveal(true);
        Assert.Equal(TrustUnlockStatus.Pending, session.TrustUnlockStatus);
        session.ConsentToReveal(false);
        Assert.Equal(TrustUnlockStatus.Unlocked, session.TrustUnlockStatus);
    }

    [Fact]
    public void Match_can_be_closed()
    {
        var match = new Match(Guid.NewGuid(), Guid.NewGuid());
        match.Close();
        Assert.Equal(MatchStatus.Closed, match.Status);
        Assert.NotNull(match.LastActivityAt);
    }

    [Fact]
    public void Recommendation_can_be_revoked()
    {
        var recommendation = new Recommendation(Guid.NewGuid(), Guid.NewGuid(), null, "Trusted member");
        recommendation.Revoke();
        Assert.Equal(RecommendationStatus.Revoked, recommendation.Status);
    }

    [Fact]
    public void Completed_date_request_cannot_be_cancelled()
    {
        var request = new DateRequest(Guid.NewGuid(), "Coffee", DateTimeOffset.UtcNow.AddDays(1),
            DateTimeOffset.UtcNow.AddDays(1).AddHours(1), "Lagos", null);
        request.Cancel();
        Assert.Equal(DateRequestStatus.Cancelled, request.Status);
        Assert.Throws<InvalidOperationException>(request.Cancel);
    }

    [Fact]
    public void Single_capacity_select_matches_legacy_one_to_one_behavior()
    {
        var winner = Guid.NewGuid();
        var loser = Guid.NewGuid();
        var request = new DateRequest(Guid.NewGuid(), "Coffee", DateTimeOffset.UtcNow.AddDays(1),
            DateTimeOffset.UtcNow.AddDays(1).AddHours(1), "Lagos", null);
        request.Acceptances.Add(new DateRequestAcceptance(request.Id, winner));
        request.Acceptances.Add(new DateRequestAcceptance(request.Id, loser));

        request.Select(winner);

        Assert.Equal(DateRequestStatus.Confirmed, request.Status);
        Assert.Equal(winner, request.SelectedUserId);
        Assert.Equal(DateAcceptanceStatus.Selected, request.Acceptances.Single(x => x.AcceptorUserId == winner).Status);
        Assert.Equal(DateAcceptanceStatus.Declined, request.Acceptances.Single(x => x.AcceptorUserId == loser).Status);
    }

    [Fact]
    public void Group_gathering_confirms_once_capacity_is_filled_and_declines_the_rest()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();
        var request = new DateRequest(Guid.NewGuid(), "Picnic", DateTimeOffset.UtcNow.AddDays(1),
            DateTimeOffset.UtcNow.AddDays(1).AddHours(2), "Lagos", null,
            RelationshipIntent.Friendship, capacity: 2, itemsToBring: "Drinks");
        request.Acceptances.Add(new DateRequestAcceptance(request.Id, first));
        request.Acceptances.Add(new DateRequestAcceptance(request.Id, second));
        request.Acceptances.Add(new DateRequestAcceptance(request.Id, third));

        request.Select(first);
        Assert.Equal(DateRequestStatus.Open, request.Status);

        request.Select(second);

        Assert.Equal(DateRequestStatus.Confirmed, request.Status);
        Assert.Equal(DateAcceptanceStatus.Selected, request.Acceptances.Single(x => x.AcceptorUserId == first).Status);
        Assert.Equal(DateAcceptanceStatus.Selected, request.Acceptances.Single(x => x.AcceptorUserId == second).Status);
        Assert.Equal(DateAcceptanceStatus.Declined, request.Acceptances.Single(x => x.AcceptorUserId == third).Status);
        Assert.Throws<InvalidOperationException>(() => request.Select(third));
    }

    [Fact]
    public void Capacity_must_be_at_least_one()
    {
        Assert.Throws<ArgumentException>(() =>
            new DateRequest(Guid.NewGuid(), "Coffee", DateTimeOffset.UtcNow.AddDays(1),
                DateTimeOffset.UtcNow.AddDays(1).AddHours(1), "Lagos", null, capacity: 0));
    }
}
