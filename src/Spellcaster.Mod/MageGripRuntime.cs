using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GlobalEnums;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using Spellcaster.Core;
using UnityEngine;

namespace Spellcaster.Mod;

/// <summary>
/// Owns only the Quick Cast button when Mage's Grip is equipped and Vengeful Spirit is unlocked.
/// Charge is stored while the button is held (walk, jump, dash, turn, wall-slide), using vanilla
/// nail-art glow / flash / audio. Nail/attack and regular Cast stay vanilla.
/// </summary>
internal sealed class MageGripRuntime
{
    private readonly SpellRuntime runtime;
    private readonly GripAttack attack = new GripAttack();
    private readonly GripRecoil recoil = new GripRecoil();
    private HeroController? hero;
    private HeroController? damageSub;
    private float? pendingAngle;
    private bool pendingPogo;
    private PlayMakerFSM? prepaidFsm;
    private string? prepaidState;
    private int suppressDebit;
    private CastKind prepaidKind;
    private bool prepaidShamanStone;
    private int visTier;
    private bool visWinding;
    private GripRelease pendingRelease;
    private bool hasPending;
    private float pendingUntil;
    private static readonly Dictionary<string, FieldInfo> Fields = new Dictionary<string, FieldInfo>();
    private static readonly MethodInfo SetStateMethod = typeof(HeroController).GetMethod("SetState", BindingFlags.Instance | BindingFlags.NonPublic)!;

    internal MageGripRuntime(SpellRuntime runtime) => this.runtime = runtime;

    internal void Install()
    {
        On.HeroController.Update += OnUpdate;
        On.HeroController.FixedUpdate += OnFixedUpdate;
        On.HeroController.CancelBounce += OnCancelBounce;
        On.HeroController.TakeMP += OnTakeMP;
        On.HutongGames.PlayMaker.Actions.ListenForQuickCast.OnUpdate += OnListenQuickCast;
    }

    internal void Uninstall()
    {
        Reset();
        On.HeroController.Update -= OnUpdate;
        On.HeroController.FixedUpdate -= OnFixedUpdate;
        On.HeroController.CancelBounce -= OnCancelBounce;
        On.HeroController.TakeMP -= OnTakeMP;
        On.HutongGames.PlayMaker.Actions.ListenForQuickCast.OnUpdate -= OnListenQuickCast;
        SpellProjectile.RetireSmall();
    }

    private static FieldInfo Field(string name)
    {
        if (!Fields.TryGetValue(name, out var field))
        {
            field = typeof(HeroController).GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(typeof(HeroController).FullName, name);
            Fields.Add(name, field);
        }
        return field;
    }
    private static void Set(HeroController self, string name, object value) => Field(name).SetValue(self, value);
    private static T? Get<T>(HeroController self, string name) where T : class => Field(name).GetValue(self) as T;

    internal void Reset()
    {
        if (prepaidFsm != null && prepaidState != null && prepaidFsm.gameObject.activeInHierarchy)
            prepaidFsm.SetState("Cancel All");
        if (hero != null) HideVisuals(hero);
        attack.Reset(); recoil.Reset();
        pendingAngle = null; pendingPogo = false;
        prepaidFsm = null; prepaidState = null; suppressDebit = 0;
        visTier = 0; visWinding = false; hasPending = false;
        UnsubscribeDamage();
        hero = null;
    }

    /// <summary>Vengeful Spirit unlocked. The runtime is only constructed while Mage's Grip is equipped.</summary>
    private static bool Active => PlayerData.instance != null && PlayerData.instance.GetInt("fireballLevel") > 0;

    /// <summary>
    /// Conditions that drop a stored charge. Intentionally allows dash, wall-slide, jump,
    /// attack-recoil and walking — the charge is kept as long as Quick Cast stays held.
    /// Damage is handled separately via <see cref="OnHurt"/>, mirroring vanilla nail arts.
    /// </summary>
    private bool MustDropCharge(HeroController self) => !runtime.CanPlay || self.cState.transitioning ||
        self.cState.dead || self.cState.hazardDeath || self.cState.hazardRespawning ||
        self.hero_state == ActorStates.no_input;

    // Small-shot pogo / recoil only. Not used to drop a stored Quick Cast charge.
    private bool Ready(HeroController self) => runtime.CanPlay && !self.cState.transitioning &&
        !self.cState.dead && !self.cState.hazardDeath && !self.cState.hazardRespawning &&
        !self.cState.recoiling && !self.cState.recoilFrozen && !self.cState.dashing &&
        !self.cState.backDashing && self.hero_state != ActorStates.no_input;

