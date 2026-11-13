using System;
using System.Collections.Generic;
using Modding;
using Spellcaster.Core;
using UnityEngine;

namespace Spellcaster.Mod;

/// <summary>Per-save ownership of one Spellcaster charm. Never written into PlayerData fields.</summary>
public sealed class CharmSave
{
    public bool Got;
    public bool Equipped;
    public bool New;
}

/// <summary>Local (per-save) charm flags. Safe when the mod is disabled: PlayerData is left vanilla.</summary>
public sealed class LocalSettings
{
    public CharmSave MageGrip = new CharmSave();
    public CharmSave ShamanMaster = new CharmSave();
    public CharmSave SoulCollector = new CharmSave();
}

/// <summary>
/// Wires the three Spellcaster charms into PlayerData hooks, notch accounting, and Mage's Grip /
/// Shaman Master / Soul Collector effects. The charm board UI is in <see cref="CharmBoard"/>.
/// </summary>
internal sealed class CharmRuntime
{
    private readonly SpellRuntime runtime;
    private readonly Func<Settings> settings;
    private readonly Func<LocalSettings> local;
    private readonly Action<LocalSettings> setLocal;
    private readonly CharmBoard board = new CharmBoard();
    private bool installed;
    private int savedFilled = -1;

    internal CharmRuntime(SpellRuntime runtime, Func<Settings> settings, Func<LocalSettings> local, Action<LocalSettings> setLocal)
    {
        this.runtime = runtime;
        this.settings = settings;
        this.local = local;
        this.setLocal = setLocal;
    }

    internal bool GripEquipped => Flag(SpellCharms.MageGripId).Equipped;
    internal bool ShamanEquipped => Flag(SpellCharms.ShamanMasterId).Equipped && GripEquipped;
    internal bool CollectorEquipped => Flag(SpellCharms.SoulCollectorId).Equipped;

    internal void Install()
    {
        if (installed) return;
        installed = true;
        ModHooks.LanguageGetHook += OnLanguage;
        ModHooks.GetPlayerBoolHook += OnGetBool;
        ModHooks.SetPlayerBoolHook += OnSetBool;
        ModHooks.GetPlayerIntHook += OnGetInt;
        On.PlayerData.CalculateNotchesUsed += OnCalculateNotches;
        On.PlayerData.EquipCharm += OnEquipCharm;
        On.PlayerData.UnequipCharm += OnUnequipCharm;
        On.BuildEquippedCharms.BuildCharmList += OnBuildCharmList;
        ModHooks.AfterSavegameLoadHook += OnAfterLoad;
        ModHooks.BeforeSavegameSaveHook += OnBeforeSave;
        ModHooks.SavegameSaveHook += OnAfterSave;
        ModHooks.NewGameHook += OnNewGame;
        board.Install();
        ApplyGive();
        SyncEffects();
    }

    internal void Uninstall()
    {
        if (!installed) return;
        installed = false;
        RestoreSaveSafety(PlayerData.instance, strip: true);
        ModHooks.LanguageGetHook -= OnLanguage;
        ModHooks.GetPlayerBoolHook -= OnGetBool;
        ModHooks.SetPlayerBoolHook -= OnSetBool;
        ModHooks.GetPlayerIntHook -= OnGetInt;
        On.PlayerData.CalculateNotchesUsed -= OnCalculateNotches;
        On.PlayerData.EquipCharm -= OnEquipCharm;
        On.PlayerData.UnequipCharm -= OnUnequipCharm;
        On.BuildEquippedCharms.BuildCharmList -= OnBuildCharmList;
        ModHooks.AfterSavegameLoadHook -= OnAfterLoad;
        ModHooks.BeforeSavegameSaveHook -= OnBeforeSave;
        ModHooks.SavegameSaveHook -= OnAfterSave;
        ModHooks.NewGameHook -= OnNewGame;
        board.Uninstall();
        runtime.SetGrip(false);
        runtime.SetCollector(false);
    }

