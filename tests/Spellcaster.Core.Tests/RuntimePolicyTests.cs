using System.IO;
using Xunit;

namespace Spellcaster.Core.Tests;

public class RuntimePolicyTests
{
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