    private static float Interval(HeroController self) => PlayerData.instance.GetBool("equippedCharm_32")
        ? self.ATTACK_COOLDOWN_TIME_CH : self.ATTACK_COOLDOWN_TIME;

    private void OnListenQuickCast(On.HutongGames.PlayMaker.Actions.ListenForQuickCast.orig_OnUpdate orig,
        ListenForQuickCast self)
    {
        if (Active) return;
        orig(self);
    }

    private HeroActions Actions(HeroController self) =>
        ((InputHandler)Field("inputHandler").GetValue(self)!).inputActions;

    private void SubscribeDamage(HeroController self)
    {
        if (damageSub == self) return;
        UnsubscribeDamage();
        damageSub = self;
        if (self != null) self.OnTakenDamage += OnHurt;
    }

    private void UnsubscribeDamage()
    {
        if (damageSub != null) damageSub.OnTakenDamage -= OnHurt;
        damageSub = null;
    }

    private void OnHurt()
    {
        if (hero != null) HideVisuals(hero);
        attack.CancelHold();
        hasPending = false;
        visTier = 0;
        visWinding = false;
    }

    private void OnUpdate(On.HeroController.orig_Update orig, HeroController self)
    {
        orig(self);
        hero = self;
        SubscribeDamage(self);
        if (!Active)
        {
            if (attack.Holding || visTier > 0 || visWinding) { HideVisuals(self); attack.CancelHold(); }
            hasPending = false;
            return;
        }
        if (MustDropCharge(self))
        {
            if (attack.Holding || visTier > 0 || visWinding) { HideVisuals(self); attack.CancelHold(); }
            hasPending = false;
            return;
        }
        var input = Actions(self);
        double now = Time.timeAsDouble;
        if (input.quickCast.WasPressed) attack.Press(now);
        if (attack.Holding && input.quickCast.IsPressed)
        {
            SyncVisuals(self, now);
            return;
        }
        if (attack.Holding)
        {
            HideVisuals(self);
            float angle = Direction(self);
            string levelName = angle == 90 ? "screamLevel" : angle == 270 ? "quakeLevel" : "fireballLevel";
            var release = attack.Release(now, PlayerData.instance.GetInt("MPCharge"),
                PlayerData.instance.GetInt(levelName) > 0, runtime.ShamanMasterEnabled,
                PlayerData.instance.GetBool("equippedCharm_19"), runtime.FastCastingEnabled,
                PlayerData.instance.GetBool("equippedCharm_33"));
            if (!TryFire(self, angle, levelName, release))
            {
                pendingRelease = release;
                hasPending = release != GripRelease.None;
                pendingUntil = Time.time + 1f;
            }
            return;
        }
        if (hasPending)
        {
            if (Time.time > pendingUntil) { hasPending = false; return; }
            float angle = Direction(self);
            string levelName = angle == 90 ? "screamLevel" : angle == 270 ? "quakeLevel" : "fireballLevel";
            if (TryFire(self, angle, levelName, pendingRelease)) hasPending = false;
        }
    }

    private bool TryFire(HeroController self, float angle, string levelName, GripRelease release)
    {
        if (release == GripRelease.None) return true;
        if ((release == GripRelease.NormalSpell || release == GripRelease.ChargedSpell) &&
            TrySpell(self, angle, levelName, release))
        {
            attack.Commit(Time.timeAsDouble, Interval(self));
            return true;
        }
        if (release == GripRelease.SmallProjectile)
        {
            FireSmall(self, angle);
            return true;
        }
        return false;
    }

    private static float Direction(HeroController self)
    {
        var input = ((InputHandler)Field("inputHandler").GetValue(self)!).inputActions;
        if (input.up.IsPressed) return 90;
        if (input.down.IsPressed) return 270;
        if (input.left.IsPressed) return 180;
        if (input.right.IsPressed) return 0;
        return self.cState.facingRight ? 0 : 180;
    }

    private void FireSmall(HeroController self, float angle)
    {
        if (!runtime.SpawnSmall(self, angle)) return;
        attack.Commit(Time.timeAsDouble, Interval(self));
        pendingAngle = angle;
    }

