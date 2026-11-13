# TASK 0.6.4 report

Version: 0.6.4. Work performed on master without committing or pushing. DESIGN.md is the updated authority. No game, Xvfb, or Inspector was started or edited; cached decompilation/FSM data was read only.

## 1. Charge times increased by 50%

| Quantity | 0.6.4 value |
| --- | --- |
| First store, `GripAttack.ChargeSeconds` | 0.675 s accumulated gameplay time |
| Glow/winding, independent `GlowSeconds` | 0.3 s accumulated gameplay time |
| Second store, without / with Shaman Stone | 2.25 / 1.65 s total accumulated time |
| Fast Casting second store | 3.375 / 2.475 s |
| Tap, `TapSeconds` | unchanged: strictly below 0.2 s wall-clock |

Formula: `(shamanStone ? 1.65 : 2.25) * (fastCasting ? 1.5 : 1)`. First store is unaffected by Stone, Fast Casting, or Nailmaster's Glory. These are total thresholds, not additional time after the first store.

Updated MageGripTests, GripChargeTests, StageThreeTests, GripFireGateTests, both charm languages and description assertions, DESIGN, README, Directory.Build.props and GetVersion. Boundary, accumulated-time pause/resume/reset, frame partitioning and modifier tables use the new values. Winding explicitly checks GlowSeconds rather than TapSeconds. Historical README timing examples were updated too.

The timing-only build passed 307 tests (306 existing cases plus the independent glow boundary). No change to the 25-soul first cost, 48/36 second cost, small-shot interval `0.9 × ATTACK_COOLDOWN_TIME_CH`, slash animations, Collector refunds, Shadow Chain, held-state Tick or reset policy.

Residual risk: no live check of animation/audio perception at the longer thresholds. Existing rendering/input frame quantization remains. Tap and glow deliberately leave a 0.2–0.3 s interval with neither a valid tap nor glow.

## 2. Why the released spell can look normal

The confirmed problem is a misleading second-ready cue: 0.6.3 StoredTier used elapsed time alone, whereas Release checked soul. A player with 47 soul could see the second flash but release a 25-soul vanilla spell. Separately, the first flash is intentionally the vanilla unlocked spell, including Shade Soul when unlocked; it is not the enlarged spell.

Changed StoredTier and Winding to accept soul and Twister; SyncVisuals supplies live MPCharge and equippedCharm_33. The second flash now requires the second threshold AND 48 soul, or 36 with Twister. First-tier timing feedback remains independent of soul, preserving its existing behavior. At the second time threshold without its soul cost, the displayed tier remains 1 and winding stops; gaining enough soul while holding enables the second flash. Soul falling below that cost returns the displayed tier to 1.

Chosen release policy: retain the existing affordable fallback. After losing soul following a second flash, release gives NormalSpell if at least 25 remains, otherwise None. It never claims ChargedSpell or spends 48/36 without sufficient soul. A queued ChargedSpell keeps its kind and rechecks the full cost in TrySpell; it does not downgrade during the one-second wait. Damage/reset events may cancel it. These policies and the tier distinction are documented in DESIGN and ZH/EN descriptions.

### Required hypothesis verdicts

1. **Only tier 1 — expected.** Release at 0.675 s returns NormalSpell and costs 25. TrySpell reads the unlocked level, enters Fireball Antic with Spell Level set, and native Level Check selects Fireball 2 for level >1. Quake and Scream likewise retain their unlocked tiers. SpellRuntime creates a NormalSpell record; charged damage/scale edits are conditional on ChargedSpell. Native Shaman Stone behavior is untouched. Added an explicit first-store + fireballLevel 2 / ShadeSoulState regression.

2. **Second flash without enough soul — confirmed and fixed.** The previous time-only StoredTier disagreed with Release. Tests at 25/47 soul, 35 with Twister, and soul loss after the flash failed before the affordability condition was added. The red run compiled cleanly but had 5 failures / 310 passes. The fix uses the same Cost function as Release; 48 and 36 remain successful boundary cases. Runtime source-policy tests verify actual visual wiring to current soul and Twister.

3. **Pending loses tier — no loss found.** GripPendingCast.Queue retains NormalSpell or ChargedSpell plus direction; TryGet does not reclassify. OnUpdate passes pendingRelease to TryFire, which passes release to TrySpell; prepaidKind is GripAttack.Kind(release). Existing deadline/cancellation tests were updated, and new scenario tests verify a blocked second-store release flushes as CastKind.ChargedSpell. Taps are never queued.

4. **prepaidFsm / spawnContext mismatch — no mismatch found in the checked vanilla paths.** In the cached `Knight-Spell Control-20204.json`, native TakeMP(33) is in Fireball 1/2, Level Check 2 (Quake), and Scream Burst 1/2. All belong to the hero's Spell Control FSM. TrySpell prepays the selected 25/48/36, records kind and Stone before SetState, and BeforeSpellState suppresses exactly the matching native debit. SpellKind is queried on the parent Spell Control entries, not on projectile child FSMs. Children inherit the CastRecord through spawnContext, sources/ancestor lookup, and Fireball ActivationCast. Pool reuse does not call SpellKind and therefore does not hit its NormalSpell fallback. Terminal Inactive/Cancel All/Spell End clears prepaid state. Source-policy tests lock the pending → prepaid → context wiring. No unsafe global charged fallback was introduced.

