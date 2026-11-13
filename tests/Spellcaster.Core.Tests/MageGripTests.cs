using Spellcaster.Core;
using Xunit;

namespace Spellcaster.Core.Tests;

public class MageGripTests
{
    [Theory]
    [InlineData(0.25)] [InlineData(0.3)] [InlineData(0.41)]
    public void SmallShotAlwaysUsesNinetyPercentOfQuickSlashCooldown(double qsCooldown)
    {
        double interval = GripAttack.SmallShotInterval(qsCooldown);
        Assert.Equal(0.9 * qsCooldown, interval, 12);
        var attack = new GripAttack();
        attack.Commit(1, interval);
        Assert.False(attack.CanAttack(1 + interval - .00001));
        Assert.True(attack.CanAttack(1 + interval));
        double lift = new GripRecoil().Down(-20, 12, 47.4, interval);
        Assert.True(lift * interval - .5 * 47.4 * interval * interval < 0);
    }

    [Theory]
    [InlineData(16, 60, .41)] [InlineData(20, 60, .25)] [InlineData(12, 40, .1)]
    public void FirstDownShotIsPositiveAndBelowRuntimePogo(double pogo, double gravity, double interval)
    {
        double v = new GripRecoil().Down(-20, pogo, gravity, interval);
        Assert.InRange(v, double.Epsilon, pogo - double.Epsilon);
        Assert.True(v * interval - .5 * gravity * interval * interval < 0);
    }

    [Theory]
    [InlineData(.41)] [InlineData(.25)] [InlineData(.1)]
    public void RepeatedShotsCannotHover(double interval)
    {
        var recoil = new GripRecoil();
        const double pogo = 16, gravity = 60;
        double velocity = 0, height = 0;
        for (int i = 0; i < 40; i++)
        {
            double before = velocity;
            velocity = recoil.Down(velocity, pogo, gravity, interval);
            if (i > 0)
            {
                Assert.True(velocity < 0);
                Assert.True(velocity >= before);
            }
            double displacement = velocity * interval - .5 * gravity * interval * interval;
            Assert.True(displacement < 0);
            height += displacement;
            velocity -= gravity * interval;
        }
        Assert.True(height < 0);
    }

    [Fact]
    public void RepeatedShotNeverRenewsAnUpwardImpulse()
    {
        var recoil = new GripRecoil();
        recoil.Down(0, 16, 60, .25);
        Assert.True(recoil.Down(10, 16, 60, .25) < 0);
    }

    [Fact]
    public void HitRestoresFullPogoAndFirstShot()
    {
        var recoil = new GripRecoil();
        recoil.Down(0, 19, 60, .25); recoil.Down(-9, 19, 60, .25);
        Assert.Equal(19, recoil.Pogo(19));
        Assert.Equal(0, recoil.DownShots);
        Assert.True(recoil.Down(-9, 19, 60, .25) > 0);
    }

    [Fact]
    public void LandingResets()
    {
        var recoil = new GripRecoil(); recoil.Down(0, 16, 60, .25);
        recoil.Land(); Assert.Equal(0, recoil.DownShots);
        Assert.True(recoil.Down(0, 16, 60, .25) > 0);
    }

    [Theory]
    [InlineData(-10)] [InlineData(0)] [InlineData(10)]
    public void UpShotAddsSmallDownwardVelocity(double velocity)
    {
        double after = new GripRecoil().Up(velocity, 16, false);
        Assert.InRange(velocity - after, .001, 15.999);
    }