    private bool TrySpell(HeroController self, float angle, string levelName, GripRelease release)
    {
        var fsm = PlayMakerFSM.FindFsmOnGameObject(self.gameObject, "Spell Control");
        // Looser than CanCast: a stored charge can be released mid-dash / mid-nail.
        if (fsm == null || fsm.ActiveStateName != "Inactive") return false;
        if (self.cState.dead || self.cState.transitioning || self.hero_state == ActorStates.no_input) return false;
        int level = PlayerData.instance.GetInt(levelName);
        string state = angle == 270 ? "Quake Antic" : angle == 90 ? (level > 1 ? "Scream Antic2" : "Scream Antic1") : "Fireball Antic";
        string debit = angle == 270 ? "Level Check 2" : angle == 90 ? (level > 1 ? "Scream Burst 2" : "Scream Burst 1") : (level > 1 ? "Fireball 2" : "Fireball 1");
        int cost = GripAttack.Cost(release, PlayerData.instance.GetBool("equippedCharm_33"));
        if (!fsm.FsmStates.Any(s => s.Name == state) || PlayerData.instance.GetInt("MPCharge") < cost) return false;
        if (angle == 0) self.FaceRight();
        if (angle == 180) self.FaceLeft();
        fsm.FsmVariables.FindFsmInt("Spell Level").Value = level;
        self.TakeMP(cost);
        prepaidFsm = fsm;
        prepaidState = debit;
        prepaidKind = GripAttack.Kind(release);
        prepaidShamanStone = PlayerData.instance.GetBool("equippedCharm_19");
        pendingAngle = null; pendingPogo = false;
        fsm.SetState(state);
        return true;
    }

