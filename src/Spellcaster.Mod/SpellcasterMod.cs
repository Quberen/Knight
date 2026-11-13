using System;
using System.Collections.Generic;
using Modding;
using UnityEngine;
using Spellcaster.Core;

namespace Spellcaster.Mod;

public sealed class Settings
{
    public bool SoulCollectorEnabled = true;
    public bool MageGripEnabled;
    public int MageGripNotchCost = 1;
    public bool ShamanMasterEnabled;
    public int ShamanMasterNotchCost = 3;
    public bool FastCastingEnabled;
    // null: use the live vanilla INVUL_TIME_QUAKE at each dive.
    public double? QuakeInvulnerabilitySeconds;
}

public sealed class SpellcasterMod : Modding.Mod, ITogglableMod, IGlobalSettings<Settings>, IMenuMod
{
    private Settings settings = new Settings();
    private SpellRuntime? runtime;
    public SpellcasterMod() : base("Spellcaster") { }
    public override string GetVersion() => "0.3.0";
    public bool ToggleButtonInsideMenu => false;

    public override void Initialize()
    {
        if (runtime != null) return;
        Clamp(settings);
        var host = new GameObject("Spellcaster.Runtime");
        UnityEngine.Object.DontDestroyOnLoad(host);
        runtime = host.AddComponent<SpellRuntime>();
        runtime.Install(settings);
        Log("Stage 3 initialized; collector=" + settings.SoulCollectorEnabled
            + " grip=" + settings.MageGripEnabled
            + " shaman=" + settings.ShamanMasterEnabled
            + " notches(grip=" + settings.MageGripNotchCost
            + ", shaman=" + settings.ShamanMasterNotchCost + ")"
            + " fastCast=" + settings.FastCastingEnabled
            + " quakeInvuln=" + (settings.QuakeInvulnerabilitySeconds.HasValue
                ? settings.QuakeInvulnerabilitySeconds.Value.ToString() : "native")
            + "; API v77 / HK 1.5.78.11833.");
    }

    public void Unload()
    {
        if (runtime == null) return;
        runtime.Uninstall();
        UnityEngine.Object.Destroy(runtime.gameObject);
        runtime = null;
    }

    public void OnLoadGlobal(Settings value)
    {
        settings = value ?? new Settings();
        Clamp(settings);
        runtime?.ApplySettings(settings);
    }
    public Settings OnSaveGlobal() => settings;

    private static void Clamp(Settings value)
    {
        value.MageGripNotchCost = Math.Max(1, Math.Min(2, value.MageGripNotchCost));
        value.ShamanMasterNotchCost = Math.Max(1, Math.Min(5, value.ShamanMasterNotchCost));
        if (value.QuakeInvulnerabilitySeconds.HasValue &&
            !QuakeInvulnerability.IsValid(value.QuakeInvulnerabilitySeconds.Value))
            value.QuakeInvulnerabilitySeconds = null;
    }

    public List<IMenuMod.MenuEntry> GetMenuData(IMenuMod.MenuEntry? toggleButtonEntry) => new List<IMenuMod.MenuEntry>
    {
        new IMenuMod.MenuEntry("Soul Collector / 灵魂收集者", new[] { "Off / 关闭", "On / 开启" },
            "Temporary charm: 9 soul per spell hit (flat, stacks with Catcher/Eater), half nail soul. No notch cost yet.",
            i => { settings.SoulCollectorEnabled = i == 1; if (runtime != null) runtime.SetCollector(i == 1); },
            () => settings.SoulCollectorEnabled ? 1 : 0),
        new IMenuMod.MenuEntry("Mage's Grip / 法师之握", new[] { "Off / 关闭", "On / 开启" },
            "Temporary charm: release attack for a small soul; hold 0.45s for a 25-soul spell. No real notch cost.",
            i => { settings.MageGripEnabled = i == 1; runtime?.SetGrip(i == 1); },
            () => settings.MageGripEnabled ? 1 : 0),
        new IMenuMod.MenuEntry("Mage's Grip notches / 法师之握槽位", new[] { "1", "2" },
            "Planned notch cost, saved in Settings; this menu switch does not occupy inventory notches yet.",
            i => settings.MageGripNotchCost = i + 1,
            () => settings.MageGripNotchCost - 1),
        new IMenuMod.MenuEntry("Shaman Master / 萨满大师", new[] { "Off / 关闭", "On / 开启" },
            "Requires Mage's Grip. Hold to the second threshold for a 48-soul (36 with Spell Twister) charged spell: damage x2, larger range.",
            i => { runtime?.ResetCharge(); settings.ShamanMasterEnabled = i == 1; },
            () => settings.ShamanMasterEnabled ? 1 : 0),
        new IMenuMod.MenuEntry("Shaman Master notches / 萨满大师槽位", new[] { "1", "2", "3", "4", "5" },
            "Saved/displayed only; does not occupy real charm notches. Default 3.",
            i => settings.ShamanMasterNotchCost = i + 1,
            () => settings.ShamanMasterNotchCost - 1),
        new IMenuMod.MenuEntry("Fast Casting timing / 极速施法时间测试", new[] { "Off / 关闭", "On / 开启" },
            "Temporary: second charge time x1.5 ONLY. No damage, interval or other spell changes.",
            i => { runtime?.ResetCharge(); settings.FastCastingEnabled = i == 1; },
            () => settings.FastCastingEnabled ? 1 : 0),
        new IMenuMod.MenuEntry("Quake invulnerability / 下砸无敌秒数",
            new[] { "Native runtime / 原版运行时", "0", "0.2", "0.4", "0.6", "0.8", "1.0", CustomLabel() },
            "Global, all dives. Settings JSON accepts any finite nonnegative duration; native floor breaking is preserved. Default is the live vanilla INVUL_TIME_QUAKE.",
            i => { if (i < 7) settings.QuakeInvulnerabilitySeconds = i == 0 ? (double?)null : (i - 1) * 0.2; },
            () => QuakeMenuIndex())
    };

    private string CustomLabel()
    {
        if (!settings.QuakeInvulnerabilitySeconds.HasValue) return "Saved custom / 自定义";
        return "Saved custom / 自定义: " + settings.QuakeInvulnerabilitySeconds.Value;
    }

    private int QuakeMenuIndex()
    {
        if (!settings.QuakeInvulnerabilitySeconds.HasValue) return 0;
        double seconds = settings.QuakeInvulnerabilitySeconds.Value;
        for (int i = 0; i <= 5; i++)
            if (Math.Abs(seconds - i * 0.2) < 0.000001) return i + 1;
        return 7;
    }
}
