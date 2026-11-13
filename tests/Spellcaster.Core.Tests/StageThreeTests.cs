using Spellcaster.Core;
using Xunit;

namespace Spellcaster.Core.Tests;

public class StageThreeTests
{
    [Theory]
    [InlineData(false, false, 1.5)]
    [InlineData(true, false, 1.1)]
    [InlineData(false, true, 2.25)]
    [InlineData(true, true, 1.65)]
    public void SecondThresholdIncludesOnlyTimeModifiers(bool stone, bool fast, double seconds)
    {
        Assert.Equal(seconds, GripAttack.SecondChargeSeconds(stone, fast), 10);
        var attack = new GripAttack();
        attack.Press(10);
        Assert.Equal(GripRelease.NormalSpell, attack.Release(10 + seconds - .00001, 99, true, true, stone, fast));
        attack.Press(20);
        Assert.Equal(GripRelease.ChargedSpell, attack.Release(20 + seconds, 99, true, true, stone, fast));
    }

    [Theory]
    [InlineData(.449, 99, false, true, true, GripRelease.None, 0, null)]
    [InlineData(.45, 99, false, true, true, GripRelease.NormalSpell, 25, CastKind.NormalSpell)]
    [InlineData(1.499, 99, false, true, true, GripRelease.NormalSpell, 25, CastKind.NormalSpell)]
    [InlineData(1.5, 48, false, true, true, GripRelease.ChargedSpell, 48, CastKind.ChargedSpell)]
    [InlineData(1.5, 36, true, true, true, GripRelease.ChargedSpell, 36, CastKind.ChargedSpell)]
    [InlineData(1.5, 47, false, true, true, GripRelease.NormalSpell, 25, CastKind.NormalSpell)]
    [InlineData(1.5, 35, true, true, true, GripRelease.NormalSpell, 25, CastKind.NormalSpell)]
    [InlineData(1.5, 25, false, true, true, GripRelease.NormalSpell, 25, CastKind.NormalSpell)]
    [InlineData(1.5, 24, true, true, true, GripRelease.None, 0, null)]
    [InlineData(1.5, 0, false, true, true, GripRelease.None, 0, null)]
    [InlineData(1.5, 99, false, false, true, GripRelease.NormalSpell, 25, CastKind.NormalSpell)]
    [InlineData(1.5, 99, false, true, false, GripRelease.None, 0, null)]
    [InlineData(.45, 25, true, true, true, GripRelease.NormalSpell, 25, CastKind.NormalSpell)]
    public void ReleaseSelectsOneAffordableTier(double seconds, int soul, bool twister, bool master, bool unlocked,
        GripRelease expected, int cost, CastKind? kind)
    {
        var attack = new GripAttack(); attack.Press(5);
        var release = attack.Release(5 + seconds, soul, unlocked, master, false, false, twister);
        Assert.Equal(expected, release);
        Assert.Equal(cost, GripAttack.Cost(release, twister));
        if (kind.HasValue) Assert.Equal(kind.Value, GripAttack.Kind(release));
        else Assert.Equal(GripRelease.None, release);
        Assert.Equal(GripRelease.None, attack.Release(10, 99, true, true));
    }

    [Fact]
    public void ResetAndCancelDiscardSecondCharge()
    {
        var attack = new GripAttack(); attack.Press(0); attack.Reset();
        Assert.Equal(GripRelease.None, attack.Release(10, 99, true, true));
        attack.Press(11); attack.CancelHold();
        Assert.Equal(GripRelease.None, attack.Release(20, 99, true, true));
        attack.Press(21);
        Assert.Equal(GripRelease.SmallProjectile, attack.Release(21.1, 99, true, true));
    }

    [Theory]
    [InlineData(5, 10, 15)] [InlineData(9, 18, 27)] [InlineData(13, 26, 39)]
    [InlineData(17, 34, 51)] [InlineData(21, 42, 63)]
    [InlineData(15, 30, 45)] [InlineData(20, 40, 60)] [InlineData(30, 60, 90)]
    [InlineData(-1, 0, 0)]
    public void DamageUsesUncharmedBaseAndOneFinalRound(int damage, int charged, int withStone)
    {
        Assert.Equal(charged, SpellDamage.Calculate(damage, true, false));
        Assert.Equal(withStone, SpellDamage.Calculate(damage, true, true));
    }

    [Theory]
    [InlineData(5, 8)] [InlineData(9, 14)] [InlineData(13, 20)]
    public void FormulaRoundsOnlyAfterAllMultipliers(int damage, int expected) =>
        Assert.Equal(expected, SpellDamage.Calculate(damage, true, true, .5));

    [Theory]
    [InlineData(0)] [InlineData(.123)] [InlineData(.7)] [InlineData(2.5)]
    public void NativeDurationIsPassedThroughAsDefault(double native)
    {
        Assert.True(QuakeInvulnerability.IsValid(native));
        Assert.Equal(native, QuakeInvulnerability.Resolve(null, native));
        Assert.Equal(.25, QuakeInvulnerability.Resolve(.25, native));
        Assert.Equal(0, QuakeInvulnerability.Resolve(0, native));
    }

    [Theory]
    [InlineData(-1)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)] [InlineData(double.MaxValue)]
    public void InvalidDurationFallsBackToNativeThenFiniteFallback(double invalid)
    {
        Assert.False(QuakeInvulnerability.IsValid(invalid));
        Assert.Equal(.731, QuakeInvulnerability.Resolve(invalid, .731));
        Assert.Equal(QuakeInvulnerability.FallbackSeconds, QuakeInvulnerability.Resolve(null, invalid));
        Assert.Equal(QuakeInvulnerability.FallbackSeconds, QuakeInvulnerability.Resolve(invalid, null));
        Assert.True(QuakeInvulnerability.IsValid(QuakeInvulnerability.FallbackSeconds));
    }

    [Fact]
    public void AbsentHeroUsesFallbackOnlyWhenResolutionIsRequested() =>
        Assert.Equal(QuakeInvulnerability.FallbackSeconds, QuakeInvulnerability.Resolve(null, null));
}
