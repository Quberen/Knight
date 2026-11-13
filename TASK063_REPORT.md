# TASK 0.6.3 report

Implemented on `master`, without committing or launching Hollow Knight. Version is 0.6.3 in Directory.Build.props and GetVersion. DESIGN.md now defines the corrected charging and firing rules; README documents them.

## 1. Held-state charging

**Problem.** The old Press method required a press edge and an expired shot interval. Charge tiers were derived from elapsed wall time. Holding through an unavailable state could never start charging, while an existing hold incorrectly counted blocked time. The old runtime also treated pause as a blanket charge cancellation.

**Vanilla findings, read directly from `.cache/decompiled/HeroController.cs`.**

- CanNailCharge, lines 4780–4787: `!attacking && !controlReqlinquished && !recoiling && !recoilingLeft && !recoilingRight && hasNailArt`.
- orig_Update, lines 5374–5417: held attack plus CanNailCharge adds deltaTime; the unavailable branch hides winding visuals without zeroing the timer. Pause while held skips accumulation. Pause plus release zeros it. Vanilla winding starts after 0.5 seconds; Grip deliberately keeps 0.2 seconds.
- HeroDash, line 3517, calls ResetAttacksDash; lines 4121–4127 clear attack flags but preserve the timer. Dash is not a charging blocker.
- TakeDamage, lines 1985–1993, clears vanilla charge and invokes OnTakenDamage for actual damage.
- RelinquishControl / NotVelocity, lines 2871–2901, reset motion/attacks and input. ResetMotion, lines 4129–4142, zeros charge; FixedUpdate lines 927–929 invokes it for hard landing off conveyors and dash landing. The other ResetMotion call sites cover scene entry, respawn, death, hazard paths, damage recoil and fail-safe recovery.
- ResetAttacks (4111–4119) also zeros vanilla charge, including ordinary attack completion at 5205. QC deliberately does not hook ResetAttacks or nail-art release: the requested QC behavior preserves charge across ordinary attacking/recoil blockers and resumes after attacks. This is the explicit QC adaptation, rather than copying every nail-button cancellation.

**Implementation.** GripAttack owns ChargeElapsed and physical hold age separately. Tick observes held state every frame, starts without WasPressed or shot-cooldown eligibility, and adds gameplay delta only when held, chargeable, unpaused and not resetting. Runtime supplies Time.deltaTime and uses Time.unscaledTimeAsDouble for genuine press duration. Core chargeability is the five negated vanilla booleans above, omitting hasNailArt. Runtime additionally excludes reset states: no gameplay scene (or non-playing state other than pause), transitioning, dead, hazardDeath, hazardRespawning, no_input and controlReqlinquished. Dash, ordinary landing, walking, jumping, turning and wall sliding are absent from blockers.

Ordinary attacking/recoil and held pause preserve accumulated charge. Visuals hide while blocked and return on resume, still after orig(Update), respecting concurrent vanilla nail-art visual ownership. OnTakenDamage and symmetric ResetMotion / RelinquishControlNotVelocity hooks clear charge and pending casts; the latter only resets on actual control relinquishment. Regular RelinquishControl is covered by its ResetMotion call and the runtime control flag. Pause plus release cancels the hold. ResetCharge retains original button age and disqualifies tap fallback; an already-held button can then accumulate from zero without another edge. No vanilla nailChargeTimer or cState.nailCharging writes were added.

**Constants.** Tap <0.2 seconds of wall time; winding from 0.2 accumulated seconds; first store 0.45 accumulated seconds, 25 soul, no NMG shortening. Second store 1.5 seconds, or 1.1 with Shaman Stone; existing Fast Casting second-tier multiplier 1.5 retained; 48 soul / 36 with Spell Twister. Completed stores remain while held. Unfinished long holds release nothing.

**Tests.** Updated MageGripTests and StageThreeTests to explicitly Tick accumulated time, retaining tier, cost, unlock, winding, interval and cancellation coverage. New GripChargeTests cover each charge blocker, mid-hold start, pause/resume, dash/ordinary-landing chargeability, resets without new edges, pause-plus-release, reset tap suppression, long blocked holds, Shaman second tiers across pauses, and gameplay delta versus wall time. Frame-partition tests now advance the charge timer per simulated frame. Item 1 build passed with 274 tests before item 2; final suite additionally includes three charge timing cases.

