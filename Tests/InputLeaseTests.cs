using ArtaleAI.Domain.Input;
using Xunit;

namespace ArtaleAI.Tests;

public sealed class InputLeaseTests
{
    [Fact]
    public void TryAcquire_FromIdle_Succeeds()
    {
        var lease = new InputLease();
        Assert.True(lease.TryAcquire(InputOwner.Combat));
        Assert.Equal(InputOwner.Combat, lease.Current);
        Assert.True(lease.BlocksNavigationKeys);
        Assert.True(lease.BlocksCombatStart);
    }

    [Fact]
    public void TryAcquire_WhenHeldByOther_Fails()
    {
        var lease = new InputLease();
        Assert.True(lease.TryAcquire(InputOwner.Combat));
        Assert.False(lease.TryAcquire(InputOwner.Party));
        Assert.Equal(InputOwner.Combat, lease.Current);
    }

    [Fact]
    public void TryAcquire_SameOwner_IsIdempotent()
    {
        var lease = new InputLease();
        Assert.True(lease.TryAcquire(InputOwner.Combat));
        Assert.True(lease.TryAcquire(InputOwner.Combat));
    }

    [Fact]
    public void Release_OnlyClearsMatchingOwner()
    {
        var lease = new InputLease();
        Assert.True(lease.TryAcquire(InputOwner.Combat));
        lease.Release(InputOwner.Party);
        Assert.Equal(InputOwner.Combat, lease.Current);
        lease.Release(InputOwner.Combat);
        Assert.True(lease.IsIdle);
    }

    [Fact]
    public void PreemptCombat_ClearsCombatOnly()
    {
        var lease = new InputLease();
        Assert.True(lease.TryAcquire(InputOwner.Combat));
        lease.PreemptCombat();
        Assert.True(lease.IsIdle);

        Assert.True(lease.TryAcquire(InputOwner.Party));
        lease.PreemptCombat();
        Assert.Equal(InputOwner.Party, lease.Current);
    }

    [Fact]
    public void TryAcquirePreemptingCombat_StealsFromCombat_AndInvokesCallback()
    {
        var lease = new InputLease();
        Assert.True(lease.TryAcquire(InputOwner.Combat));
        int calls = 0;
        Assert.True(lease.TryAcquirePreemptingCombat(InputOwner.Party, () => calls++));
        Assert.Equal(1, calls);
        Assert.Equal(InputOwner.Party, lease.Current);
    }

    [Fact]
    public void TryAcquirePreemptingCombat_DoesNotStealFromOtherUiOwner()
    {
        var lease = new InputLease();
        Assert.True(lease.TryAcquirePreemptingCombat(InputOwner.ChangeChannel));
        Assert.False(lease.TryAcquirePreemptingCombat(InputOwner.Party));
        Assert.Equal(InputOwner.ChangeChannel, lease.Current);
    }
}
