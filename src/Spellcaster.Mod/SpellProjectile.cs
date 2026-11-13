using System;
using System.Collections.Generic;
using GlobalEnums;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using Spellcaster.Core;
using UnityEngine;

namespace Spellcaster.Mod;

/// <summary>Per pooled activation; reversible edits never touch the prefab or normal spell damage.</summary>
internal sealed class SpellProjectile : MonoBehaviour
{
    private static readonly HashSet<SpellProjectile> Instances = new HashSet<SpellProjectile>();
    private readonly List<Action> restore = new List<Action>();
    private readonly HashSet<FsmStateAction> edited = new HashSet<FsmStateAction>();
    private SpellRuntime? runtime;
    private CastRecord? cast;
    private Vector3 originalScale;
    private Quaternion originalRotation;
    private bool scaled;
    private Vector2 velocity;
    internal bool Tracked => runtime != null && cast != null;
    internal CastRecord? ActivationCast => cast;
    private bool Small => cast?.Kind == CastKind.SmallProjectile;

    internal static SpellProjectile Prepare(GameObject owner, SpellRuntime runtime, CastRecord? cast)
    {
        var marker = owner.GetComponent<SpellProjectile>() ?? owner.AddComponent<SpellProjectile>();
        Instances.Add(marker);
        if (marker.cast == cast && marker.runtime == runtime) return marker;
        marker.Restore();
        marker.runtime = runtime;
        marker.cast = cast;
        marker.originalRotation = owner.transform.rotation;
        if (cast?.Kind == CastKind.SmallProjectile) marker.Launch(cast.Speed);
        return marker;
    }

    internal void Launch(float speed)
    {
        if (!Small || cast == null) return;
        float radians = cast.Angle * Mathf.Deg2Rad;
        velocity = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * speed;
        // Avoid near-zero residual horizontal components on vertical shots.
        if (cast.Angle == 90 || cast.Angle == 270) velocity.x = 0;
        if (cast.Angle == 0 || cast.Angle == 180) velocity.y = 0;
        var body = GetComponent<Rigidbody2D>();
        if (body != null) body.velocity = velocity;
        transform.rotation = Quaternion.Euler(0, 0, cast.Angle);
    }

    internal void BeforeState(FsmState state)
    {
        if (!Small || state.Fsm.Name != "Fireball Control") return;
        if (state.Name == "Idle" || state.Name == "Fireball Safe Return")
        {
            foreach (var action in state.Actions)
            {
                if (!edited.Add(action)) continue;
                if (action is SetVelocity2d set)
                {
                    var x = set.x; var y = set.y;
                    restore.Add(() => { set.x = x; set.y = y; });
                    set.x = new FsmFloat(velocity.x);
                    set.y = new FsmFloat(velocity.y);
                }
                // Native wall-stall detection only examines vx; vertical shots have vx=0.
                else if (action is FloatCompare && state.Name == "Idle")
                {
                    bool enabled = action.Enabled;
                    restore.Add(() => action.Enabled = enabled);
                    action.Enabled = false;
                }
            }
        }
    }

    internal void AfterState(FsmState state)
    {
        if (!Small || cast == null || state.Fsm.Name != "Fireball Control") return;
        if (state.Name == "Set Damage")
        {
            originalScale = transform.localScale;
            scaled = true;
            transform.localScale = new Vector3(Mathf.Abs(originalScale.x) * .5f, Mathf.Abs(originalScale.y) * .5f, originalScale.z);
            foreach (var damageFsm in GetComponentsInChildren<PlayMakerFSM>(true))
            {
                if (damageFsm.FsmName != "damages_enemy") continue;
                var damage = damageFsm.FsmVariables.FindFsmInt("damageDealt");
                var direction = damageFsm.FsmVariables.FindFsmFloat("direction");
                if (damage != null)
                {
                    int previous = damage.Value;
                    restore.Add(() => damage.Value = previous);
                    damage.Value = cast.Damage;
                }
                if (direction != null)
                {
                    float previous = direction.Value;
                    restore.Add(() => direction.Value = previous);
                    direction.Value = cast.Angle;
                }
            }
        }
        if (state.Name == "L" || state.Name == "R")
        {
            var scale = transform.localScale;
            transform.localScale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), scale.z);
        }
        if (state.Name == "Set Damage" || state.Name == "L" || state.Name == "R" || state.Name == "Idle")
            transform.rotation = Quaternion.Euler(0, 0, cast.Angle);
    }

    private void OnCollisionEnter2D(Collision2D collision) => Contact(collision.collider);
    private void OnTriggerEnter2D(Collider2D other) => Contact(other);
    private void Contact(Collider2D other)
    {
        if (!Small || cast == null || other == null || cast.Angle != 270) return;
        var nonBouncer = other.GetComponent<NonBouncer>();
        if (nonBouncer != null && nonBouncer.active) return;
        if (other.transform.IsChildOf(transform) || other.GetComponentInParent<HeroController>() != null ||
            other.GetComponentInParent<SpellProjectile>() != null) return;
        var layer = (PhysLayers)other.gameObject.layer;
        // Match native NailSlash pogo layers, plus explicit spike/tink and bounce components.
        if (layer == PhysLayers.ENEMIES || layer == PhysLayers.INTERACTIVE_OBJECT ||
            layer == PhysLayers.HERO_ATTACK || other.GetComponent<TinkEffect>() != null ||
            other.GetComponent<BounceShroom>() != null || other.GetComponent<BigBouncer>() != null)
            runtime?.ProjectilePogo(cast);
    }

    private void Restore()
    {
        for (int i = restore.Count - 1; i >= 0; i--) restore[i]();
        restore.Clear(); edited.Clear();
        if (scaled)
        {
            transform.localScale = originalScale;
            transform.rotation = originalRotation;
        }
        scaled = false; cast = null; runtime = null; velocity = Vector2.zero;
    }

    private void OnDisable() => Restore();
    private void OnDestroy() { Restore(); Instances.Remove(this); }

    internal static void RetireSmall()
    {
        foreach (var marker in new List<SpellProjectile>(Instances))
            if (marker != null && marker.Small) marker.gameObject.Recycle();
    }

    internal static void RestoreAll()
    {
        RetireSmall();
        foreach (var marker in new List<SpellProjectile>(Instances))
            if (marker != null) marker.Restore();
    }
}
