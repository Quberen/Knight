using System;
using System.Collections.Generic;
using Modding;
using UnityEngine;
using Spellcaster.Core;

namespace Spellcaster.Mod;

public sealed class Settings
{
    public int MageGripNotchCost = SpellCharms.DefaultGripCost;
    public int ShamanMasterNotchCost = SpellCharms.DefaultShamanCost;
    public int SoulCollectorNotchCost = SpellCharms.DefaultCollectorCost;
    public bool FastCastingEnabled;
    // 0 = use earned vanilla notches; 12–16 = requested total (never below earned, cap 16).
    public int CharmNotchTotal;
    // Kept for old GlobalSettings.json; charms are owned by default on every save.
    // The debug menu no longer grants or removes them.
    public bool GiveSpellcasterCharms;
    // 0–2, picks one of the recolored vanilla-sprite variants.
    public int CharmIconVariant;
    // null: use the live vanilla INVUL_TIME_QUAKE at each dive.
    public double? QuakeInvulnerabilitySeconds;
}

public sealed class SpellcasterMod : Modding.Mod, ITogglableMod, IGlobalSettings<Settings>, ILocalSettings<LocalSettings>, IMenuMod
{
    private Settings settings = new Settings();
    private LocalSettings local = new LocalSettings();
    private SpellRuntime? runtime;
    public SpellcasterMod() : base("Spellcaster") { }
    public override string GetVersion() => "0.6.1";
    public bool ToggleButtonInsideMenu => false;

    public override void Initialize()
    {
        if (runtime != null) return;
        Clamp(settings);
        var host = new GameObject("Spellcaster.Runtime");
        UnityEngine.Object.DontDestroyOnLoad(host);
        runtime = host.AddComponent<SpellRuntime>();
        runtime.Install(settings);
        runtime.InstallCharms(() => local, value => local = value ?? new LocalSettings());
        Log("Stage 5 (real charms) initialized; charms owned by default on every save;"
            + " icons(grip=C, shaman=A, collector=A)"
            + " costs(grip=" + settings.MageGripNotchCost
            + ", shaman=" + settings.ShamanMasterNotchCost
            + ", collector=" + settings.SoulCollectorNotchCost
            + ", charmTotal=" + (settings.CharmNotchTotal == 0 ? "vanilla" : settings.CharmNotchTotal.ToString()) + ")"
            + " equipped(grip=" + local.MageGrip.Equipped
            + ", shaman=" + local.ShamanMaster.Equipped
            + ", collector=" + local.SoulCollector.Equipped + ")"
            + " shadowChain=Dashmaster+ShamanStone"
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

    public void OnLoadLocal(LocalSettings value)
    {
        local = value ?? new LocalSettings();
        runtime?.Charms?.ApplySettings();
    }
    public LocalSettings OnSaveLocal() => local;

    private static void Clamp(Settings value)
    {
        value.MageGripNotchCost = SpellCharms.ClampCost(SpellCharms.MageGripId, value.MageGripNotchCost);
        value.ShamanMasterNotchCost = SpellCharms.ClampCost(SpellCharms.ShamanMasterId, value.ShamanMasterNotchCost);
        value.SoulCollectorNotchCost = SpellCharms.ClampCost(SpellCharms.SoulCollectorId, value.SoulCollectorNotchCost);
        value.CharmNotchTotal = CharmNotches.ClampTotal(value.CharmNotchTotal);
        value.CharmIconVariant = SpellCharms.ClampVariant(value.CharmIconVariant);
        if (value.QuakeInvulnerabilitySeconds.HasValue &&
            !QuakeInvulnerability.IsValid(value.QuakeInvulnerabilitySeconds.Value))
            value.QuakeInvulnerabilitySeconds = null;
    }

    public List<IMenuMod.MenuEntry> GetMenuData(IMenuMod.MenuEntry? toggleButtonEntry) => new List<IMenuMod.MenuEntry>
    {
        new IMenuMod.MenuEntry("Debug: give Spellcaster charms / 调试：给予法术流护符",
            new[] { "Off / 关闭", "Give / 给予" },
            "The three Spellcaster charms are already owned on every save (not auto-equipped). This leftover debug switch is harmless: it does not grant or remove them.",
            i => { settings.GiveSpellcasterCharms = i == 1; },
            () => settings.GiveSpellcasterCharms ? 1 : 0),
        new IMenuMod.MenuEntry("Mage's Grip notches / 法师之握槽位", new[] { "1", "2" },
            "Notch cost counted toward overcharm when the charm is equipped. Default 1 (DESIGN: 1 or 2).",
            i => { settings.MageGripNotchCost = i + 1; runtime?.Charms?.ApplySettings(); },
            () => settings.MageGripNotchCost - 1),
        new IMenuMod.MenuEntry("Shaman Master notches / 萨满大师槽位", new[] { "1", "2", "3", "4", "5" },
            "Notch cost counted toward overcharm when equipped. Default 3.",
            i => { settings.ShamanMasterNotchCost = i + 1; runtime?.Charms?.ApplySettings(); },
            () => settings.ShamanMasterNotchCost - 1),
        new IMenuMod.MenuEntry("Soul Collector notches / 灵魂收集者槽位", new[] { "1", "2", "3" },
            "Notch cost counted toward overcharm when equipped. Default 2 (DESIGN.md).",
            i => { settings.SoulCollectorNotchCost = i + 1; runtime?.Charms?.ApplySettings(); },
            () => settings.SoulCollectorNotchCost - 1),
        new IMenuMod.MenuEntry("Fast Casting timing / 极速施法时间测试", new[] { "Off / 关闭", "On / 开启" },
            "Temporary: second charge time x1.5 ONLY. No damage, interval or other spell changes.",
            i => { runtime?.ResetCharge(); settings.FastCastingEnabled = i == 1; },
            () => settings.FastCastingEnabled ? 1 : 0),
        new IMenuMod.MenuEntry("Charm notches / 符咒槽总数",
            new[] { "Vanilla / 原版", "12", "13", "14", "15", "16" },
            "Total equipped-charm notches, up to 16. Vanilla uses what you have earned (cap 11). Never below earned. GetPlayerIntHook only: PlayerData.charmSlots is never written. Extra pips are cloned onto the charm board when possible.",
            i => { settings.CharmNotchTotal = i == 0 ? 0 : i + 11; runtime?.ApplySettings(settings); },
            () => settings.CharmNotchTotal == 0 ? 0 : settings.CharmNotchTotal - 11),
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
