using Spellcaster.Core;
using Xunit;

namespace Spellcaster.Core.Tests;

public class ImprovedKingSoulTests
{
    [Theory]
    [InlineData(-5, 6)]
    [InlineData(0, 6)]
    [InlineData(23, 6)]
    [InlineData(24, 3)]
    [InlineData(65, 3)]
    [InlineData(66, 2)]
    [InlineData(99, 2)]
    [InlineData(200, 2)]
    public void RateTableFollowsDesignBands(int soul, double expected) =>
        Assert.Equal(expected, ImprovedKingSoul.RatePerSecond(soul));

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public void PrerequisiteIsExactlyVanillaKingsoulState(int state, bool expected) =>
        Assert.Equal(expected, ImprovedKingSoul.PrerequisiteMet(state));

    [Theory]
    [InlineData(0, 1, 6)]
    [InlineData(0, 2, 12)]
    [InlineData(40, 1, 3)]
    [InlineData(40, 2, 6)]
    [InlineData(80, 1, 2)]
    [InlineData(80, 2, 4)]
    public void WholeSecondsInASingleBandGrantTheBandRate(int soul, double seconds, int expected)
    {
        var regen = new KingSoulRegen();
        Assert.Equal(expected, regen.Tick(seconds, soul, 999, true));
        Assert.Equal(0, regen.Remainder, 9);
    }

    [Fact]
    public void CrossingFromTwentyThreeSplitsIntoTheMidBand()
    {
        var regen = new KingSoulRegen();
        Assert.Equal(1, regen.Tick(1.0 / 6.0, 23, 999, true));
        Assert.Equal(0, regen.Remainder, 6);
        Assert.Equal(3, regen.Tick(1.0, 24, 999, true));

        var once = new KingSoulRegen();
        Assert.Equal(4, once.Tick(1.0 / 6.0 + 1.0, 23, 999, true));
    }

    [Fact]
    public void StopsAtMaxGrantEvenInTheFastBand()
    {
        var regen = new KingSoulRegen();
        Assert.Equal(2, regen.Tick(1.0, 97, 2, true));
        Assert.Equal(0, regen.Remainder, 9);
        Assert.Equal(0, regen.Tick(1.0, 99, 0, true));
    }

    [Fact]
    public void InactiveOrZeroTimeGrantsNothing()
    {
        var regen = new KingSoulRegen();
        Assert.Equal(0, regen.Tick(2.0, 0, 999, false));
        Assert.Equal(0, regen.Remainder);
        Assert.Equal(0, regen.Tick(0, 0, 999, true));
        Assert.Equal(6, regen.Tick(1.0, 0, 999, true));
    }

    [Fact]
    public void FrameDivisionMatchesASingleTick()
    {
        var one = new KingSoulRegen();
        int a = one.Tick(1.0, 0, 999, true);

        var many = new KingSoulRegen();
        int b = 0;
        for (int i = 0; i < 60; i++) b += many.Tick(1.0 / 60.0, 0 + b, 999 - b, true);

        Assert.Equal(6, a);
        Assert.Equal(6, b);
        Assert.Equal(one.Remainder, many.Remainder, 6);
    }

    [Fact]
    public void MidBandFrameDivisionIsStable()
    {
        var one = new KingSoulRegen();
        int a = one.Tick(2.0, 40, 999, true);
        var many = new KingSoulRegen();
        int b = 0;
        for (int i = 0; i < 120; i++) b += many.Tick(2.0 / 120.0, 40 + b, 999 - b, true);
        Assert.Equal(6, a);
        Assert.Equal(6, b);
    }

    [Fact]
    public void ResetClearsRemainder()
    {
        var regen = new KingSoulRegen();
        Assert.Equal(0, regen.Tick(0.1, 0, 999, true)); // 0.6 soul
        Assert.True(regen.Remainder > 0);
        regen.Reset();
        Assert.Equal(0, regen.Remainder);
    }

    [Fact]
    public void ConstantsMatchDesign()
    {
        Assert.Equal(23, ImprovedKingSoul.LowBandMax);
        Assert.Equal(65, ImprovedKingSoul.MidBandMax);
        Assert.Equal(6, ImprovedKingSoul.LowRate);
        Assert.Equal(3, ImprovedKingSoul.MidRate);
        Assert.Equal(2, ImprovedKingSoul.HighRate);
        Assert.Equal(3, ImprovedKingSoul.VanillaKingsoulState);
        Assert.Equal(5, SpellCharms.DefaultCost(SpellCharms.ImprovedKingSoulId));
    }
}
