using System;
using System.Collections.Generic;
using System.Linq;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using Spellcaster.Core;
using UnityEngine;

namespace Spellcaster.Mod;

/// <summary>Activation-local changes. Never edits prefab data or PlayerData charm flags.</summary>
internal sealed class ChargedSpellEffect : MonoBehaviour
{
    private static readonly HashSet<ChargedSpellEffect> Instances = new HashSet<ChargedSpellEffect>();
    private readonly Dictionary<FsmInt, int> damageRestore = new Dictionary<FsmInt, int>();
    private readonly List<Action> restore = new List<Action>();
    private CastRecord? cast;
    private Vector3 originalScale;
    private Vector3 enlargedScale;
    private bool scaled;
    private bool rangeRoot;

    internal static ChargedSpellEffect Prepare(GameObject owner, CastRecord cast, bool rangeRoot = false)
    {
        var effect = owner.GetComponent<ChargedSpellEffect>() ?? owner.AddComponent<ChargedSpellEffect>();
        Instances.Add(effect);
        if (effect.cast != cast) effect.Restore();
        effect.cast = cast;
        effect.rangeRoot |= rangeRoot;
        return effect;
    }

    // Replace both native branches with the same Core result. The first setter is
    // the uncharmed base; the second must not apply Shaman Stone a second time.
    internal Action? BeforeDamageState(FsmState state)
    {
        if (cast == null || state.Name != "Set Damage" ||
            (state.Fsm.Name != "Set Damage" && state.Fsm.Name != "Fireball Control")) return null;
        var setters = state.Actions.OfType<SetFsmInt>()
            .Where(a => a.fsmName.Value == "damages_enemy" && a.variableName.Value == "damageDealt").ToArray();
        if (setters.Length == 0) return null;
        int damage = SpellDamage.Calculate(setters[0].setValue.Value, true, cast.ShamanStone);
        var originals = setters.Select(a => a.setValue).ToArray();
        foreach (var fsm in GetComponents<PlayMakerFSM>())
        {
            if (fsm.FsmName != "damages_enemy") continue;
            var value = fsm.FsmVariables.FindFsmInt("damageDealt");
            if (value != null && !damageRestore.ContainsKey(value)) damageRestore.Add(value, value.Value);
        }
        foreach (var setter in setters) setter.setValue = new FsmInt(damage);
        return () => { for (int i = 0; i < setters.Length; i++) setters[i].setValue = originals[i]; };
    }

    internal void AfterState(FsmState state)
    {
        if (cast == null || !rangeRoot || !gameObject.activeInHierarchy) return;
        // Only after native scale initialization. Scaling earlier pollutes native
        // GetScale caches (especially scream unparenting and quake facing changes).
        bool ready = state.Fsm.Name == "Fireball Control" && state.Name == "Set Damage" ||
            state.Fsm.Name == "Set Damage" && state.Name == "Set Damage" ||
            state.Fsm.Name == "Hit Box Control" && (state.Name == "Pos" || state.Name == "Neg") ||
            state.Fsm.Name == "FSM" && state.Name == "Wait";
        if (ready) Enlarge();
    }

    internal void Enlarge()
    {
        if (cast == null || !rangeRoot || scaled) return;
        originalScale = transform.localScale;
        enlargedScale = new Vector3(originalScale.x * 1.45f, originalScale.y * 1.45f, originalScale.z);
        transform.localScale = enlargedScale;
        scaled = true;
    }

    internal void RestoreWith(Action action) => restore.Add(action);

    private void Restore()
    {
        foreach (var pair in damageRestore) pair.Key.Value = pair.Value;
        damageRestore.Clear();
        for (int i = restore.Count - 1; i >= 0; i--) restore[i]();
        restore.Clear();
        if (scaled) transform.localScale = originalScale;
        scaled = false; rangeRoot = false; cast = null;
    }

    private void OnDisable() => Restore();
    private void OnDestroy() { Restore(); Instances.Remove(this); }

    internal static void RestoreTree(GameObject owner)
    {
        foreach (var effect in owner.GetComponentsInChildren<ChargedSpellEffect>(true)) effect.Restore();
    }

    internal static void RestoreAll()
    {
        foreach (var effect in new List<ChargedSpellEffect>(Instances))
            if (effect != null) effect.Restore();
    }
}
