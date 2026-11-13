using System;
using System.Collections.Generic;
using GlobalEnums;
using HutongGames.PlayMaker;
using Modding;
using Spellcaster.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Spellcaster.Mod;

public sealed class SpellRuntime : MonoBehaviour
{
    private readonly HeatMeter heat = new HeatMeter();
    private readonly SoulLedger ledger = new SoulLedger();
    private readonly SoulAccumulator spellSoul = new SoulAccumulator();
    private readonly SoulAccumulator nailSoul = new SoulAccumulator();
    private readonly Dictionary<GameObject, DamageSource> sources = new Dictionary<GameObject, DamageSource>();
    private readonly List<CastRecord> casts = new List<CastRecord>();
    private readonly HashSet<long> liveCasts = new HashSet<long>();
    private readonly HeatFeedback feedback = new HeatFeedback();
    private CastRecord? current;
    private CastRecord? spawnContext;
    private bool installed;
    private bool collector;
    private double lastTime;
    private HeroController? hero;
    public double Heat => heat.Heat;
    public bool CollectorEnabled => collector;

    public void Install(bool enableCollector)
    {
        if (installed) return;
        installed = true;
        On.HutongGames.PlayMaker.FsmState.OnEnter += OnStateEnter;
        On.SpellFluke.OnEnable += OnFlukeEnable;
        ModHooks.BeforeSceneLoadHook += BeforeSceneLoad;
        ModHooks.AfterSavegameLoadHook += AfterLoad;
        ModHooks.NewGameHook += ResetAll;
        ModHooks.BeforePlayerDeadHook += ResetAll;
        UnityEngine.SceneManagement.SceneManager.activeSceneChanged += OnSceneChanged;
        ResetAll();
        SetCollector(enableCollector);
    }

    public void SetCollector(bool enabled)
    {
        if (!installed || enabled == collector) return;
        collector = enabled;
        if (enabled)
        {
            On.HealthManager.TakeDamage += OnDamage;
            On.SpellFluke.DoDamage += OnFlukeDamage;
            ModHooks.SoulGainHook += OnNailSoul;
        }
        else
        {
            On.HealthManager.TakeDamage -= OnDamage;
            On.SpellFluke.DoDamage -= OnFlukeDamage;
            ModHooks.SoulGainHook -= OnNailSoul;
        }
        ResetSoul();
    }

    public void Uninstall()
    {
        if (!installed) return;
        SetCollector(false);
        On.HutongGames.PlayMaker.FsmState.OnEnter -= OnStateEnter;
        On.SpellFluke.OnEnable -= OnFlukeEnable;
        ModHooks.BeforeSceneLoadHook -= BeforeSceneLoad;
        ModHooks.AfterSavegameLoadHook -= AfterLoad;
        ModHooks.NewGameHook -= ResetAll;
        ModHooks.BeforePlayerDeadHook -= ResetAll;
        UnityEngine.SceneManagement.SceneManager.activeSceneChanged -= OnSceneChanged;
        ResetAll();
        installed = false;
    }

    private void OnDestroy() => Uninstall();
    private string BeforeSceneLoad(string scene) { ResetAll(); return scene; }
    private void OnSceneChanged(Scene from, Scene to) => ResetAll();
    private void AfterLoad(SaveGameData data) => ResetAll();
    private void ResetSoul()
    {
        ledger.Reset(); spellSoul.Reset(); nailSoul.Reset();
        sources.Clear(); casts.Clear(); current = null; spawnContext = null;
    }
    private void ResetAll()
    {
        ResetSoul(); heat.Reset(); feedback.Reset();
        hero = null; lastTime = Time.timeAsDouble;
    }

    private bool CanPlay => installed && GameManager.UnsafeInstance != null &&
        GameManager.UnsafeInstance.IsGameplayScene() && !GameManager.UnsafeInstance.isPaused &&
        GameManager.UnsafeInstance.gameState == GameState.PLAYING && hero != null && !hero.cState.dead;

    private void AdvanceClock()
    {
        double now = Time.timeAsDouble;
        double elapsed = Math.Max(0, now - lastTime);
        lastTime = now;
        if (CanPlay && heat.Advance(elapsed)) feedback.FlashCooled();
    }

    private void Update()
    {
        if (!installed) return;
        var nextHero = HeroController.instance;
        if (nextHero != hero)
        {
            ResetAll();
            hero = nextHero;
        }
        AdvanceClock();
        if (CanPlay) feedback.Update(hero!, heat.Heat);
        CleanupCasts();
    }

    private void CleanupCasts()
    {
        liveCasts.Clear();
        var remaining = new Dictionary<GameObject, DamageSource>();
        foreach (var pair in sources)
        {
            GameObject key = pair.Key;
            bool dead = key == null || (!key.activeInHierarchy && pair.Value.Cast.CreatedFrame < Time.frameCount);
            if (dead) continue;
            remaining[key!] = pair.Value;
            liveCasts.Add(pair.Value.Cast.Id);
        }
        if (remaining.Count != sources.Count)
        {
            sources.Clear();
            foreach (var pair in remaining) sources.Add(pair.Key, pair.Value);
        }
        for (int i = casts.Count - 1; i >= 0; --i)
        {
            var cast = casts[i];
            if (cast != current && cast.CreatedFrame < Time.frameCount && !liveCasts.Contains(cast.Id))
            { ledger.EndCast(cast.Id); casts.RemoveAt(i); }
        }
    }

    private CastRecord BeginCast()
    {
        AdvanceClock();
        heat.AddCast(CastKind.NormalSpell);
        current = new CastRecord(ledger.BeginCast());
        casts.Add(current);
        return current;
    }