**Residual risks.** Unity input/FSM timing and visual ownership need live verification; this task intentionally permits only compilation and unit tests. Frame sampling can move a threshold by roughly one frame, as with vanilla deltaTime charging. The requested generic attack/recoil pause policy differs from vanilla's ResetAttacks-only cancellation sites (long horizontal recoil and explicit attack cancellation included); ResetMotion and actual damage still reset QC. No game or Inspector verification is claimed.

## 2. Release and pending fire gating

**Problem.** Small shots spawned without checking hero control state. Stored spells used a deliberately loose FSM-only check, allowing release during dash or other unavailable states. A failed tap could also enter the pending mechanism.

**Vanilla findings.** CanAttack (4771–4778) requires attack_cooldown <=0, no attacking/dashing/death/hazard states/control relinquishment, and neither no_input nor hard_landing/dash_landing. CanCast (2973–2980) adds pause, backdash, recoil/freeze, transitioning, CanInput and preventCastByDialogueEndTimer <=0; its attack recovery allowance is subsumed by CanAttack's stricter !attacking. CanInput (1770–1773) returns acceptingInput. Neither CanAttack nor CanCast blocks wallSliding. The cached Inventory Control FSM (`.cache/allfsms/Inventory-Inventory Control-23035.json`, around 1780) calls RelinquishControl, so its control/input state is covered. These firing restrictions do not imply resetting charge: brief dash/recoil blockers can retain a completed release in the pending window.

**Exact predicate.** GripFireGate.Allowed requires Gameplay && CanInput, AttackCooldown <=0, DialogueEndTimer <=0, and all of these false: Paused, Attacking, Dashing, Dead, HazardDeath, HazardRespawning, ControlRelinquished, NoInput, HardLanding, DashLanding, BackDashing, Recoiling, RecoilingLeft, RecoilingRight, RecoilFrozen, Transitioning, Focusing, AtBench, InAcid. Runtime maps these directly from runtime.CanPlay, HeroController.CanInput(), GameManager.isPaused, hero_state, cState, controlReqlinquished, PlayerData.atBench, both acid flags, attack_cooldown and preventCastByDialogueEndTimer. Private values use cached reflection fields. No wall-slide prohibition or attack hooks were added.

Release checks the gate: a blocked tap becomes None even with charged deferral enabled. Charged releases may return an intent for deferral, but TryFire checks the predicate and interval before any spawn, debit or FSM entry. Pending.TryGet also checks the predicate on flush, expires after the existing inclusive one-second window, and Queue accepts only NormalSpell / ChargedSpell. Reset conditions clear pending state. Actual spell execution still requires the idle Spell Control FSM and rechecks unlock and soul. Pending preserves direction and tier, not permission to fire.

Small shots still commit `0.9 × ATTACK_COOLDOWN_TIME_CH` (0.225 seconds with vanilla 0.25), and charging can start during that interval. Existing charged-spell interval behavior remains. Directional Slash/UpSlash/DownSlash, no nail hitbox, Collector-only ordinary refunds, Shadow Chain, acquisition behavior and regular Cast/nail input were left intact.

**Tests.** GripFireGateTests individually exercise every boolean restriction against tap, normal and charged releases; timer boundaries, clean state and multiple blockers; dash-deferred normal/charged casts, direction retention, expiry while blocked, and clearing; tap rejection without pending; cooldown rejection followed by a valid tap. Existing interval and combat tests remain green.

**Residual risks.** Compilation validates API v77 hooks and public fields, but private-field reflection and live bench/dialogue/focus/menu transitions need executor verification. Gate mappings use actual game state rather than guessed FSM names; other mods may alter those states. Paused visuals may replay their ready cue when restored. No live projectile or animation test was performed.

## Verification and scope

- `bash scripts/build.sh`: Release build with TreatWarningsAsErrors, zero warnings/errors; **306 passed, 0 failed, 0 skipped**.
- `git diff --check`: clean.
- New hooks have matching unregistration; damage subscription cleanup remains symmetric.
- No Hollow Knight/Xvfb launch, Unity asset unpacking, Inspector edit, commit, push, reset, rebase or branch change. Existing untracked REVIEW.md / TASK062.md / TASK063.md left untouched.
- No 冷静施法, formal acquisition, overheat, nail-button takeover, or writes to vanilla charge state.
