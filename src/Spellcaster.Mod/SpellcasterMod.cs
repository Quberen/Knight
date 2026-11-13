using System;
using System.Collections.Generic;
using Modding;
using UnityEngine;

namespace Spellcaster.Mod;

public sealed class Settings
{
    public bool SoulCollectorEnabled = true;
    public bool MageGripEnabled;
    public int MageGripNotchCost = 1;
}

public sealed class SpellcasterMod : Modding.Mod, ITogglableMod, IGlobalSettings<Settings>, IMenuMod
{
    private Settings settings = new Settings();
    private SpellRuntime? runtime;
    public SpellcasterMod() : base("Spellcaster") { }
    public override string GetVersion() => "0.2.0";
    public bool ToggleButtonInsideMenu => false;

    public override void Initialize()
    {
        if (runtime != null) return;
        var host = new GameObject("Spellcaster.Runtime");
        UnityEngine.Object.DontDestroyOnLoad(host);
        runtime = host.AddComponent<SpellRuntime>();
        runtime.Install(settings.SoulCollectorEnabled, settings.MageGripEnabled);
        Log("Stage 2 initialized; API v77 / HK 1.5.78.11833.");
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
        settings.MageGripNotchCost = Math.Max(1, Math.Min(2, settings.MageGripNotchCost));
        if (runtime != null)
        {
            runtime.SetCollector(settings.SoulCollectorEnabled);
            runtime.SetGrip(settings.MageGripEnabled);
        }
    }
    public Settings OnSaveGlobal() => settings;
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
            () => settings.MageGripNotchCost - 1)
    };
}