    private void Bind(GameObject? root, CastRecord cast, int group)
    {
        if (root != null) sources[root] = new DamageSource(cast, group);
    }

    private DamageSource? FindSource(GameObject? go)
    {
        if (go == null) return null;
        for (var t = go.transform; t != null; t = t.parent)
            if (sources.TryGetValue(t.gameObject, out var source)) return source;
        return null;
    }

    private void BindHeroSpell(CastRecord cast, string state)
    {
        if (hero == null) return;
        var spells = hero.transform.Find("Spells");
        if (spells == null) return;
        if (state.StartsWith("Scream", StringComparison.Ordinal))
        {
            Bind(spells.Find(state == "Scream Burst 2" ? "Scr Heads 2" : "Scr Heads")?.gameObject, cast, 0);
        }
        else if (state == "Quake Antic" || state == "Enter Quake")
        {
            Bind(spells.Find("Q Fall Damage")?.gameObject, cast, 0);
            Bind(spells.Find("Q Slam")?.gameObject, cast, 1);
            Bind(spells.Find("Q Slam 2")?.gameObject, cast, 1);
            Bind(spells.Find("Q Mega")?.gameObject, cast, 2);
        }
    }

    private void OnStateEnter(On.HutongGames.PlayMaker.FsmState.orig_OnEnter orig, FsmState state)
    {
        var fsm = state.Fsm;
        var owner = fsm?.GameObject;
        CastRecord? context = null;
        // Use committed cast states, not button presses, MP changes or focus/healing states.
        if (hero != null && owner == hero.gameObject && fsm!.Name == "Spell Control")
        {
            string name = state.Name;
            if (name == "Fireball 1" || name == "Fireball 2" || name == "Quake Antic" ||
                name == "Enter Quake" || name == "Scream Burst 1" || name == "Scream Burst 2")
            {
                context = BeginCast();
                BindHeroSpell(context, name);
            }
            else if (name == "Spell End" || name == "Cancel All") current = null;
            else context = current;
        }
        else if (owner != null)
        {
            // A pooled object's initialization must overwrite its old cast, never the next cast at hit time.
            bool projectileStart = (fsm!.Name == "Fireball Cast" && state.Name == "L or R") ||
                (fsm.Name == "Fireball Control" && state.Name == "Set Damage") ||
                (owner.name.StartsWith("Spell Fluke Dung", StringComparison.Ordinal) && state.Name == "Init") ||
                (owner.name.StartsWith("Dung Explosion", StringComparison.Ordinal) && state.Name == "Explode");
            if (projectileStart && spawnContext != null)
                Bind(owner, spawnContext, owner.name.StartsWith("Spell Fluke", StringComparison.Ordinal) ? spawnContext.NextGroup++ : 0);
            context = FindSource(owner)?.Cast;
        }
        var previous = spawnContext;
        spawnContext = context ?? previous;
        try { orig(state); }
        finally { spawnContext = previous; }
    }

    private void OnFlukeEnable(On.SpellFluke.orig_OnEnable orig, SpellFluke self)
    {
        sources.Remove(self.gameObject); // Object pool reuse cannot inherit a retired cast.
        if (spawnContext != null) Bind(self.gameObject, spawnContext, spawnContext.NextGroup++);
        orig(self);
    }

    private void OnDamage(On.HealthManager.orig_TakeDamage orig, HealthManager self, HitInstance hit)
    {
        var source = hit.AttackType == AttackTypes.Spell ? FindSource(hit.Source) : null;
        int hpBefore = self != null ? self.hp : 0;
        int targetId = self != null ? self.GetInstanceID() : 0;
        orig(self, hit);
        // Invincible/rejected hits never reach TakeDamage; still check actual HP reduction,
        // including lethal hits (Unity Destroy is deferred until the end of the frame).
        if (source != null && self != null && self.hp < hpBefore) Reward(source, targetId);
    }

    private void OnFlukeDamage(On.SpellFluke.orig_DoDamage orig, SpellFluke self, GameObject target, int recursion, bool burst)
    {
        var source = self != null ? FindSource(self.gameObject) : null;
        var health = target != null ? target.GetComponent<HealthManager>() : null;
        int hpBefore = health != null ? health.hp : 0;
        int targetId = health != null ? health.GetInstanceID() : 0;
        orig(self, target, recursion, burst);
        if (source != null && health != null && health.hp < hpBefore) Reward(source, targetId);
    }

    private void Reward(DamageSource source, int targetId)
    {
        if (!collector || !CanPlay || PlayerData.instance == null) return;
        AdvanceClock();
        var data = PlayerData.instance;
        int segment = source.Segment(targetId, Time.fixedTimeAsDouble);
        double amount = ledger.Hit(source.Cast.Id, segment, heat.Heat,
            data.GetBool("equippedCharm_20"), data.GetBool("equippedCharm_21"));
        if (data.GetInt("MPCharge") >= data.GetInt("maxMP") && data.GetInt("MPReserve") >= data.GetInt("MPReserveMax"))
        { spellSoul.Reset(); return; }
        int whole = spellSoul.Add(amount);
        if (whole > 0 && hero != null && GameCameras.instance != null && GameCameras.instance.soulOrbFSM != null &&
            GameManager.UnsafeInstance.soulVessel_fsm != null)
            hero.AddMPCharge(whole); // Bypasses SoulGainHook; no recursive halving of spell refunds.
    }

    private int OnNailSoul(int amount) => nailSoul.Add(Math.Max(0, amount) / 2.0);
}
