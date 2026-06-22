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
}
