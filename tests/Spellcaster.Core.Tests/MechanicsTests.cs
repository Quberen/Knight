using Spellcaster.Core;
using Xunit;

namespace Spellcaster.Core.Tests;

public class MechanicsTests
{
    [Theory]
    [InlineData(0, 11)] [InlineData(25, 8.25)] [InlineData(40, 6.6)]
    [InlineData(80, 2.2)] [InlineData(100, 0)]
    public void Formula(double heat, double expected) => Assert.Equal(expected, SoulFormula.Spell(heat, 1), 9);

    [Theory]
    [InlineData(1, 11)] [InlineData(2, 11)] [InlineData(3, 11)]
    [InlineData(4, 5.5)] [InlineData(5, 5.5)] [InlineData(99, 5.5)]
    public void FourthAndLaterHitsAreHalved(int hit, double expected) => Assert.Equal(expected, SoulFormula.Spell(0, hit));

    [Theory]
    [InlineData(false, false, 8.25)] [InlineData(true, false, 10.5)]
    [InlineData(false, true, 14.25)] [InlineData(true, true, 16.5)]
    public void VanillaSoulCharmsStack(bool catcher, bool eater, double expected)
    {
        Assert.Equal(expected, SoulFormula.Spell(25, 1, catcher, eater));
        Assert.Equal(expected / 2, SoulFormula.Spell(25, 4, catcher, eater));
    }

    [Theory]
    [InlineData(CastKind.SmallProjectile, 5)] [InlineData(CastKind.NormalSpell, 25)]
    [InlineData(CastKind.ChargedSpell, 40)] [InlineData(CastKind.ChainDash, 15)]
    public void Heating(CastKind kind, double expected)
    {
        var heat = new HeatMeter(); heat.AddCast(kind); Assert.Equal(expected, heat.Heat);
    }

    [Fact]
    public void CooldownCrossingDelayOnlyUsesExcessTime()
    {
        var heat = new HeatMeter(); heat.AddCast(CastKind.NormalSpell);
        heat.Advance(0.49); Assert.Equal(25, heat.Heat);
        heat.Advance(0.01); Assert.Equal(25, heat.Heat, 9);
        heat.Advance(0.25); Assert.Equal(20, heat.Heat, 9);
    }

    [Fact]
    public void NewCastRestartsDelayAndBoundsStayValid()
    {
        var heat = new HeatMeter();
        for (int i = 0; i < 10; i++) heat.AddCast(CastKind.ChargedSpell);
        Assert.Equal(100, heat.Heat);
        heat.Advance(0.6); Assert.Equal(98, heat.Heat, 9);
        heat.AddCast(CastKind.SmallProjectile); heat.Advance(0.5);
        Assert.Equal(100, heat.Heat);
        Assert.True(heat.Advance(100)); Assert.Equal(0, heat.Heat);
        Assert.False(heat.Advance(100));
    }

    [Theory]
    [InlineData(30)] [InlineData(60)] [InlineData(144)] [InlineData(240)]
    public void CoolingIsIndependentOfFrameRate(int fps)
    {
        var heat = new HeatMeter(); heat.AddCast(CastKind.ChargedSpell);
        for (int i = 0; i < fps * 2; i++) heat.Advance(1.0 / fps);
        Assert.Equal(10, heat.Heat, 8);
        var singleFrame = new HeatMeter(); singleFrame.AddCast(CastKind.ChargedSpell); singleFrame.Advance(2);
        Assert.Equal(singleFrame.Heat, heat.Heat, 8);
    }

    [Fact]
    public void PausedTimeDoesNotCool()
    {
        var heat = new HeatMeter(); heat.AddCast(CastKind.NormalSpell);
        for (int i = 0; i < 100; i++) heat.Advance(0);
        Assert.Equal(25, heat.Heat);
    }

    [Fact]
    public void DuplicateSegmentDoesNotPayOrAdvanceOrdinal()
    {
        var ledger = new SoulLedger(); long id = ledger.BeginCast();
        Assert.Equal(11, ledger.Hit(id, 1, 0));
        for (int i = 0; i < 20; i++) Assert.Equal(0, ledger.Hit(id, 1, 0));
        Assert.Equal(11, ledger.Hit(id, 2, 0)); Assert.Equal(11, ledger.Hit(id, 3, 0));
        Assert.Equal(5.5, ledger.Hit(id, 4, 0));
    }

    [Fact]
    public void OverlappingCastsHaveIndependentOrdinals()
    {
        var ledger = new SoulLedger(); long a = ledger.BeginCast(), b = ledger.BeginCast();
        for (int i = 0; i < 3; i++) Assert.Equal(11, ledger.Hit(a, i, 0));
        Assert.Equal(11, ledger.Hit(b, 0, 0)); Assert.Equal(5.5, ledger.Hit(a, 3, 0));
        Assert.Equal(0, ledger.Hit(b, 0, 0));
    }

    [Fact]
    public void ZeroSoulHitStillCountsAndUsesHeatAtHitTime()
    {
        var ledger = new SoulLedger(); long id = ledger.BeginCast();
        Assert.Equal(0, ledger.Hit(id, 0, 100));
        Assert.Equal(8.25, ledger.Hit(id, 1, 25)); Assert.Equal(11, ledger.Hit(id, 2, 0));
        Assert.Equal(5.5, ledger.Hit(id, 3, 0));
    }

    [Fact]
    public void ResetInvalidatesOldCastsAndAllTransientState()
    {
        var heat = new HeatMeter(); var ledger = new SoulLedger(); var accumulator = new SoulAccumulator();
        heat.AddCast(CastKind.NormalSpell); long old = ledger.BeginCast(); accumulator.Add(8.25);
        heat.Reset(); ledger.Reset(); accumulator.Reset();
        Assert.Equal(0, heat.Heat); Assert.Equal(0, ledger.ActiveCastCount); Assert.Equal(0, accumulator.Remainder);
        long fresh = ledger.BeginCast(); Assert.NotEqual(old, fresh);
        Assert.Equal(0, ledger.Hit(old, 0, 0)); Assert.Equal(11, ledger.Hit(fresh, 0, 0));
        ledger.EndCast(fresh); Assert.Equal(0, ledger.Hit(fresh, 1, 0));
    }

    [Fact]
    public void FractionalSoulIsConservedForSpellsAndHalfNailSoul()
    {
        var accumulator = new SoulAccumulator(); int total = 0;
        for (int i = 0; i < 4; i++) total += accumulator.Add(8.25);
        Assert.Equal(33, total); Assert.Equal(0, accumulator.Remainder);
        Assert.Equal(5, accumulator.Add(11 / 2.0)); Assert.Equal(6, accumulator.Add(11 / 2.0));
    }

    [Theory]
    [InlineData(-1)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void InvalidNumbersCannotPoisonState(double value)
    {
        var heat = new HeatMeter(); heat.AddCast(CastKind.NormalSpell);
        Assert.Throws<ArgumentOutOfRangeException>(() => heat.Advance(value)); Assert.Equal(25, heat.Heat);
        Assert.Throws<ArgumentOutOfRangeException>(() => SoulFormula.Spell(value, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SoulAccumulator().Add(value));
    }

    [Fact]
    public void InvalidHitDoesNotConsumeItsSegment()
    {
        var ledger = new SoulLedger(); long id = ledger.BeginCast();
        Assert.Throws<ArgumentOutOfRangeException>(() => ledger.Hit(id, 0, 101));
        Assert.Equal(11, ledger.Hit(id, 0, 0));
        Assert.Equal(0, ledger.Hit(999, 0, 0));
    }
}
