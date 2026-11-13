using System;
using System.Collections.Generic;
using Modding;
using UnityEngine;

namespace Spellcaster.Mod;

public sealed class Settings
{
    public bool SoulCollectorEnabled = true;
}

public sealed class SpellcasterMod : Modding.Mod, ITogglableMod, IGlobalSettings<Settings>, IMenuMod
{
    private Settings settings = new Settings();
    private SpellRuntime? runtime;
    public SpellcasterMod() : base("Spellcaster") { }
    public override string GetVersion() => "0.1.0";
    public bool ToggleButtonInsideMenu => false;

    public override void Initialize()
    {
        if (runtime != null) return;
        var host = new GameObject("Spellcaster.Runtime");
        UnityEngine.Object.DontDestroyOnLoad(host);
        runtime = host.AddComponent<SpellRuntime>();
        runtime.Install(settings.SoulCollectorEnabled);
        Log("Stage 1 initialized; API v77 / HK 1.5.78.11833.");
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
        if (runtime != null) runtime.SetCollector(settings.SoulCollectorEnabled);
    }
    public Settings OnSaveGlobal() => settings;
    public List<IMenuMod.MenuEntry> GetMenuData(IMenuMod.MenuEntry? toggleButtonEntry) => new List<IMenuMod.MenuEntry>
    {
        new IMenuMod.MenuEntry("Soul Collector / 灵魂收集者", new[] { "Off / 关闭", "On / 开启" },
            "Stage 1 temporary charm switch; spell soul gain, half nail soul. No notch cost yet.",
            i => { settings.SoulCollectorEnabled = i == 1; if (runtime != null) runtime.SetCollector(i == 1); },
            () => settings.SoulCollectorEnabled ? 1 : 0)
    };
}