5. **Wrong object / premature restore — no demonstrated defect in the Fireball, Quake, Scream and ordinary SpellFluke paths inspected.** Fireball Cast `L or R` and Fireball Control `Set Damage` stamp the activation; OnPoolSpawn also stamps it for synchronous or delayed initialization. sources.Remove discards stale ownership before rebinding. Hero Quake parts (Q Fall Damage, Q Slam, Q Slam 2, Q Mega) and the correct Scr Heads root are bound before native activation. Quake Antic activates Q Charge, not Q Fall Damage; the later Enter Quake rebind happens before Quake1/2 Down activates the damage object, so this is not an observed mid-hit damage restore. ChargedSpellEffect edits Set Damage setters using SpellDamage.Calculate and restores action references after execution. Only the range root grows 1.45×: Fireball after Set Damage, Q Fall after Set Damage, slam/mega after Pos/Neg, Scr Heads after Wait following native unparent/scale setup. Child damage FSMs inherit the source without independently doubling scale. OnDisable restores activation-local changes, RestoreTree clears prior hero-part effects before binding, and Prepare restores when the cast changes. Ordinary SpellFluke.OnEnable resets native scale/damage first; the hook then applies charged damage from base 4 and enlarges its root. Pool/Unity activation order still requires live verification; static inspection is not a runtime proof. Special Dung/Flukenest variants also warrant explicit live coverage.

6. **Vanilla Quick Cast interception — no competing QC route found.** Read the installed Assembly-CSharp ListenForQuickCast and ListenForCast with the existing ilspycmd, without writing the cached sources. ListenForQuickCast.OnEnter only initializes references; all QC input checks are in OnUpdate, which Grip swallows while Active. ListenForCast checks inputActions.cast exclusively, not quickCast. Cached Spell Control Inactive routes QUICK CAST to Can Cast? QC, but the swallowed listener does not emit that event. The normal Cast key remains intentionally vanilla. Parent debit suppression and terminal cleanup prevent a leftover prepaid transaction from becoming a second native charge in the inspected route. External mods or binding both actions to one physical button are outside this verification.

7. **Immediate / airborne / dash / attack / recoil release — no wrong-kind path found.** Core tests cover immediate second-store release, mid-air-equivalent unrestricted gate, just-after-dash gate, and deferral during dash/attack/recoil. All preserve ChargedSpell through flush. Airborne itself is not a gate restriction. Dash permits charge accumulation but blocks firing; attack/recoil pause accumulation and retain the stored time. Actual damage calls OnHurt and clears charge/pending, as designed; it does not turn a charged intent into a normal spell. ResetMotion (including hard/dash landing), no-input and control loss can similarly cancel, not downgrade. A new hold replaces pending intent, and the one-second deadline can discard it.

### Validation and limits

Final `bash scripts/build.sh`: build succeeded with 0 warnings and 0 errors; all 325 xUnit cases passed (0 failed, 0 skipped). `git diff --check` passed. Coverage includes including timing/glow boundaries, affordable visual tier, release after soul loss, first-tier Shade Soul, six release/gate scenarios, description numbers/tier distinction in both languages, glyph coverage, and runtime wiring checks. Core tests model state snapshots; source-policy assertions cannot execute Unity FSMs, rendering, audio or object-pool callbacks.

No damage buff was added to tier 1. Tier 2 retains SpellDamage.Calculate(charged: true) ×2 and the existing 1.45× range-root implementation, including the ordinary fluke hook. No observed gameplay episode was available to identify which explanation caused the user's particular report. The executor should verify actual Fireball/Dive/Shriek and fluke scales/damage, pooled charged→normal reuse, second flash at 47/48 and 35/36 soul, soul loss after flashing, and release immediately after store/during gates. Longer bilingual text also needs an in-game layout check.

## Live-verify (executor, 13 Nov 2026 22:59 Asia/Shanghai)

Item 2 was scoped down after a user update: the “not 大大法术” report is tier-1 (normal-size vanilla) vs tier-2 confusion; no deep launch-path hunt was required. The second-flash-requires-soul visual alignment Codex already implemented was kept (Release fallback unchanged). No further code change after this run.

Inspector 064-live, xvfb 960×540, fireballLevel=2, charms 41+42, MP 99, no Shaman Stone / Twister / Fast Casting. Timestamps from driver.log:

| Scenario | Expected | Actual | OK |
| --- | --- | --- | --- |
| Glow start | ~0.300 s | 0.319 s | yes |
| First store | ~0.675 s (not 0.45) | 0.711 s | yes |
| Second store | ~2.250 s | 2.219 s (logged at ≥2.20) | yes |
| T1 ground | vanilla Shade Soul, scale 1.80, dmg 30, −25 MP, no ChargedSpellEffect | Spiral 1.80, dmg 30, MP 99→74, cse=False | yes |
| T2 ground | ×1.45 scale, ×2 dmg, −48 MP, ChargedSpellEffect | Spiral 2.61, dmg 60, MP 99→51, cse=True | yes |
| T2 mid-air | same charged, airborne | 2.61 / 60 / 99→51, air=True | yes |
| T2 mid-dash then after | no projectile while dashing; charged flush after | new=0, MP still 99 mid-dash; then 2.61 / 60 / 99→51 | yes |

First-store sample is ~36 ms after the 0.675 threshold (one-to-two frames of detection), consistent with earlier live runs. Installed SHA-256: Spellcaster.Mod `ccd62940bf861cd680b7611fc25c406b6f57e56e77154781c49700ed6969e1a6`, Spellcaster.Core `5c52e3974e981da00324d6f9c64c1af23669c16a095602a7801643590e19bc15`. Contact sheet: `.cache/shots/charm-064-contact.png` (not committed). Inspector.dll restored to stage1 (7680). No gameplay bug; no additional Codex cycle.