    [Theory]
    [InlineData(0)] [InlineData(.00001)] [InlineData(-.00001)]
    public void GroundedUpShotDoesNotPushIntoFloor(double velocity) =>
        Assert.Equal(velocity, new GripRecoil().Up(velocity, 16, true));

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(144)] [InlineData(240)]
    public void BallisticIntegrationIsFramePartitionIndependent(int fps)
    {
        double Simulate(int steps)
        {
            var recoil = new GripRecoil();
            double y = 0, v = 0;
            for (int shot = 0; shot < 20; shot++)
            {
                v = recoil.Down(v, 16, 60, .25);
                double dt = .25 / steps;
                for (int i = 0; i < steps; i++)
                { y += v * dt - 30 * dt * dt; v -= 60 * dt; }
            }
            return y;
        }
        Assert.Equal(Simulate(1), Simulate(fps), 8);
        var attack = new GripAttack(); attack.Press(0);
        double elapsed = 0;
        for (int i = 0; i < fps; i++) elapsed += .45 / fps;
        Assert.Equal(GripRelease.NormalSpell, attack.Release(elapsed, 25, true));
    }

    [Theory]
    [InlineData(.199, 25, true, GripRelease.SmallProjectile)] // genuine tap
    [InlineData(.2, 25, true, GripRelease.None)]             // past tap window, not yet charged: no-op
    [InlineData(.449, 25, true, GripRelease.None)]
    [InlineData(.45, 25, true, GripRelease.NormalSpell)]
    [InlineData(10, 25, true, GripRelease.NormalSpell)]      // stored indefinitely while held
    [InlineData(1.5, 24, true, GripRelease.None)]            // charged but not enough soul: no fallback shot
    [InlineData(.45, 0, true, GripRelease.None)]
    [InlineData(.45, 25, false, GripRelease.None)]
    [InlineData(.1, 0, false, GripRelease.SmallProjectile)]
    public void ReleaseRequiresThresholdSoulAndUnlock(double held, int soul, bool unlocked, GripRelease expected)
    {
        var attack = new GripAttack(); attack.Press(2);
        Assert.Equal(expected, attack.Release(2 + held, soul, unlocked));
        Assert.Equal(GripRelease.None, attack.Release(3, 99, true));
    }

    [Theory]
    [InlineData(.41)] [InlineData(.25)]
    public void CooldownHonoursPassedInterval(double interval)
    {
        var attack = new GripAttack(); attack.Commit(1, interval);
        Assert.False(attack.CanAttack(1 + interval - .00001));
        attack.Press(1); Assert.False(attack.Holding);
        Assert.True(attack.CanAttack(1 + interval));
        attack.Press(1 + interval); Assert.True(attack.Holding);
        attack.CancelHold(); Assert.False(attack.Holding);
        attack.Reset(); Assert.True(attack.CanAttack(0));
    }

    [Theory]
    [InlineData(5, 2)] [InlineData(9, 4)] [InlineData(13, 6)] [InlineData(17, 8)] [InlineData(21, 10)]
    public void DamageUsesRoundToEven(int nail, int damage) => Assert.Equal(damage, GripAttack.Damage(nail));

    [Theory]
    [InlineData(false, false, false, 5.5)] [InlineData(false, true, false, 7)]
    [InlineData(false, false, true, 9.5)] [InlineData(false, true, true, 11)]
    [InlineData(true, false, false, 3)] [InlineData(true, true, true, 7)]
    public void HalfNailSoulIncludesCharmsAndReserve(bool full, bool catcher, bool eater, double expected) =>
        Assert.Equal(expected, GripAttack.Soul(full, catcher, eater));

    [Theory]
    [InlineData(0, null)]
    [InlineData(1, "Fireball 1")]
    [InlineData(2, "Fireball 1")]
    [InlineData(99, "Fireball 1")]
    public void SmallShotsAlwaysUseWhiteVengefulSpirit(int level, string? state) =>
        Assert.Equal(state, GripProjectiles.SmallShotState(level));

    [Theory]
    [InlineData(0, null)]
    [InlineData(1, "Fireball 1")]
    [InlineData(2, "Fireball 2")]
    [InlineData(99, "Fireball 2")]
    public void ChargedFireballsUseUnlockedTier(int level, string? state) =>
        Assert.Equal(state, GripProjectiles.ChargedFireballState(level));

    [Fact]
    public void TapWindowAndChargeConstantsAreFixed()
    {
        Assert.Equal(0.2, GripAttack.TapSeconds);
        Assert.Equal(0.45, GripAttack.ChargeSeconds);
    }

    [Theory]
    [InlineData(0.199, 0, false)]
    [InlineData(0.2, 0, true)]   // winding after tap window
    [InlineData(0.449, 0, true)]
    [InlineData(0.45, 1, true)]  // first stored; Shaman Master winds toward the second flash
    [InlineData(1.499, 1, true)]
    [InlineData(1.5, 2, false)]  // shaman second stored
    [InlineData(8, 2, false)]    // still stored
    public void StoredTierAndWindingFollowHoldTime(double held, int tier, bool winding)
    {
        var attack = new GripAttack();
        attack.Press(1);
        Assert.Equal(tier, attack.StoredTier(1 + held, shamanMaster: true));
        Assert.Equal(winding, attack.Winding(1 + held, shamanMaster: true));
    }

    [Fact]
    public void WindingForSecondChargeAfterFirstIsStored()
    {
        var attack = new GripAttack();
        attack.Press(0);
        Assert.True(attack.Winding(0.3, shamanMaster: true));
        Assert.True(attack.Winding(0.3, shamanMaster: false)); // first-charge winding is independent of Shaman Master
        // After first charge, only Shaman Master keeps winding toward the second flash.
        Assert.False(attack.Winding(0.8, shamanMaster: false));
        Assert.True(attack.Winding(0.8, shamanMaster: true));
        Assert.False(attack.Winding(1.5, shamanMaster: true));
    }

    [Theory]
    [InlineData(true, false, false, false, false, false, false)]
    [InlineData(false, false, false, false, false, false, true)]
    [InlineData(true, true, false, false, false, false, true)]
    [InlineData(true, false, true, false, false, false, true)]
    [InlineData(true, false, false, true, false, false, true)]
    [InlineData(true, false, false, false, true, false, true)]
    [InlineData(true, false, false, false, false, true, true)]
    public void EachDropConditionDiscardsCharge(bool canPlay, bool transition, bool dead,
        bool hazard, bool respawning, bool noInput, bool expected) =>
        Assert.Equal(expected, GripAttack.MustDropCharge(canPlay, transition, dead, hazard, respawning, noInput));

    [Fact]
    public void PendingCastKeepsReleaseDirectionAndTierUntilInclusiveDeadline()
    {
        var pending = new GripPendingCast();
        pending.Queue(10, 270, GripRelease.ChargedSpell);
        foreach (double now in new[] { 10, 10.5, 11 })
        {
            Assert.True(pending.TryGet(now, out int direction, out var release));
            Assert.Equal(270, direction);
            Assert.Equal(GripRelease.ChargedSpell, release);
        }
        Assert.False(pending.TryGet(11.00001, out _, out _));
        Assert.False(pending.TryGet(12, out _, out _));
    }

    [Fact]
    public void PendingCastCanBeCancelledOrReplacedWithoutReplayingOldRelease()
    {
        var pending = new GripPendingCast();
        Assert.False(pending.TryGet(0, out _, out _));
        pending.Queue(0, 270, GripRelease.ChargedSpell);
        pending.Clear();
        Assert.False(pending.TryGet(.1, out _, out _));
        pending.Queue(.2, 90, GripRelease.NormalSpell);
        Assert.True(pending.TryGet(.3, out int direction, out var release));
        Assert.Equal(90, direction);
        Assert.Equal(GripRelease.NormalSpell, release);
        pending.Queue(.4, 180, GripRelease.None);
        Assert.False(pending.TryGet(.5, out _, out _));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void FirstTierRemainsFixedAndStoredWithEverySecondTierModifier(bool stone, bool fast)
    {
        var attack = new GripAttack();
        attack.Press(0);
        Assert.Equal(1, attack.StoredTier(.45, true, stone, fast));
        Assert.Equal(2, attack.StoredTier(3600, true, stone, fast));
        Assert.Equal(GripRelease.ChargedSpell, attack.Release(3600, 48, true, true, stone, fast));
    }
}