    private void SyncVisuals(HeroController self, double now)
    {
        bool shaman = runtime.ShamanMasterEnabled;
        bool stone = PlayerData.instance.GetBool("equippedCharm_19");
        bool fast = runtime.FastCastingEnabled;
        int tier = attack.StoredTier(now, shaman, stone, fast);
        bool winding = attack.Winding(now, shaman, stone, fast);
        var glow = Get<GameObject>(self, "artChargeEffect");
        var charged = Get<GameObject>(self, "artChargedEffect");
        var flash = Get<GameObject>(self, "artChargedFlash");
        var audio = Get<HeroAudioController>(self, "audioCtrl");
        if (winding && !visWinding)
        {
            if (glow != null && !glow.activeSelf) glow.SetActive(true);
            audio?.PlaySound(HeroSounds.NAIL_ART_CHARGE);
        }
        if (!winding && visWinding)
        {
            if (glow != null) glow.SetActive(false);
            audio?.StopSound(HeroSounds.NAIL_ART_CHARGE);
        }
        if (winding && glow != null && !glow.activeSelf) glow.SetActive(true);
        visWinding = winding;

        if (tier > visTier)
        {
            if (glow != null) glow.SetActive(false);
            audio?.StopSound(HeroSounds.NAIL_ART_CHARGE);
            if (charged != null) charged.SetActive(true);
            if (flash != null) flash.SetActive(true);
            var anim = Field("artChargedEffectAnim").GetValue(self);
            anim?.GetType().GetMethod("PlayFromFrame", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(int) }, null)
                ?.Invoke(anim, new object[] { 0 });
            try { GameCameras.instance?.cameraShakeFSM?.SendEvent("EnemyKillShake"); } catch { }
            var src = Get<AudioSource>(self, "audioSource");
            var clip = Field("nailArtChargeComplete").GetValue(self) as AudioClip;
            if (src != null && clip != null) src.PlayOneShot(clip, 1f);
            audio?.PlaySound(HeroSounds.NAIL_ART_READY);
        }
        if (tier >= 1 && charged != null && !charged.activeSelf) charged.SetActive(true);
        if (tier == 0 && visTier > 0) HideVisuals(self);
        visTier = tier;
    }

    private void HideVisuals(HeroController self)
    {
        // Do not tear down a concurrent vanilla nail-art charge.
        bool nail = self.cState.nailCharging;
        float timer = 0f;
        try { timer = (float)Field("nailChargeTimer").GetValue(self)!; } catch { }
        float need = 0f;
        try { need = (float)Field("nailChargeTime").GetValue(self)!; } catch { }
        bool vanillaOwns = nail || timer >= need && timer > 0f;
        var audio = Get<HeroAudioController>(self, "audioCtrl");
        if (!vanillaOwns)
        {
            var glow = Get<GameObject>(self, "artChargeEffect");
            var charged = Get<GameObject>(self, "artChargedEffect");
            var flash = Get<GameObject>(self, "artChargedFlash");
            if (glow != null) glow.SetActive(false);
            if (charged != null) charged.SetActive(false);
            if (flash != null) flash.SetActive(false);
            audio?.StopSound(HeroSounds.NAIL_ART_CHARGE);
            audio?.StopSound(HeroSounds.NAIL_ART_READY);
        }
        visTier = 0;
        visWinding = false;
    }

    internal (CastKind Kind, bool ShamanStone) SpellKind(FsmState state) =>
        prepaidFsm != null && state.Fsm == prepaidFsm.Fsm
            ? (prepaidKind, prepaidShamanStone) : (CastKind.NormalSpell, false);

    internal bool BeforeSpellState(FsmState state)
    {
        if (prepaidFsm == null || state.Fsm != prepaidFsm.Fsm) return false;
        if (state.Name == "Inactive" || state.Name == "Cancel All" || state.Name == "Spell End")
        { prepaidFsm = null; prepaidState = null; return false; }
        if (state.Name != prepaidState) return false;
        prepaidState = null; // A single native debit, scoped to this state entry.
        suppressDebit++;
        return true;
    }

    internal void AfterSpellState() => suppressDebit = Math.Max(0, suppressDebit - 1);
    private void OnTakeMP(On.HeroController.orig_TakeMP orig, HeroController self, int amount)
    {
        if (suppressDebit > 0 && self == hero) return;
        orig(self, amount);
    }

    internal bool QueuePogo()
    {
        if (hero == null || !Ready(hero) || hero.cState.onGround || hero.cState.wallSliding) return false;
        pendingPogo = true;
        return true;
    }

    private void OnCancelBounce(On.HeroController.orig_CancelBounce orig, HeroController self)
    {
        orig(self);
        pendingAngle = null; pendingPogo = false;
        // Dash/damage cancellation must not grant another first-shot lift in mid-air.
        if (self.cState.onGround) recoil.Land();
    }

    private void OnFixedUpdate(On.HeroController.orig_FixedUpdate orig, HeroController self)
    {
        orig(self);
        if (!Ready(self) || self.cState.wallSliding)
        { pendingAngle = null; pendingPogo = false; return; }
        var body = self.GetComponent<Rigidbody2D>();
        if (body == null) return;
        bool grounded = self.cState.onGround && body.velocity.y <= .01f;
        if (grounded) recoil.Land();
        if (pendingPogo)
        {
            pendingPogo = false; pendingAngle = null;
            self.Bounce(); // Restores air dash/double jump and uses native bounce duration/cancellation.
            body.velocity = new Vector2(body.velocity.x, (float)recoil.Pogo(self.BOUNCE_VELOCITY));
        }
        else if (pendingAngle.HasValue)
        {
            float angle = pendingAngle.Value;
            pendingAngle = null;
            if (angle == 270)
            {
                self.CancelHeroJump();
                self.cState.bouncing = false;
                self.cState.shroomBouncing = false;
                Set(self, "bounceTimer", 0f);
                self.cState.doubleJumping = false;
                self.cState.onGround = false;
                SetStateMethod.Invoke(self, new object[] { ActorStates.airborne });
                double gravity = Math.Abs(Physics2D.gravity.y * body.gravityScale);
                body.velocity = new Vector2(body.velocity.x, (float)recoil.Down(body.velocity.y, self.BOUNCE_VELOCITY, gravity, Interval(self)));
            }
            else if (angle == 90)
            {
                float before = body.velocity.y;
                if (!grounded)
                {
                    self.CancelHeroJump();
                    self.cState.bouncing = false;
                    self.cState.shroomBouncing = false;
                    self.cState.doubleJumping = false;
                    Set(self, "bounceTimer", 0f);
                }
                body.velocity = new Vector2(body.velocity.x, (float)recoil.Up(before, self.BOUNCE_VELOCITY, grounded));
            }
        }
    }

    internal static (GameObject? Prefab, float Speed) FindProjectile(HeroController self)
    {
        // Small shots always use white Vengeful Spirit (Fireball 1), even with Shade Soul unlocked.
        string? state = GripProjectiles.SmallShotState(PlayerData.instance.GetInt("fireballLevel"));
        if (state == null) return (null, 0);
        var control = PlayMakerFSM.FindFsmOnGameObject(self.gameObject, "Spell Control");
        var top = control?.FsmStates.FirstOrDefault(s => s.Name == state)?.Actions
            .OfType<SpawnObjectFromGlobalPool>().FirstOrDefault()?.gameObject.Value;
        if (top == null) return (null, 0);
        // Prefabs are absent from PlayMakerFSM's active-instance registry.
        var cast = top.GetComponents<PlayMakerFSM>().FirstOrDefault(fsm => fsm.FsmName == "Fireball Cast");
        cast?.Fsm.InitData(); // Deserialize action references only; never activate the prefab.
        var prefab = cast?.FsmStates.FirstOrDefault(s => s.Name == "Cast Right")?.Actions
            .OfType<SpawnObjectFromGlobalPool>().FirstOrDefault()?.gameObject.Value;
        return (prefab, cast?.FsmVariables.FindFsmFloat("Fire Speed")?.Value ?? 0);
    }
}
