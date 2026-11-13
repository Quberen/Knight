using System;
using System.Reflection;
using GlobalEnums;
using Spellcaster.Core;
using UnityEngine;

namespace Spellcaster.Mod;

internal sealed class ShadowChainRuntime
{
    private readonly SpellRuntime runtime;
    private readonly ShadowChain chain = new ShadowChain();
    private HeroController? hero;
    private static readonly FieldInfo ShadowTimer = Field("shadowDashTimer");
    private static readonly FieldInfo AirDashed = Field("airDashed");
    private static readonly FieldInfo DashCooldown = Field("dashCooldownTimer");

    internal ShadowChainRuntime(SpellRuntime runtime) => this.runtime = runtime;
    private static FieldInfo Field(string name) =>
        typeof(HeroController).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(HeroController).FullName, name);

    internal void Install()
    {
        On.HeroController.HeroDash += OnHeroDash;
        On.HeroController.CanDash += OnCanDash;
        On.HeroController.FinishedDashing += OnFinishedDashing;
        On.HeroController.Update += OnUpdate;
        On.HealthManager.TakeDamage += OnDamage;
    }

    internal void Uninstall()
    {
        On.HeroController.HeroDash -= OnHeroDash;
        On.HeroController.CanDash -= OnCanDash;
        On.HeroController.FinishedDashing -= OnFinishedDashing;
        On.HeroController.Update -= OnUpdate;
        On.HealthManager.TakeDamage -= OnDamage;
        Reset();
    }

    internal void Reset() { chain.Reset(); hero = null; }

    private bool Observe(HeroController self)
    {
        if (hero != self) { Reset(); hero = self; }
        if (PlayerData.instance == null ||
            !ShadowChain.Active(PlayerData.instance.GetBool("hasShadowDash"),
                PlayerData.instance.GetBool("equippedCharm_31"),
                PlayerData.instance.GetBool("equippedCharm_19")) ||
            self.cState.dead || self.cState.hazardDeath || self.cState.hazardRespawning || self.cState.transitioning)
        { chain.Reset(); return false; }
        if (!runtime.CanPlay) return false;
        var body = self.GetComponent<Rigidbody2D>();
        chain.Observe(Time.timeAsDouble, self.cState.onGround, body != null ? body.velocity.x : 0,
            self.cState.dashing || self.cState.backDashing);
        return true;
    }

    private void OnUpdate(On.HeroController.orig_Update orig, HeroController self)
    {
        Observe(self);
        orig(self);
        Observe(self);
    }

    private void OnFinishedDashing(On.HeroController.orig_FinishedDashing orig, HeroController self)
    {
        bool wasShadow = self.cState.shadowDashing;
        orig(self);
        // Start the 0.3s window from dash end; vanilla DASH_COOLDOWN is longer than 0.3s from dash start.
        if (Observe(self) && wasShadow) chain.ExtendWindow(Time.timeAsDouble);
    }

    private bool OnCanDash(On.HeroController.orig_CanDash orig, HeroController self)
    {
        bool active = Observe(self);
        if (orig(self)) return true;
        if (!active || !chain.InWindow(Time.timeAsDouble)) return false;
        if (!self.cState.onGround && !chain.CanExtraAirDash(Time.timeAsDouble)) return false;
        // Native DASH_COOLDOWN (~0.6s) is longer than the 0.3s chain window, so the window
        // would be unusable unless cooldown is waived for a chain attempt. Other native
        // restrictions (dashing, preventDash, landing, etc.) still apply via orig.
        float previousCooldown = (float)DashCooldown.GetValue(self)!;
        bool previousAir = (bool)AirDashed.GetValue(self)!;
        try
        {
            DashCooldown.SetValue(self, 0f);
            if (!self.cState.onGround) AirDashed.SetValue(self, false);
            return orig(self);
        }
        finally
        {
            DashCooldown.SetValue(self, previousCooldown);
            AirDashed.SetValue(self, previousAir);
        }
    }

    private void OnHeroDash(On.HeroController.orig_HeroDash orig, HeroController self)
    {
        if (!Observe(self)) { orig(self); return; }
        double now = Time.timeAsDouble;
        bool grounded = self.cState.onGround;
        float previous = (float)ShadowTimer.GetValue(self)!;
        var decision = chain.Decide(now, previous <= 0, PlayerData.instance.GetInt("MPCharge"), grounded);
        bool paid = decision.Kind == ShadowDashKind.PaidShadow;
        bool actualShadow = false;
        try
        {
            if (paid) ShadowTimer.SetValue(self, 0f);
            // Vanilla owns shadowDashing, i-frames, Sharp Shadow activation and recharge FX.
            orig(self);
            actualShadow = self.cState.dashing && self.cState.shadowDashing;
        }
        finally
        {
            // Never leave a borrowed zero behind, including if another hook rejects the dash.
            if (paid) ShadowTimer.SetValue(self, actualShadow ? self.SHADOW_DASH_COOLDOWN : previous);
        }
        if (!self.cState.dashing) return;
        chain.Commit(now, decision, grounded, actualShadow);
        if (paid && actualShadow) self.TakeMP(decision.SoulCost);
    }

    private void OnDamage(On.HealthManager.orig_TakeDamage orig, HealthManager self, HitInstance hit)
    {
        if (hit.AttackType == AttackTypes.SharpShadow && PlayerData.instance != null &&
            ShadowChain.Active(PlayerData.instance.GetBool("hasShadowDash"),
                PlayerData.instance.GetBool("equippedCharm_31"),
                PlayerData.instance.GetBool("equippedCharm_19")) &&
            PlayerData.instance.GetBool("equippedCharm_16"))
            hit.DamageDealt = ShadowChain.SharpDamage(hit.DamageDealt);
        // HitInstance is a value type: neither the source FSM nor the pooled prefab is edited.
        // Native SharpShadow never calls SoulGain; SpellRuntime rewards only AttackTypes.Spell.
        orig(self, hit);
    }
}
