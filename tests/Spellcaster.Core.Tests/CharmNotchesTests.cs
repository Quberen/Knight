using Spellcaster.Core;
using Xunit;

namespace Spellcaster.Core.Tests;

public class CharmNotchesTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 0)]
    [InlineData(11, 0)]
    [InlineData(-4, 0)]
    [InlineData(12, 12)]
    [InlineData(16, 16)]
    [InlineData(17, 16)]
    [InlineData(99, 16)]
    public void ClampTotalTreatsVanillaCapOrBelowAsVanillaAndCapsAtSixteen(int input, int expected) =>
        Assert.Equal(expected, CharmNotches.ClampTotal(input));

    [Theory]
    [InlineData(3, 0, 3)]
    [InlineData(8, 0, 8)]
    [InlineData(11, 0, 11)]
    [InlineData(8, 12, 12)]
    [InlineData(11, 12, 12)]
    [InlineData(11, 16, 16)]
    [InlineData(5, 16, 16)]
    [InlineData(11, 99, 16)]
    [InlineData(0, 16, 16)]
    [InlineData(0, 0, 0)]
    [InlineData(-1, 12, 12)]
    [InlineData(11, 11, 11)]
    [InlineData(13, 0, 13)]
    [InlineData(20, 0, 16)]
    [InlineData(8, 10, 8)]
    public void EffectiveNeverBelowEarnedAndNeverAboveSixteen(int earned, int menu, int expected) =>
        Assert.Equal(expected, CharmNotches.Effective(earned, menu));
}