    internal void ApplySettings()
    {
        ApplyGive();
        SyncEffects();
        board.RefreshIcons(settings().CharmIconVariant);
        if (PlayerData.instance != null) PlayerData.instance.CalculateNotchesUsed();
    }

    internal void GiveAll(bool asNew)
    {
        foreach (int id in SpellCharms.Ids)
        {
            var flag = Flag(id);
            // Only flash the "new" orb the first time the charm is granted.
            if (asNew) flag.New = !flag.Got;
            flag.Got = true;
        }
        if (PlayerData.instance != null)
            PlayerData.instance.SetBool("hasCharm", true);
        SyncEffects();
    }

    internal void TakeAll()
    {
        foreach (int id in SpellCharms.Ids)
        {
            Unequip(id);
            var flag = Flag(id);
            flag.Got = false;
            flag.Equipped = false;
            flag.New = false;
        }
        SyncEffects();
        if (PlayerData.instance != null) PlayerData.instance.CalculateNotchesUsed();
    }

    internal void SyncEffects()
    {
        runtime.SetCollector(CollectorEquipped);
        runtime.SetGrip(GripEquipped);
        runtime.ResetCharge();
    }

    private CharmSave Flag(int id) => id switch
    {
        SpellCharms.MageGripId => local().MageGrip,
        SpellCharms.ShamanMasterId => local().ShamanMaster,
        SpellCharms.SoulCollectorId => local().SoulCollector,
        _ => throw new ArgumentOutOfRangeException(nameof(id))
    };

    private int Cost(int id)
    {
        var s = settings();
        return id switch
        {
            SpellCharms.MageGripId => SpellCharms.ClampCost(id, s.MageGripNotchCost),
            SpellCharms.ShamanMasterId => SpellCharms.ClampCost(id, s.ShamanMasterNotchCost),
            SpellCharms.SoulCollectorId => SpellCharms.ClampCost(id, s.SoulCollectorNotchCost),
            _ => SpellCharms.DefaultCost(id)
        };
    }

    private int CustomFilled()
    {
        int n = 0;
        foreach (int id in SpellCharms.Ids)
            if (Flag(id).Equipped) n += Cost(id);
        return n;
    }

    private void ApplyGive()
    {
        if (settings().GiveSpellcasterCharms) GiveAll(asNew: true);
        else
        {
            // Only take charms that were granted by the debug switch when it flips off;
            // a loaded local save that already has Got stays until the player turns the switch off.
        }
    }

    private void Unequip(int id)
    {
        var flag = Flag(id);
        if (!flag.Equipped) return;
        flag.Equipped = false;
        if (PlayerData.instance != null)
        {
            var list = PlayerData.instance.GetVariable<List<int>>("equippedCharms");
            list?.Remove(id);
        }
    }

    private string OnLanguage(string key, string sheet, string orig)
    {
        if (SpellCharms.IsNameKey(key, out int id)) return SpellCharms.DisplayName(id);
        if (SpellCharms.IsDescKey(key, out id)) return SpellCharms.DisplayDescription(id);
        return orig;
    }

    private bool OnGetBool(string name, bool orig)
    {
        if (!SpellCharms.TryParse(name, out int id)) return orig;
        var flag = Flag(id);
        if (name.StartsWith("gotCharm_", StringComparison.Ordinal)) return flag.Got;
        if (name.StartsWith("equippedCharm_", StringComparison.Ordinal)) return flag.Equipped;
        if (name.StartsWith("newCharm_", StringComparison.Ordinal)) return flag.New;
        return orig;
    }

    private bool OnSetBool(string name, bool orig)
    {
        if (!SpellCharms.TryParse(name, out int id)) return orig;
        var flag = Flag(id);
        if (name.StartsWith("gotCharm_", StringComparison.Ordinal)) flag.Got = orig;
        else if (name.StartsWith("newCharm_", StringComparison.Ordinal)) flag.New = orig;
        else if (name.StartsWith("equippedCharm_", StringComparison.Ordinal))
        {
            flag.Equipped = orig;
            SyncEffects();
        }
        // Returning orig is fine: SetBoolInternal uses SetFieldSafe and no-ops on missing fields.
        return orig;
    }

