using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spellcaster.Core;
using Xunit;

namespace Spellcaster.Core.Tests;

public class SpellCharmsTests
{
    [Fact]
    public void ChargeDescriptionsExplainBothTiersAndNewTimesInBothLanguages()
    {
        foreach (bool chinese in new[] { true, false })
        {
            string grip = SpellCharms.DisplayDescription(41, chinese);
            string shaman = SpellCharms.DisplayDescription(42, chinese);
            foreach (string value in new[] { "0.675", "0.3", "25", "×2" }) Assert.Contains(value, grip);
            foreach (string value in new[] { "2.25", "1.65", "3.375", "2.475", "×2", "×1.45", "48", "36" }) Assert.Contains(value, shaman);
            Assert.Contains(chinese ? "原版" : "vanilla", grip);
            Assert.Contains(chinese ? "暗影之魂" : "Shade Soul", grip);
            Assert.Contains(chinese ? "只有第二段" : "only the second", shaman);
            Assert.Contains(chinese ? "魂足够" : "enough soul", shaman);
        }
    }

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
    public void DisplayTextIsSingleLanguageAndDoesNotMix()
    {
        foreach (int id in SpellCharms.Ids)
        {
            string cnName = SpellCharms.DisplayName(id, chinese: true);
            string enName = SpellCharms.DisplayName(id, chinese: false);
            Assert.Equal(SpellCharms.ChineseName(id), cnName);
            Assert.Equal(SpellCharms.EnglishName(id), enName);
            Assert.DoesNotContain('/', cnName);
            Assert.DoesNotContain(enName, cnName);
            Assert.DoesNotContain(cnName, enName);

            string cnDesc = SpellCharms.DisplayDescription(id, chinese: true);
            string enDesc = SpellCharms.DisplayDescription(id, chinese: false);
            Assert.Equal(SpellCharms.ChineseDescription(id), cnDesc);
            Assert.Equal(SpellCharms.EnglishDescription(id), enDesc);
            Assert.DoesNotContain("\n\n", cnDesc);
            Assert.DoesNotContain("\n\n", enDesc);
            Assert.True(cnDesc.Length > 10);
            Assert.True(enDesc.Length > 20);
            // No leftover bilingual glue.
            Assert.DoesNotContain("Tap attack", cnDesc);
            Assert.DoesNotContain("Requires Mage", cnDesc);
            Assert.DoesNotContain("轻点", enDesc);
        }
        Assert.Equal("法师之握", SpellCharms.ChineseName(41));
        Assert.Equal("Mage's Grip", SpellCharms.EnglishName(41));
        Assert.Equal("萨满大师", SpellCharms.ChineseName(42));
        Assert.Equal("灵魂收集者", SpellCharms.ChineseName(43));
        Assert.Contains("快捷施法", SpellCharms.ChineseDescription(41));
        Assert.Contains("快捷施法", SpellCharms.ChineseDescription(42));
        Assert.Contains("Quick Cast", SpellCharms.EnglishDescription(41));
        Assert.Contains("Quick Cast", SpellCharms.EnglishDescription(42));
        Assert.DoesNotContain("禁用骨钉技艺", SpellCharms.ChineseDescription(41));
        Assert.DoesNotContain("Nail arts", SpellCharms.EnglishDescription(41));
        Assert.DoesNotContain("按住攻击", SpellCharms.ChineseDescription(41));
        Assert.DoesNotContain("按住攻击", SpellCharms.ChineseDescription(42));
        Assert.DoesNotContain("Tap attack", SpellCharms.EnglishDescription(41));
        Assert.DoesNotContain("Hold attack", SpellCharms.EnglishDescription(42));
    }

    [Theory]
    [InlineData("ZH", true)]
    [InlineData("zh", true)]
    [InlineData("ZH_CN", true)]
    [InlineData("ZH_TW", true)]
    [InlineData("EN", false)]
    [InlineData("FR", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsChineseLanguageRecognizesZhCodes(string? code, bool expected) =>
        Assert.Equal(expected, SpellCharms.IsChineseLanguage(code));

    [Theory]
    [InlineData("EN", "ZH", false)]   // in-session switch to EN must not stay Chinese because GameManager is still ZH
    [InlineData("ZH", "EN", true)]    // in-session switch to ZH must be Chinese even if GameManager is still EN
    [InlineData("ZH_CN", "EN", true)]
    [InlineData("EN", "EN", false)]
    [InlineData("ZH", "ZH", true)]
    [InlineData(null, "ZH", true)]    // fallback only when CurrentLanguage is unavailable
    [InlineData("", "ZH", true)]
    [InlineData(null, "EN", false)]
    [InlineData(null, null, false)]
    public void ResolveIsChinesePrefersLiveCurrentLanguage(string? current, string? saved, bool expected) =>
        Assert.Equal(expected, SpellCharms.ResolveIsChinese(current, saved));


    [Fact]
    public void EveryChineseGlyphIsInTheVanillaZhCorpus()
    {
        string path = Path.Combine(Path.GetDirectoryName(typeof(SpellCharmsTests).Assembly.Location)!,
            "Data", "vanilla-zh-glyphs.txt");
        Assert.True(File.Exists(path), "vanilla-zh-glyphs.txt is missing from the test output.");
        var glyphs = new HashSet<char>(File.ReadAllText(path));
        var missing = new List<string>();
        foreach (int id in SpellCharms.Ids)
        {
            foreach (string s in new[] { SpellCharms.ChineseName(id), SpellCharms.ChineseDescription(id) })
            {
                foreach (char ch in s)
                {
                    if (ch < 0x4E00 || ch > 0x9FFF) continue;
                    if (!glyphs.Contains(ch)) missing.Add($"{id}:{ch} U+{(int)ch:X4}");
                }
            }
        }
        Assert.True(missing.Count == 0, "Tofu-risk glyphs not in vanilla ZH corpus: " + string.Join(", ", missing.Distinct()));
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

    [Fact]
    public void DefaultIconsMatchUserPick()
    {
        // Mage's Grip = C (warm gold), Shaman Master = A (amber), Soul Collector = A (soul teal).
        Assert.Equal(2, SpellCharms.DefaultIconVariant(41));
        Assert.Equal(0, SpellCharms.DefaultIconVariant(42));
        Assert.Equal(0, SpellCharms.DefaultIconVariant(43));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(9, 2)]
    public void IconVariantClampsToThreeChoices(int raw, int expected) =>
        Assert.Equal(expected, SpellCharms.ClampVariant(raw));

    [Theory]
    [InlineData(0f, -2f, .55f)]
    [InlineData(0f, -1f, .45f)]
    [InlineData(0f, -.55f, 0f)]
    [InlineData(0f, -.2f, 0f)]
    [InlineData(-5f, -7f, .55f)]
    public void FifthRowLiftPreservesMinimumGap(float fourth, float fifth, float expected)
    {
        Assert.Equal(.55f, CharmBoardLayout.FifthLift);
        Assert.Equal(.55f, CharmBoardLayout.MinGap);
        Assert.Equal(expected, CharmBoardLayout.Lift(fourth, fifth), 5);
    }
}
