using System;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace Spellcaster.Mod;

/// <summary>
/// Adapt only the native nail-only receiver's trigger dispatch. No broadcast HIT events,
/// persistent tags/layers, enemy damage conversion or floor/terrain destruction.
/// </summary>
internal static class ProjectileInteractions
{
    internal static void Install()
    {
        On.HutongGames.PlayMaker.Actions.Trigger2dEvent.DoTriggerEnter2D += OnFsmTrigger;
        On.HutongGames.PlayMaker.Actions.Trigger2dEvent.DoTriggerStay2D += OnFsmStay;
        On.BridgeLever.OnTriggerEnter2D += OnBridgeTrigger;
        On.HutongGames.PlayMaker.Actions.Collision2dEvent.DoCollisionEnter2D += OnFsmCollision;
        On.HutongGames.PlayMaker.Actions.Collision2dEvent.DoCollisionStay2D += OnFsmCollisionStay;
    }
    internal static void Uninstall()
    {
        On.HutongGames.PlayMaker.Actions.Trigger2dEvent.DoTriggerEnter2D -= OnFsmTrigger;
        On.HutongGames.PlayMaker.Actions.Trigger2dEvent.DoTriggerStay2D -= OnFsmStay;
        On.BridgeLever.OnTriggerEnter2D -= OnBridgeTrigger;
        On.HutongGames.PlayMaker.Actions.Collision2dEvent.DoCollisionEnter2D -= OnFsmCollision;
        On.HutongGames.PlayMaker.Actions.Collision2dEvent.DoCollisionStay2D -= OnFsmCollisionStay;
    }

    private static bool Projectile(Collider2D collision) => collision != null &&
        collision.GetComponentInParent<SpellProjectile>() is SpellProjectile marker && marker.Tracked;

    private static void AsNail(Collider2D collision, Action dispatch)
    {
        string tag = collision.gameObject.tag;
        try { collision.gameObject.tag = "Nail Attack"; dispatch(); }
        finally { if (collision != null) collision.gameObject.tag = tag; }
    }

    private static void OnBridgeTrigger(On.BridgeLever.orig_OnTriggerEnter2D orig, BridgeLever self, Collider2D collision)
    {
        if (Projectile(collision)) AsNail(collision, () => orig(self, collision));
        else orig(self, collision);
    }

    private static bool NailReceiver(FsmStateAction self, string tag, Collider2D collision) =>
        Projectile(collision) && self.Owner != null && self.Owner.GetComponentInParent<HealthManager>() == null &&
        self.Owner.GetComponentInParent<HeroController>() == null && tag == "Nail Attack";

    private static void OnFsmTrigger(On.HutongGames.PlayMaker.Actions.Trigger2dEvent.orig_DoTriggerEnter2D orig,
        Trigger2dEvent self, Collider2D collision)
    {
        if (NailReceiver(self, self.collideTag.Value, collision)) AsNail(collision, () => orig(self, collision));
        else orig(self, collision);
    }

    private static void OnFsmStay(On.HutongGames.PlayMaker.Actions.Trigger2dEvent.orig_DoTriggerStay2D orig,
        Trigger2dEvent self, Collider2D collision)
    {
        if (NailReceiver(self, self.collideTag.Value, collision)) AsNail(collision, () => orig(self, collision));
        else orig(self, collision);
    }
    private static void OnFsmCollision(On.HutongGames.PlayMaker.Actions.Collision2dEvent.orig_DoCollisionEnter2D orig,
        Collision2dEvent self, Collision2D collision)
    {
        if (NailReceiver(self, self.collideTag.Value, collision.collider))
            AsNail(collision.collider, () => orig(self, collision));
        else orig(self, collision);
    }

    private static void OnFsmCollisionStay(On.HutongGames.PlayMaker.Actions.Collision2dEvent.orig_DoCollisionStay2D orig,
        Collision2dEvent self, Collision2D collision)
    {
        if (NailReceiver(self, self.collideTag.Value, collision.collider))
            AsNail(collision.collider, () => orig(self, collision));
        else orig(self, collision);
    }

}