    private int OnGetInt(string name, int orig)
    {
        if (SpellCharms.TryParse(name, out int id) && name.StartsWith("charmCost_", StringComparison.Ordinal))
            return Cost(id);
        return orig;
    }

    private void OnEquipCharm(On.PlayerData.orig_EquipCharm orig, PlayerData self, int charmNum)
    {
        orig(self, charmNum);
        if (SpellCharms.IsOurs(charmNum))
        {
            Flag(charmNum).Equipped = true;
            SyncEffects();
        }
    }

    private void OnUnequipCharm(On.PlayerData.orig_UnequipCharm orig, PlayerData self, int charmNum)
    {
        orig(self, charmNum);
        if (SpellCharms.IsOurs(charmNum))
        {
            Flag(charmNum).Equipped = false;
            SyncEffects();
        }
    }

    private void OnCalculateNotches(On.PlayerData.orig_CalculateNotchesUsed orig, PlayerData self)
    {
        orig(self);
        self.charmSlotsFilled += CustomFilled();
    }

    private void OnBuildCharmList(On.BuildEquippedCharms.orig_BuildCharmList orig, BuildEquippedCharms self)
    {
        try
        {
            board.Ensure(settings().CharmIconVariant);
        }
        catch (Exception e) { Modding.Logger.LogError("[Spellcaster] charm board: " + e); }
        orig(self);
        try { board.ExpandNotches(CharmNotches.Effective(
            PlayerData.instance != null ? PlayerData.instance.GetIntInternal("charmSlots") : 0,
            settings().CharmNotchTotal)); }
        catch (Exception e) { Modding.Logger.LogError("[Spellcaster] notches: " + e); }
    }

    private void OnAfterLoad(SaveGameData data)
    {
        ApplyGive();
        RestoreList(data?.playerData ?? PlayerData.instance);
        if (PlayerData.instance != null) PlayerData.instance.CalculateNotchesUsed();
        SyncEffects();
        board.Ensure(settings().CharmIconVariant);
    }

    private void OnNewGame()
    {
        // Fresh local flags already default to false; honour the debug grant.
        if (settings().GiveSpellcasterCharms) GiveAll(asNew: true);
        else setLocal(new LocalSettings());
        SyncEffects();
    }

    private void OnBeforeSave(SaveGameData data)
    {
        var pd = data?.playerData ?? PlayerData.instance;
        RestoreSaveSafety(pd, strip: true);
    }

    private void OnAfterSave(int _)
    {
        RestoreSaveSafety(PlayerData.instance, strip: false);
    }

    /// <summary>
    /// Custom IDs and their notch fill must not remain in a save that is later opened without the mod.
    /// </summary>
    private void RestoreSaveSafety(PlayerData? pd, bool strip)
    {
        if (pd == null) return;
        var list = pd.equippedCharms ?? pd.GetVariable<List<int>>("equippedCharms");
        if (list != null)
        {
            if (strip)
            {
                for (int i = list.Count - 1; i >= 0; i--)
                    if (SpellCharms.IsOurs(list[i])) list.RemoveAt(i);
            }
            else RestoreList(pd);
        }
        int custom = CustomFilled();
        if (strip)
        {
            savedFilled = pd.charmSlotsFilled;
            pd.charmSlotsFilled = Math.Max(0, pd.charmSlotsFilled - custom);
        }
        else if (savedFilled >= 0)
        {
            pd.charmSlotsFilled = savedFilled;
            savedFilled = -1;
        }
        else pd.CalculateNotchesUsed();
    }

    private void RestoreList(PlayerData? pd)
    {
        if (pd == null) return;
        var list = pd.equippedCharms ?? pd.GetVariable<List<int>>("equippedCharms");
        if (list == null) return;
        foreach (int id in SpellCharms.Ids)
        {
            if (Flag(id).Equipped && Flag(id).Got && !list.Contains(id)) list.Add(id);
        }
    }
}
