using ArtaleAI.Application.Pipeline;
using Xunit;

namespace ArtaleAI.Tests;

public sealed class AttackInputArbiterTests
{
    [Fact]
    public void SessionCooldown_BlocksUntilElapsed()
    {
        var last = Utc(0);
        Assert.False(AttackInputArbiter.IsCooldownReady(last, Utc(149)));
        Assert.True(AttackInputArbiter.IsCooldownReady(last, Utc(150)));
    }

    [Fact]
    public void SessionCooldown_AllowsFirstAttack()
    {
        Assert.True(AttackInputArbiter.IsCooldownReady(DateTime.MinValue, Utc(0)));
    }

    [Fact]
    public void Facing_RetapsOnlyAfterCooldown()
    {
        var last = Utc(0);
        Assert.False(AttackInputArbiter.ShouldRetapFacing(last, Utc(200)));
        Assert.True(AttackInputArbiter.ShouldRetapFacing(last, Utc(201)));
    }

    private static DateTime Utc(int ms) =>
        new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ms);
}
