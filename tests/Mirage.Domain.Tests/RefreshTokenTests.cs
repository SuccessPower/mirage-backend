using Mirage.Domain.Entities;
using Xunit;

namespace Mirage.Domain.Tests;

public sealed class RefreshTokenTests
{
    private static RefreshToken NewToken(string value = "raw-refresh-token") =>
        new(Guid.NewGuid(), value, DateTimeOffset.UtcNow.AddDays(365));

    [Fact]
    public void A_fresh_token_is_active_and_outside_the_rotation_grace_window()
    {
        var token = NewToken();

        Assert.True(token.IsActive);
        Assert.Null(token.RevokedAt);
        Assert.False(token.IsWithinRotationGrace(TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void A_just_rotated_token_stays_inside_the_grace_window()
    {
        var token = NewToken();
        token.Revoke();

        // A mobile client interrupted mid-refresh replays the token it still holds; inside the
        // window the API re-issues instead of signing the member out.
        Assert.True(token.IsWithinRotationGrace(TimeSpan.FromMinutes(1)));
        Assert.False(token.IsActive);
    }

    [Fact]
    public void A_token_rotated_before_the_window_is_no_longer_forgiven()
    {
        var token = NewToken();
        token.Revoke();

        Assert.False(token.IsWithinRotationGrace(TimeSpan.Zero.Subtract(TimeSpan.FromSeconds(1))));
    }

    [Fact]
    public void Revoking_twice_does_not_slide_the_grace_window_forward()
    {
        var token = NewToken();
        token.Revoke();
        var firstRevocation = token.RevokedAt;

        token.Revoke();

        // Otherwise a leaked token could be replayed indefinitely, each replay buying another
        // grace window.
        Assert.Equal(firstRevocation, token.RevokedAt);
    }

    [Fact]
    public void An_expired_token_is_inactive_even_before_revocation()
    {
        var token = new RefreshToken(Guid.NewGuid(), "raw", DateTimeOffset.UtcNow.AddSeconds(-1));

        Assert.False(token.IsActive);
    }
}
