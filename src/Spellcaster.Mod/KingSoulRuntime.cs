using System;
using HutongGames.PlayMaker;
using Spellcaster.Core;
using UnityEngine;

namespace Spellcaster.Mod;

/// <summary>
/// Gameplay ticker for Improved King's Soul. Grants via HeroController.AddMPCharge so the
/// soul orb animates, and suppresses the vanilla White Charm FSM while this charm is active
/// so the 4-soul / 2 s Kingsoul loop cannot stack.
/// </summary>
internal sealed class KingSoulRuntime
{
    private readonly KingSoulRegen regen = new KingSoulRegen();
    private readonly Func<bool> active;
    private bool installed;
    private bool suppressing;
    private PlayMakerFSM? whiteCharm;

    internal KingSoulRuntime(Func<bool> active) => this.active = active;

    internal void Install()
    {
        if (installed) return;
        installed = true;
        On.HutongGames.PlayMaker.FsmState.OnEnter += OnFsmEnter;
    }

    internal void Uninstall()
    {
        if (!installed) return;
        installed = false;
        On.HutongGames.PlayMaker.FsmState.OnEnter -= OnFsmEnter;
        SetSuppressed(false);
        whiteCharm = null;
        regen.Reset();
    }

    internal void Reset() => regen.Reset();

    internal void Tick(float dt, HeroController? hero)
    {
        if (!installed) return;
        bool on = active();
        SetSuppressed(on);
        if (!on || hero == null || dt <= 0f) return;

        var pd = PlayerData.instance;
        if (pd == null) return;
        try { if (pd.GetBool("disablePause")) return; } catch { }

        int soul = pd.GetInt("MPCharge");
        int room = RemainingRoom(pd);
        if (room <= 0) return;
        int grant = regen.Tick(dt, soul, room, true);
        for (int i = 0; i < grant; i++) hero.AddMPCharge(1);
    }

    internal static int RemainingRoom(PlayerData pd)
    {
        int orb = Math.Max(0, pd.GetInt("maxMP") - pd.GetInt("MPCharge"));
        int reserve = 0;
        bool bound = false;
        try { bound = BossSequenceController.BoundSoul; } catch { }
        if (!bound) reserve = Math.Max(0, pd.GetInt("MPReserveMax") - pd.GetInt("MPReserve"));
        return orb + reserve;
    }

    internal static bool HasVanillaKingsoul(PlayerData? pd)
    {
        if (pd == null) return false;
        try { return ImprovedKingSoul.PrerequisiteMet(pd.GetInt("royalCharmState")); }
        catch { return false; }
    }

    /// <summary>
    /// Skip the vanilla White Charm "Soul UP" grant only. The FSM still leaves the state so
    /// it is not stuck, but AddMPCharge(4) is not invoked.
    /// </summary>
    private void OnFsmEnter(On.HutongGames.PlayMaker.FsmState.orig_OnEnter orig, FsmState self)
    {
        if (suppressing && self != null && self.Name == "Soul UP")
        {
            var fsm = self.Fsm;
            if (fsm != null && fsm.Name == "White Charm")
            {
                // Advance without running SendMessageV2(AddMPCharge, 4).
                var owner = fsm.FsmComponent;
                if (owner != null) owner.SendEvent("FINISHED");
                return;
            }
        }
        orig(self);
    }

    private void SetSuppressed(bool value)
    {
        if (suppressing == value) return;
        suppressing = value;
        var fsm = FindWhiteCharm();
        if (fsm == null) return;
        if (value)
        {
            try { fsm.SendEvent("INACTIVE"); } catch { }
        }
        else
        {
            try { fsm.SendEvent("CHARM INDICATOR CHECK"); } catch { }
        }
    }

    private PlayMakerFSM? FindWhiteCharm()
    {
        if (whiteCharm != null && whiteCharm) return whiteCharm;
        whiteCharm = null;
        try
        {
            var hero = HeroController.instance;
            if (hero != null)
            {
                foreach (var fsm in hero.GetComponentsInChildren<PlayMakerFSM>(true))
                    if (fsm != null && fsm.FsmName == "White Charm") { whiteCharm = fsm; return fsm; }
            }
            foreach (var fsm in UnityEngine.Object.FindObjectsOfType<PlayMakerFSM>())
                if (fsm != null && fsm.FsmName == "White Charm") { whiteCharm = fsm; return fsm; }
        }
        catch { }
        return whiteCharm;
    }
}
