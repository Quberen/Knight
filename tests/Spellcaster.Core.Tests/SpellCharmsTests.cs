using Spellcaster.Core;
using Xunit;

namespace Spellcaster.Core.Tests;

public class SpellCharmsTests
{
    [Fact]
    public void IdsAreFortyOneThroughFortyThreeAndDoNotOverlapVanilla()
    {
        Assert.Equal(new[] { 41, 42, 43 }, SpellCharms.Ids);
        Assert.Equal(3, SpellCharms.Count);
        foreach (int id in SpellCharms.Ids)
        {
            Assert.True(SpellCharms.IsOurs(id));
            Assert.True(id > 40);
        }
        Assert.False(SpellCharms.IsOurs(40));
        Assert.False(SpellCharms.IsOurs(44));
        Assert.Equal(0, SpellCharms.IndexOf(41));
        Assert.Equal(2, SpellCharms.IndexOf(43));
    }

    [Theory]
    [InlineData("gotCharm_41", 41)]
    [InlineData("equippedCharm_42", 42)]
    [InlineData("newCharm_43", 43)]
    [InlineData("charmCost_41", 41)]
    [InlineData("CHARM_NAME_42", 42)]
    [InlineData("CHARM_DESC_43", 43)]
    public void TryParseRecognizesOurKeys(string key, int id)
    {
        Assert.True(SpellCharms.TryParse(key, out int parsed));
        Assert.Equal(id, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("gotCharm_40")]
    [InlineData("gotCharm_44")]
    [InlineData("charmSlots")]
    [InlineData("equippedCharm_16")]
    [InlineData("CHARM_NAME_19")]
    public void TryParseRejectsVanillaAndUnknown(string? key)
    {
        Assert.False(SpellCharms.TryParse(key, out _));
    }

    [Fact]
    public void NamesAndDescriptionsAreBilingualAndNonEmpty()
    {
        foreach (int id in SpellCharms.Ids)
        {
            Assert.Contains(SpellCharms.ChineseName(id), SpellCharms.DisplayName(id));
            Assert.Contains(SpellCharms.EnglishName(id), SpellCharms.DisplayName(id));
            Assert.Contains('/', SpellCharms.DisplayName(id));
            string desc = SpellCharms.DisplayDescription(id);
            Assert.Contains("\n\n", desc);
            Assert.True(desc.Length > 40);
        }
        Assert.Equal("法师之握", SpellCharms.ChineseName(41));
        Assert.Equal("Mage's Grip", SpellCharms.EnglishName(41));
        Assert.Equal("萨满大师", SpellCharms.ChineseName(42));
        Assert.Equal("灵魂收集者", SpellCharms.ChineseName(43));
    }

    [Theory]
    [InlineData(41, 0, 1)]
    [InlineData(41, 1, 1)]
    [InlineData(41, 2, 2)]
    [InlineData(41, 9, 2)]
    [InlineData(42, 0, 3)]
    [InlineData(42, 3, 3)]
    [InlineData(42, 5, 5)]
    [InlineData(42, 9, 5)]
    [InlineData(43, 0, 2)]
    [InlineData(43, 2, 2)]
    [InlineData(43, 3, 3)]
    [InlineData(43, 9, 3)]
    public void CostsFollowDesignDefaultsAndClamp(int id, int raw, int expected) =>
        Assert.Equal(expected, SpellCharms.ClampCost(id, raw));

    [Fact]
    public void DefaultCostsMatchDesign()
    {
        Assert.Equal(1, SpellCharms.DefaultCost(41));
        Assert.Equal(3, SpellCharms.DefaultCost(42));
        Assert.Equal(2, SpellCharms.DefaultCost(43));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(9, 2)]
    public void IconVariantClampsToThreeChoices(int raw, int expected) =>
        Assert.Equal(expected, SpellCharms.ClampVariant(raw));
}
