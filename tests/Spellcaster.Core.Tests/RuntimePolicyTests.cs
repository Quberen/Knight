using System.IO;
using Xunit;

namespace Spellcaster.Core.Tests;

public class RuntimePolicyTests
{
    private static string RuntimeSource(string name)
    {
        var root = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Spellcaster.sln"))) root = root.Parent;
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine(root!.FullName, "src/Spellcaster.Mod", name + ".cs"));
    }

    [Fact]
    public void GripVisualsUseLiveSoulAndTwisterForSecondFlash()
    {
        string source = RuntimeSource("MageGripRuntime");
        Assert.Contains("int soul = PlayerData.instance.GetInt(\"MPCharge\");", source);
        Assert.Contains("bool twister = PlayerData.instance.GetBool(\"equippedCharm_33\");", source);
        Assert.Contains("attack.StoredTier(now, shaman, stone, fast, soul, twister)", source);
        Assert.Contains("attack.Winding(now, shaman, stone, fast, soul, twister)", source);
    }

    [Fact]
    public void GripPendingAndPrepaidKindReachSpawnContextWithoutReclassification()
    {
        string grip = RuntimeSource("MageGripRuntime");
        string spells = RuntimeSource("SpellRuntime");
        Assert.Contains("TryFire(self, pendingDirection, levelName, pendingRelease)", grip);
        Assert.Contains("prepaidKind = GripAttack.Kind(release);", grip);
        Assert.Contains("? (prepaidKind, prepaidShamanStone)", grip);
        Assert.Contains("context = BeginCast(recipe.Item1);", spells);
        Assert.Contains("spawnContext = context ?? previous;", spells);
        Assert.Contains("Bind(obj, spawnContext, 0)", spells);
        Assert.Contains("if (Active) return;\n        orig(self);", grip);
    }

    // Unity cannot run in the Core test host: lock the runtime policy separately
    // from SoulLedger's deliberately equipment-independent arithmetic.
    [Fact]
    public void RuntimeWithholdsOrdinaryRefundsWithoutCollector()
    {
        var root = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Spellcaster.sln"))) root = root.Parent;
        Assert.NotNull(root);
        string source = File.ReadAllText(Path.Combine(root!.FullName, "src/Spellcaster.Mod/SpellRuntime.cs"));
        Assert.Contains("installed && (collector || grip != null)", source);
        Assert.Contains("if (small ? grip == null : !collector) return;", source);
        Assert.Contains("self.hp < hpBefore", source);
    }
}
