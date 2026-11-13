using Spellcaster.Core;
using Xunit;

namespace Spellcaster.Core.Tests;

public class GripFireGateTests
{
    private static GripFireGate Clean => new GripFireGate { Gameplay = true, CanInput = true };

    [Theory]
    [InlineData(nameof(GripFireGate.Gameplay), false)]
    [InlineData(nameof(GripFireGate.CanInput), false)]
    [InlineData(nameof(GripFireGate.Paused), true)]
    [InlineData(nameof(GripFireGate.Attacking), true)]
    [InlineData(nameof(GripFireGate.Dashing), true)]
    [InlineData(nameof(GripFireGate.Dead), true)]
    [InlineData(nameof(GripFireGate.HazardDeath), true)]
    [InlineData(nameof(GripFireGate.HazardRespawning), true)]
    [InlineData(nameof(GripFireGate.ControlRelinquished), true)]
    [InlineData(nameof(GripFireGate.NoInput), true)]
    [InlineData(nameof(GripFireGate.HardLanding), true)]
    [InlineData(nameof(GripFireGate.DashLanding), true)]
    [InlineData(nameof(GripFireGate.BackDashing), true)]
    [InlineData(nameof(GripFireGate.Recoiling), true)]
    [InlineData(nameof(GripFireGate.RecoilingLeft), true)]
    [InlineData(nameof(GripFireGate.RecoilingRight), true)]
    [InlineData(nameof(GripFireGate.RecoilFrozen), true)]
    [InlineData(nameof(GripFireGate.Transitioning), true)]
    [InlineData(nameof(GripFireGate.Focusing), true)]
    [InlineData(nameof(GripFireGate.AtBench), true)]
    [InlineData(nameof(GripFireGate.InAcid), true)]
    public void EachRestrictionAloneBlocksBothReleaseKinds(string field, bool value)
    {
        object boxed = Clean;
        typeof(GripFireGate).GetField(field)!.SetValue(boxed, value);
        var gate = (GripFireGate)boxed;
        Assert.False(gate.Allowed);
        foreach (double duration in new[] { .1, .45, 1.5 })
        {
            var attack = new GripAttack();
            attack.Tick(0, true, true);
            attack.Tick(duration, true, true);
            Assert.Equal(GripRelease.None, attack.Release(duration, 99, true,
                shamanMaster: true, canFire: gate.Allowed));
        }
    }

    [Theory]
    [InlineData(-.1, true)] [InlineData(0, true)] [InlineData(.00001, false)]
    public void DialogueAndAttackTimersMustExpire(double timer, bool allowed)
    {
        var gate = Clean;
        gate.DialogueEndTimer = timer;
        Assert.Equal(allowed, gate.Allowed);
        gate.DialogueEndTimer = 0;
        gate.AttackCooldown = timer;
        Assert.Equal(allowed, gate.Allowed);
    }

    [Fact]
    public void CleanAllowsFireAndClearingOnlyOneOfMultipleBlockersDoesNot()
    {
        var gate = Clean;
        Assert.True(gate.Allowed);
        gate.AtBench = gate.ControlRelinquished = gate.Focusing = true;
        Assert.False(gate.Allowed);
        gate.ControlRelinquished = false;
        Assert.False(gate.Allowed);
        gate.AtBench = gate.Focusing = false;
        Assert.True(gate.Allowed);
    }

    [Theory]
    [InlineData(.45, GripRelease.NormalSpell)]
    [InlineData(1.5, GripRelease.ChargedSpell)]
    public void ChargedIntentWaitsThroughDashAndExpiresEvenWhileBlocked(double duration, GripRelease expected)
    {
        var gate = Clean;
        gate.Dashing = true;
        var attack = new GripAttack();
        attack.Tick(0, true, true);
        attack.Tick(duration, true, true);
        var release = attack.Release(duration, 99, true, shamanMaster: true,
            canFire: gate.Allowed, deferCharged: true);
        Assert.Equal(expected, release);
        var pending = new GripPendingCast();
        pending.Queue(duration, 90, release);
        Assert.False(pending.TryGet(duration + .1, out _, out _, gate.Allowed));
        gate.Dashing = false;
        Assert.True(pending.TryGet(duration + .2, out int direction, out var ready, gate.Allowed));
        Assert.Equal(90, direction);
        Assert.Equal(expected, ready);
        pending.Clear();
        Assert.False(pending.TryGet(duration + .3, out _, out _, gate.Allowed));
        pending.Queue(duration, 270, release);
        Assert.False(pending.TryGet(duration + 1.01, out _, out _, false));
        Assert.False(pending.TryGet(duration + 1.02, out _, out _, true));
    }

    [Fact]
    public void BlockedTapIsDiscardedEvenWhenChargedDeferralIsEnabled()
    {
        var attack = new GripAttack();
        attack.Tick(0, true, true);
        attack.Tick(.1, true, true);
        Assert.Equal(GripRelease.None, attack.Release(.1, 99, true, canFire: false, deferCharged: true));
        var pending = new GripPendingCast();
        pending.Queue(.1, 0, GripRelease.SmallProjectile);
        Assert.False(pending.TryGet(.2, out _, out _, true));
    }

    [Fact]
    public void ShortTapRequiresGateAndIntervalButChargingCanStartDuringCooldown()
    {
        var attack = new GripAttack();
        attack.Tick(0, true, true);
        Assert.Equal(GripRelease.SmallProjectile, attack.Release(.1, 0, true, canFire: Clean.Allowed));
        attack.Commit(.1, GripAttack.SmallShotInterval(.25));
        attack.Tick(.15, true, true);
        Assert.True(attack.Holding);
        Assert.Equal(GripRelease.None, attack.Release(.2, 0, true, canFire: Clean.Allowed));
        attack.Tick(.25, true, true);
        Assert.Equal(GripRelease.SmallProjectile, attack.Release(.325, 0, true, canFire: Clean.Allowed));
    }
}
