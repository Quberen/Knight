using Spellcaster.Core;
using Xunit;

namespace Spellcaster.Core.Tests;

public class GripChargeTests
{
    [Theory]
    [InlineData(true, false, false, false, false)]
    [InlineData(false, true, false, false, false)]
    [InlineData(false, false, true, false, false)]
    [InlineData(false, false, false, true, false)]
    [InlineData(false, false, false, false, true)]
    public void ChargeBlockersPause(bool attacking, bool control, bool recoil, bool left, bool right)
    {
        var attack = new GripAttack();
        attack.Tick(0, true, true);
        attack.Tick(.2, true, true);
        attack.Tick(10, true, GripAttack.CanCharge(attacking, control, recoil, left, right));
        Assert.Equal(.2, attack.ChargeElapsed, 9);
        attack.Tick(10.474, true, true);
        Assert.Equal(0, attack.StoredTier(10.474));
        attack.Tick(10.475, true, true);
        Assert.Equal(1, attack.StoredTier(10.475));
    }

    [Fact]
    public void HoldingBeforeChargeableStartsWithoutEdge()
    {
        var attack = new GripAttack();
        attack.Tick(0, true, false);
        attack.Tick(10, true, false);
        attack.Tick(10.674, true, true);
        Assert.Equal(0, attack.StoredTier(10.674));
        attack.Tick(10.675, true, true);
        Assert.Equal(1, attack.StoredTier(10.675));
    }

    [Fact]
    public void DashAndOrdinaryLandingDoNotBlockCharge()
    {
        Assert.True(GripAttack.CanCharge(false, false, false, false, false));
        var attack = new GripAttack();
        attack.Tick(0, true, true);
        attack.Tick(.675, true, true);
        Assert.Equal(GripRelease.NormalSpell, attack.Release(.675, 25, true));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ResetWhileHeldRestartsWithoutEdge(bool mustDrop)
    {
        var attack = new GripAttack();
        attack.Tick(0, true, true);
        attack.Tick(2, true, true);
        if (mustDrop) attack.Tick(2, true, false, reset: true);
        else attack.ResetCharge(); // damage / ResetMotion callback
        Assert.True(attack.Holding);
        Assert.Equal(0, attack.StoredTier(2));
        attack.Tick(2.674, true, true);
        Assert.Equal(0, attack.StoredTier(2.674));
        attack.Tick(2.675, true, true);
        Assert.Equal(GripRelease.NormalSpell, attack.Release(2.675, 25, true));
    }

    [Fact]
    public void PauseRetainsChargeButPausedReleaseResets()
    {
        var attack = new GripAttack();
        attack.Tick(0, true, true);
        attack.Tick(.3, true, true);
        attack.Tick(10, true, true, paused: true);
        Assert.Equal(.3, attack.ChargeElapsed, 9);
        attack.Tick(10.375, true, true);
        Assert.Equal(1, attack.StoredTier(10.375));
        attack.Tick(11, false, false, paused: true);
        Assert.Equal(GripRelease.None, attack.Release(11, 99, true));
        attack.Tick(12, true, true);
        attack.Tick(12.675, true, true);
        Assert.Equal(1, attack.StoredTier(12.675));
    }

    [Fact]
    public void LongBlockedHoldCannotBecomeTap()
    {
        var attack = new GripAttack();
        attack.Tick(0, true, false);
        attack.Tick(10, true, false);
        attack.Tick(10.1, true, true);
        Assert.Equal(GripRelease.None, attack.Release(10.1, 99, true));
    }

    [Fact]
    public void ResetCannotConvertHoldIntoTap()
    {
        var attack = new GripAttack();
        attack.Tick(0, true, true);
        attack.Tick(.05, true, false, reset: true);
        Assert.Equal(GripRelease.None, attack.Release(.1, 99, true));
    }
    [Theory]
    [InlineData(false, 2.25)] [InlineData(true, 1.65)]
    public void SecondStoreUsesAccumulatedTimeAcrossPause(bool stone, double threshold)
    {
        var attack = new GripAttack();
        attack.Tick(0, true, true);
        attack.Tick(.675, true, true);
        attack.Tick(20, true, false);
        attack.Tick(20 + threshold - .675 - .001, true, true);
        Assert.Equal(1, attack.StoredTier(21, true, stone));
        attack.Tick(20 + threshold - .675, true, true);
        Assert.Equal(2, attack.StoredTier(21, true, stone));
    }

    [Fact]
    public void GameplayDeltaDoesNotUseUnscaledWallTimeForCharge()
    {
        var attack = new GripAttack();
        attack.Tick(0, true, true, deltaSeconds: 0);
        attack.Tick(10, true, true, deltaSeconds: .1);
        Assert.Equal(.1, attack.ChargeElapsed, 9);
        Assert.Equal(GripRelease.None, attack.Release(10, 99, true));
    }
}
