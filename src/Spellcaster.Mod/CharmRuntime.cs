using System;
using System.Collections.Generic;
using System.Linq;
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
    public CharmSave ImprovedKingSoul = new CharmSave();
}

/// <summary>
/// Wires the Spellcaster charms into PlayerData hooks, notch accounting, and Mage's Grip /
/// Shaman Master / Soul Collector / Improved King's Soul effects. The charm board UI is in <see cref="CharmBoard"/>.
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
    internal bool KingSoulEquipped =>
        Flag(SpellCharms.ImprovedKingSoulId).Equipped && KingSoulRuntime.HasVanillaKingsoul(PlayerData.instance);

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
        try { On.Language.Language.DoSwitch += OnDoSwitch; }
        catch (Exception e) { Modding.Logger.Log("[Spellcaster] DoSwitch hook: " + e.Message); }
        EnsureOwned(asNew: true);
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
        try { On.Language.Language.DoSwitch -= OnDoSwitch; } catch { }
        board.Uninstall();
        runtime.SetGrip(false);
        runtime.SetCollector(false);
    }

    internal void ApplySettings()
    {
        EnsureOwned(asNew: true);
        SyncEffects();
        board.RefreshIcons();
        if (PlayerData.instance != null) PlayerData.instance.CalculateNotchesUsed();
    }

    internal static bool GameIsChinese()
    {
        string? current = null;
        string? saved = null;
        try { current = Language.Language.CurrentLanguage().ToString(); } catch { }
        try
        {
            if (GameManager.instance != null)
                saved = GameManager.instance.GetLanguageAsString();
        }
        catch { }
        return SpellCharms.ResolveIsChinese(current, saved);
    }

    /// <summary>
    /// Every save (new or existing) owns all Spellcaster charms, not auto-equipped.
    /// Improved King's Soul still requires vanilla King's Soul (<c>royalCharmState == 3</c>) to equip.
    /// The "new" orb is only set the first time a charm becomes owned.
    /// </summary>
    internal void EnsureOwned(bool asNew)
    {
        foreach (int id in SpellCharms.Ids)
        {
            var flag = Flag(id);
            if (!flag.Got)
            {
                if (asNew) flag.New = true;
                flag.Got = true;
            }
        }
        if (PlayerData.instance != null)
            PlayerData.instance.SetBool("hasCharm", true);
        SyncEffects();
    }

    internal void SyncEffects()
    {
        DropKingSoulIfPrerequisiteLost();
        runtime.SetCollector(CollectorEquipped);
        runtime.SetGrip(GripEquipped);
        runtime.SetKingSoul(KingSoulEquipped);
        runtime.ResetCharge();
    }

    private void DropKingSoulIfPrerequisiteLost()
    {
        var flag = Flag(SpellCharms.ImprovedKingSoulId);
        if (!flag.Equipped) return;
        if (KingSoulRuntime.HasVanillaKingsoul(PlayerData.instance)) return;
        flag.Equipped = false;
        var pd = PlayerData.instance;
        if (pd == null) return;
        var list = pd.equippedCharms ?? pd.GetVariable<System.Collections.Generic.List<int>>("equippedCharms");
        list?.Remove(SpellCharms.ImprovedKingSoulId);
        try { pd.CalculateNotchesUsed(); } catch { }
    }

    private CharmSave Flag(int id) => id switch
    {
        SpellCharms.MageGripId => local().MageGrip,
        SpellCharms.ShamanMasterId => local().ShamanMaster,
        SpellCharms.SoulCollectorId => local().SoulCollector,
        SpellCharms.ImprovedKingSoulId => local().ImprovedKingSoul,
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
            SpellCharms.ImprovedKingSoulId => SpellCharms.ClampCost(id, s.ImprovedKingSoulNotchCost),
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


    private string OnLanguage(string key, string sheet, string orig)
    {
        // Resolve at query time from the live CurrentLanguage, not a cached flag.
        bool zh = GameIsChinese();
        if (SpellCharms.IsNameKey(key, out int id)) return SpellCharms.DisplayName(id, zh);
        if (SpellCharms.IsDescKey(key, out id)) return SpellCharms.DisplayDescription(id, zh);
        return orig;
    }

    private static void OnDoSwitch(On.Language.Language.orig_DoSwitch orig, Language.LanguageCode newLang)
    {
        orig(newLang);
        try { RefreshDisplayedCharmText(); }
        catch (Exception e) { Modding.Logger.Log("[Spellcaster] refresh charm text: " + e.Message); }
    }

    /// <summary>
    /// Re-apply name/desc on the open Charms pane after an in-session language switch
    /// so TextMeshPro is not left holding the previous language.
    /// </summary>
    internal static void RefreshDisplayedCharmText()
    {
        var cameras = GameCameras.instance;
        if (cameras == null || cameras.hudCamera == null) return;
        var charmsGo = FindNamed(cameras.hudCamera.gameObject, "Charms");
        if (charmsGo == null || !charmsGo.activeInHierarchy) return;

        bool zh = GameIsChinese();
        int id = 0;
        try
        {
            var fsm = charmsGo.GetComponents<PlayMakerFSM>().FirstOrDefault(f => f.FsmName == "UI Charms");
            var v = fsm != null ? fsm.FsmVariables.FindFsmInt("Current Item Number") : null;
            if (v != null) id = v.Value;
        }
        catch { }

        string name = SpellCharms.IsOurs(id) ? SpellCharms.DisplayName(id, zh) : null!;
        string desc = SpellCharms.IsOurs(id) ? SpellCharms.DisplayDescription(id, zh) : null!;

        SetTmp(FindNamed(charmsGo, "Text Name"), name);
        SetTmp(FindNamed(charmsGo, "Text Desc"), desc);

        // Ask the vanilla Update Text FSM to re-pull Convo Name / Convo Desc (Language.Get).
        try
        {
            foreach (var fsm in charmsGo.GetComponents<PlayMakerFSM>())
            {
                if (fsm == null) continue;
                if (fsm.FsmName == "Update Text")
                {
                    fsm.SendEvent("UPDATE TEXT");
                    fsm.SendEvent("CHANGE TEXT");
                }
            }
            // After the FSM runs it will Language.Get through OnLanguage. If we already
            // wrote TMP and the FSM also writes, that is fine — same resolved string.
        }
        catch { }

        Modding.Logger.Log("[Spellcaster] refreshed charm text lang=" + (zh ? "ZH" : "EN")
            + " id=" + id
            + (SpellCharms.IsOurs(id) ? " name=" + name : ""));
    }

    private static void SetTmp(GameObject? go, string? text)
    {
        if (go == null || text == null) return;
        foreach (var c in go.GetComponents<Component>())
        {
            var tpe = c.GetType();
            if (tpe.Name.IndexOf("TextMeshPro", StringComparison.OrdinalIgnoreCase) < 0
                && tpe.Name != "TextMeshPro") continue;
            var prop = tpe.GetProperty("text") ?? tpe.GetProperty("Text");
            if (prop != null && prop.CanWrite) prop.SetValue(c, text, null);
        }
    }

    private static GameObject? FindNamed(GameObject? parent, string name)
    {
        if (parent == null) return null;
        if (parent.name == name) return parent;
        var tr = parent.transform.Find(name);
        if (tr != null) return tr.gameObject;
        foreach (Transform c in parent.transform)
        {
            var f = FindNamed(c.gameObject, name);
            if (f != null) return f;
        }
        return null;
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
            if (id == SpellCharms.ImprovedKingSoulId && orig && !KingSoulRuntime.HasVanillaKingsoul(PlayerData.instance))
                return false;
            flag.Equipped = orig;
            if (id == SpellCharms.ImprovedKingSoulId && orig) UnequipVanillaKingsoul(PlayerData.instance);
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
        if (charmNum == SpellCharms.ImprovedKingSoulId && !KingSoulRuntime.HasVanillaKingsoul(self))
            return;
        orig(self, charmNum);
        if (SpellCharms.IsOurs(charmNum))
        {
            Flag(charmNum).Equipped = true;
            if (charmNum == SpellCharms.ImprovedKingSoulId) UnequipVanillaKingsoul(self);
            SyncEffects();
        }
    }

    private static void UnequipVanillaKingsoul(PlayerData pd)
    {
        if (pd == null) return;
        try
        {
            if (pd.GetBool("equippedCharm_36")) pd.UnequipCharm(36);
        }
        catch (Exception e) { Modding.Logger.Log("[Spellcaster] unequip Kingsoul: " + e.Message); }
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
            board.Ensure();
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
        EnsureOwned(asNew: true);
        RestoreList(data?.playerData ?? PlayerData.instance);
        if (PlayerData.instance != null) PlayerData.instance.CalculateNotchesUsed();
        SyncEffects();
        board.Ensure();
    }

    private void OnNewGame()
    {
        setLocal(new LocalSettings());
        EnsureOwned(asNew: true);
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
