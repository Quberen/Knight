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

/// <summary>Owns only the attack-button path. Spell/focus input and native spell states stay vanilla.</summary>
internal sealed class MageGripRuntime
{
    private readonly SpellRuntime runtime;
    private readonly GripAttack attack = new GripAttack();
    private readonly GripRecoil recoil = new GripRecoil();
    private HeroController? hero;
    private float? pendingAngle;
    private bool pendingPogo;
    private PlayMakerFSM? prepaidFsm;
    private string? prepaidState;
    private int suppressDebit;
    private CastKind prepaidKind;
    private bool prepaidShamanStone;
    private static readonly Dictionary<string, FieldInfo> Fields = new Dictionary<string, FieldInfo>();
    private static readonly MethodInfo SetStateMethod = typeof(HeroController).GetMethod("SetState", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo CanAttackMethod = typeof(HeroController).GetMethod("CanAttack", BindingFlags.Instance | BindingFlags.NonPublic)!;

    internal MageGripRuntime(SpellRuntime runtime) => this.runtime = runtime;

    internal void Install()
    {
        On.HeroController.DoAttack += OnDoAttack;
        On.HeroController.Attack += OnAttack;
        On.HeroController.CanNailCharge += OnCanNailCharge;
        On.HeroController.CanNailArt += OnCanNailArt;
        On.HeroController.Update += OnUpdate;
        On.HeroController.FixedUpdate += OnFixedUpdate;
        On.HeroController.CancelBounce += OnCancelBounce;
        On.HeroController.TakeMP += OnTakeMP;
        ClearNailCharge(HeroController.instance);
        var self = HeroController.instance;
        if (self != null)
        {
            foreach (var slash in self.GetComponentsInChildren<NailSlash>(true)) slash.CancelAttack();
            self.cState.attacking = false;
            self.cState.upAttacking = false;
            self.cState.downAttacking = false;
            Set(self, "attack_time", 0f);
        }
    }

    internal void Uninstall()
    {
        Reset();
        ClearNailCharge(HeroController.instance);
        On.HeroController.DoAttack -= OnDoAttack;
        On.HeroController.Attack -= OnAttack;
        On.HeroController.CanNailCharge -= OnCanNailCharge;
        On.HeroController.CanNailArt -= OnCanNailArt;
        On.HeroController.Update -= OnUpdate;
        On.HeroController.FixedUpdate -= OnFixedUpdate;
        On.HeroController.CancelBounce -= OnCancelBounce;
        On.HeroController.TakeMP -= OnTakeMP;
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

    private static void ClearNailCharge(HeroController? self)
    {
        if (self == null) return;
        self.cState.nailCharging = false;
        Set(self, "nailChargeTimer", 0f);
        var audio = Field("audioCtrl").GetValue(self) as HeroAudioController;
        if (audio != null)
        {
            audio.StopSound(HeroSounds.NAIL_ART_CHARGE);
            audio.StopSound(HeroSounds.NAIL_ART_READY);
        }
        foreach (string name in new[] { "artChargeEffect", "artChargedEffect", "artChargedFlash" })
            (Field(name).GetValue(self) as GameObject)?.SetActive(false);
    }

    internal void Reset()
    {
        if (prepaidFsm != null && prepaidState != null && prepaidFsm.gameObject.activeInHierarchy)
            prepaidFsm.SetState("Cancel All");
        attack.Reset(); recoil.Reset();
        pendingAngle = null; pendingPogo = false;
        prepaidFsm = null; prepaidState = null; suppressDebit = 0;
        hero = null;
    }

    private static bool Active => PlayerData.instance != null && PlayerData.instance.GetInt("fireballLevel") > 0;

    private bool Ready(HeroController self) => runtime.CanPlay && !self.cState.transitioning &&
        !self.cState.dead && !self.cState.hazardDeath && !self.cState.hazardRespawning &&
        !self.cState.recoiling && !self.cState.recoilFrozen && !self.cState.dashing &&
        !self.cState.backDashing && self.hero_state != ActorStates.no_input;

    private static bool NativeCanAttack(HeroController self) => (bool)CanAttackMethod.Invoke(self, null)!;
    private static float Interval(HeroController self) => PlayerData.instance.GetBool("equippedCharm_32")
        ? self.ATTACK_COOLDOWN_TIME_CH : self.ATTACK_COOLDOWN_TIME;

    private void OnDoAttack(On.HeroController.orig_DoAttack orig, HeroController self)
    {
        if (!Active) { orig(self); return; }
        hero = self;
        Set(self, "attackQueuing", false);
        if (Ready(self)) attack.Press(Time.timeAsDouble);
    }

    private bool OnCanNailCharge(On.HeroController.orig_CanNailCharge orig, HeroController self) => !Active && orig(self);
    private bool OnCanNailArt(On.HeroController.orig_CanNailArt orig, HeroController self) => !Active && orig(self);

    private void OnUpdate(On.HeroController.orig_Update orig, HeroController self)
    {
        orig(self);
        hero = self;
        if (!Active || !Ready(self))
        {
            attack.CancelHold(); pendingAngle = null; pendingPogo = false;
            return;
        }
        if (!attack.Holding || ((InputHandler)Field("inputHandler").GetValue(self)!).inputActions.attack.IsPressed) return;
        float angle = Direction(self);
        string levelName = angle == 90 ? "screamLevel" : angle == 270 ? "quakeLevel" : "fireballLevel";
        var release = attack.Release(Time.timeAsDouble, PlayerData.instance.GetInt("MPCharge"),
            PlayerData.instance.GetInt(levelName) > 0, runtime.ShamanMasterEnabled,
            PlayerData.instance.GetBool("equippedCharm_19"), runtime.FastCastingEnabled,
            PlayerData.instance.GetBool("equippedCharm_33"));
        if (!NativeCanAttack(self)) return;
        if ((release == GripRelease.NormalSpell || release == GripRelease.ChargedSpell) && TrySpell(self, angle, levelName, release))
        {
            attack.Commit(Time.timeAsDouble, Interval(self));
            Set(self, "attack_cooldown", Interval(self));
        }
        else if (release != GripRelease.None)
        {
            // Includes insufficient soul or a locked directional spell.
            self.Attack(angle == 90 ? AttackDirection.upward : angle == 270 ? AttackDirection.downward : AttackDirection.normal);
        }
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

    private void OnAttack(On.HeroController.orig_Attack orig, HeroController self, AttackDirection direction)
    {
        if (!Active) { orig(self, direction); return; }
        if (!Ready(self) || !attack.CanAttack(Time.timeAsDouble) || !NativeCanAttack(self)) return;
        hero = self;
        float angle = direction == AttackDirection.upward ? 90 : direction == AttackDirection.downward ? 270 : Direction(self);
        if (!runtime.SpawnSmall(self, angle)) return;
        // The original update loop still owns attack recovery and decrements these timers.
        // Replacing Attack avoids both StartSlash and Grubberfly's extra nail beam.
        // Vanilla CancelAttack/CancelDownAttack dereference slashComponent even though no slash started.
        Set(self, "slashComponent", Field(angle == 90 ? "upSlash" : angle == 270 ? "downSlash" : "normalSlash").GetValue(self)!);
        Set(self, "wallSlashing", false);
        self.cState.attacking = true;
        self.cState.upAttacking = angle == 90;
        self.cState.downAttacking = angle == 270;
        Set(self, "attack_time", 0f);
        Set(self, "attackDuration", PlayerData.instance.GetBool("equippedCharm_32") ? self.ATTACK_DURATION_CH : self.ATTACK_DURATION);
        Set(self, "attack_cooldown", Interval(self));
        Set(self, "attackQueuing", false);
        attack.Commit(Time.timeAsDouble, Interval(self));
        pendingAngle = angle;
    }

    private bool TrySpell(HeroController self, float angle, string levelName, GripRelease release)
    {
        var fsm = PlayMakerFSM.FindFsmOnGameObject(self.gameObject, "Spell Control");
        if (fsm == null || fsm.ActiveStateName != "Inactive" || !self.CanCast()) return false;
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
