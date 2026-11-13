using Spellcaster.Core;
using Xunit;

namespace Spellcaster.Core.Tests;

public class MageGripTests
{
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
    [InlineData(.449, 25, true, GripRelease.SmallProjectile)]
    [InlineData(.45, 25, true, GripRelease.NormalSpell)]
    [InlineData(1.5, 24, true, GripRelease.SmallProjectile)]
    [InlineData(.45, 0, true, GripRelease.SmallProjectile)]
    [InlineData(.45, 25, false, GripRelease.SmallProjectile)]
    public void ReleaseRequiresThresholdSoulAndUnlock(double held, int soul, bool unlocked, GripRelease expected)
    {
        var attack = new GripAttack(); attack.Press(2);
        Assert.Equal(expected, attack.Release(2 + held, soul, unlocked));
        Assert.Equal(GripRelease.None, attack.Release(3, 99, true));
    }

    [Theory]
    [InlineData(.41)] [InlineData(.25)]
    public void CooldownHonoursPassedNailInterval(double interval)
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
}
