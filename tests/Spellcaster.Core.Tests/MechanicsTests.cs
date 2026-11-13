using System;
using Spellcaster.Core;
using Xunit;

namespace Spellcaster.Core.Tests;

public class MechanicsTests
{
    [Fact]
    public void SpellHitIsASingleFlatConstant()
    {
        Assert.Equal(9, SoulFormula.SpellHitSoul);
        Assert.Equal(SoulFormula.SpellHitSoul, SoulFormula.Spell());
    }

    [Theory]
    [InlineData(false, false, 9)]
    [InlineData(true, false, 12)]
    [InlineData(false, true, 17)]
    [InlineData(true, true, 20)]
    public void VanillaSoulCharmsStackAdditively(bool catcher, bool eater, double expected)
    {
        Assert.Equal(expected, SoulFormula.Spell(catcher, eater));
    }

    [Fact]
    public void FourthAndLaterHitsAreNotHalved()
    {
        var ledger = new SoulLedger();
        long id = ledger.BeginCast();
        for (int i = 1; i <= 6; i++)
            Assert.Equal(SoulFormula.SpellHitSoul, ledger.Hit(id, i));
    }

    [Fact]
    public void DuplicateSegmentDoesNotPay()
    {
        var ledger = new SoulLedger();
        long id = ledger.BeginCast();
        Assert.Equal(9, ledger.Hit(id, 1));
        for (int i = 0; i < 20; i++) Assert.Equal(0, ledger.Hit(id, 1));
        Assert.Equal(9, ledger.Hit(id, 2));
        Assert.Equal(9, ledger.Hit(id, 3));
        Assert.Equal(9, ledger.Hit(id, 4));
    }

    [Fact]
    public void OverlappingCastsHaveIndependentSegments()
    {
        var ledger = new SoulLedger();
        long a = ledger.BeginCast(), b = ledger.BeginCast();
        for (int i = 0; i < 3; i++) Assert.Equal(9, ledger.Hit(a, i));
        Assert.Equal(9, ledger.Hit(b, 0));
        Assert.Equal(9, ledger.Hit(a, 3));
        Assert.Equal(0, ledger.Hit(b, 0));
    }

    [Fact]
    public void CharmStackIsSettledPerHitOnTheLedger()
    {
        var ledger = new SoulLedger();
        long id = ledger.BeginCast();
        Assert.Equal(12, ledger.Hit(id, 0, soulCatcher: true));
        Assert.Equal(17, ledger.Hit(id, 1, soulEater: true));
        Assert.Equal(20, ledger.Hit(id, 2, soulCatcher: true, soulEater: true));
        Assert.Equal(9, ledger.Hit(id, 3));
    }

    [Fact]
    public void ResetInvalidatesOldCastsAndAllTransientState()
    {
        var ledger = new SoulLedger();
        var accumulator = new SoulAccumulator();
        long old = ledger.BeginCast();
        accumulator.Add(9);
        ledger.Reset();
        accumulator.Reset();
        Assert.Equal(0, ledger.ActiveCastCount);
        Assert.Equal(0, accumulator.Remainder);
        long fresh = ledger.BeginCast();
        Assert.NotEqual(old, fresh);
        Assert.Equal(0, ledger.Hit(old, 0));
        Assert.Equal(9, ledger.Hit(fresh, 0));
        ledger.EndCast(fresh);
        Assert.Equal(0, ledger.Hit(fresh, 1));
    }

    [Fact]
    public void FractionalSoulIsConservedForHalfNailSoul()
    {
        var accumulator = new SoulAccumulator();
        int total = 0;
        for (int i = 0; i < 4; i++) total += accumulator.Add(SoulFormula.SpellHitSoul);
        Assert.Equal(36, total);
        Assert.Equal(0, accumulator.Remainder);
        Assert.Equal(5, accumulator.Add(11 / 2.0));
        Assert.Equal(6, accumulator.Add(11 / 2.0));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidNumbersCannotPoisonState(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Numbers.RequireFiniteNonnegative(value, "value"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SoulAccumulator().Add(value));
        var accumulator = new SoulAccumulator();
        Assert.Equal(9, accumulator.Add(9));
        Assert.Equal(0, accumulator.Remainder);
    }

    [Fact]
    public void InvalidHitDoesNotConsumeItsSegment()
    {
        var ledger = new SoulLedger();
        long id = ledger.BeginCast();
        Assert.Throws<ArgumentOutOfRangeException>(() => ledger.Hit(id, -1));
        Assert.Equal(9, ledger.Hit(id, 0));
        Assert.Equal(0, ledger.Hit(999, 0));
        Assert.Equal(0, ledger.Hit(id, 0));
    }

    [Fact]
    public void RejectedAccumulatorOverflowPreservesFractionalSoul()
    {
        var accumulator = new SoulAccumulator();
        Assert.Equal(0, accumulator.Add(.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => accumulator.Add(int.MaxValue));
        Assert.Equal(.5, accumulator.Remainder);
        Assert.Equal(1, accumulator.Add(.5));
    }

    [Fact]
    public void ChangingSoulCharmsCannotRepayAnAlreadyRewardedSegment()
    {
        var ledger = new SoulLedger();
        long cast = ledger.BeginCast();
        Assert.Equal(9, ledger.Hit(cast, 0));
        Assert.Equal(0, ledger.Hit(cast, 0, true, true));
        Assert.Equal(20, ledger.Hit(cast, 1, true, true));
    }
}
