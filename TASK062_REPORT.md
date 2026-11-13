# TASK 0.6.2 report

Implemented on `master` in `/workspace/hk-mod`. Version is 0.6.2 in `Directory.Build.props` and `SpellcasterMod.GetVersion`. DESIGN.md is updated to the requested rules; README now documents them and the 262-test count.

## 1. Collector-only ordinary spell refunds

**Wrong:** the previous review installed damage hooks globally and allowed ordinary spell refunds without charm 43.

**Changed:** `SpellRuntime.RefreshDamageHooks` now requires `installed && (collector || grip != null)`. `Reward` rejects `small ? grip == null : !collector` before reward bookkeeping. Ordinary spells require Collector; Grip small shots retain their independent eligibility. Existing actual-HP-loss checks, castId/segment deduplication and Collector nail-soul halving remain.

**Numbers:** ordinary hit segments award 9 + Catcher 3 + Eater 8 (9/12/17/20); without Collector they award 0. Small shots still award half nail soul: `(11 + 3 + 8)/2`, or the applicable reserve formula `(6 + 2 + 6)/2`, with optional charm additions and fractional carry. Core SoulFormula/SoulLedger remain equipment-independent math.

**Tests:** added `RuntimeWithholdsOrdinaryRefundsWithoutCollector`, a source-level regression check of the Unity runtime gates and HP-loss guard. Existing mathematical and deduplication tests remain. First build: 258 passing tests.

**Residual risks:** this source-level check does not execute Unity damage hooks. Actual-hit, pooled-projectile and other-mod integration remain untested in this run.

## 2. Unlimited ground and air Shadow Chain

**Wrong:** Core restricted extra airborne attempts to one and landing cleared the price/window. The runtime repeated the air restriction. Landing during `FinishedDashing` could erase the timestamp before `ExtendWindow`.

**Changed:** removed MaxAirChains and all air-count eligibility limits; landing no longer resets the sequence. `AirChains` remains diagnostic only, and `CanExtraAirDash` simply tests the window. Runtime `CanDash` borrows zero dash cooldown and an unused air dash, calls native eligibility, and restores both in `finally`. Paid `HeroDash` borrows zero shadow cooldown, then restores the native cooldown on success or the previous value on rejection/exception. These existing cooldown paths now work for every air/ground chain. Native CanDash still checks dashing, backdashing, preventDash, attack recovery, landing states and control restrictions. Decompiled HeroDash does not recheck dash cooldown; the shadow timer is read in its shadow-selection branch.

**Numbers:** activation remains 31 + 19 + Shade Cloak. Native-ready shadow is free and resets the paid sequence. Paid costs are 11, 22, then 33 indefinitely. Window is 0.3 seconds inclusive (existing 1e-9 tolerance), refreshed at successful shadow dash start and extended from its end. Standstill reset remains 1 second, horizontal speed <= 0.01 and not dashing. Scene/death/unload/lost activation resets remain. Unaffordable attempts are normal dashes, free of soul cost, with no price advance or window refresh. Sharp Shadow retains half damage, midpoint-to-even rounding, and no soul reward.

**Tests:** expanded capped-cost test to eight paid shadows separately on ground and in air, with dash-end extensions each time. Updated air-attempt tests to allow later attempts, landing test to preserve window and price through dash end, and fallback test to assert ordinary cooldown waiver without shadow waiver. Core decisions expose `WaiveDashCooldown` and `WaiveShadowCooldown`; paid decisions assert both. Existing query-without-commit, standstill, free restart, rejected-shadow and ExtendWindow boundary tests remain. Second build: 259 passing tests.

**Residual risks:** no live input/cooldown integration run. Other mods can reject or alter native dash behavior; timer restoration remains exception-safe. Native hard/dash landing animation states still restrict eligibility, as requested for other vanilla restrictions.

## 3. Tap-shot interval and real slash animation

**Wrong:** tap cooldown followed actual Quick Slash equipment and otherwise used the slower ordinary nail field; small shots did not play directional Knight slash animations.

**Changed interval:** Core defines `GripAttack.SmallShotIntervalFactor = 0.9` and validates/multiplies the supplied Quick Slash cooldown. Small-shot runtime always reads `self.ATTACK_COOLDOWN_TIME_CH`, independent of charm 32. Down-shot recoil uses this same interval. Held/charged spell cooldown retains its previous behavior in a separate helper; nail behavior is unchanged.

**Measured value and product:** existing live measurement in `.cache/shots/stage4/gap-driver.log:3` records `ATTACK_COOLDOWN_CH=0.25` (ordinary cooldown 0.41). Thus **0.9 × 0.25 s = 0.225 s**, about 4.444 shots/s. This is a prior instance measurement, not a new measurement from a game launched for this task. Runtime reads the instance every time, so 0.25 is not hardcoded.

**Changed animation:** after successful white Fireball spawn, horizontal direction faces the Knight appropriately and tk2d plays `Slash`; up/down play `UpSlash`/`DownSlash`, restarting from frame zero. A symmetrically registered HeroAnimationController.Update hook briefly defers locomotion animation selection while that clip plays, bounded by its actual frame-count/fps duration. It yields to native attack, spell, dash, damage, lost control, changed clip, expiry, or inactive Grip. Reset/unload clears ownership. It does not disable the controller or set `cState.attacking`, attack duration/cooldown, nail charge fields, or player movement state.

Reviewed decompiled HeroController.Attack: that path selects NailSlash components, spawns Grubberfly beams, calls StartSlash and signals the orbit shield. None is called here. HeroAnimationController uses the same three clip names for real nail swings; direct sprite playback does not invoke the damage-spawning attack path. No slash collider/FSM activation or suppression is needed; Attack-button nail hitboxes remain native. Existing small Fireball damage, size, soul, pogo and lever behavior remain unchanged.

**Tests:** added three interval cases (Quick Slash cooldown input 0.25, 0.30, 0.41), checking exact 0.9 multiplication, cooldown boundary and first-down-shot net descent with the resulting interval. Renamed the general cooldown test to avoid claiming all intervals are nail intervals. Third/final builds: 262 passing tests.

**Residual risks:** directional animation playback and interruption order cannot be fully exercised by the Core host. No in-game visual validation was performed. Existing actions or other mods may interrupt the cosmetic clip. Animation lifetime uses the loaded clip metadata; no guessed animation duration is introduced.

## Validation and scope

`bash scripts/build.sh`: **build succeeded, 0 warnings, 0 errors; 262 passed, 0 failed, 0 skipped**. TreatWarningsAsErrors remains enabled. `git diff --check` passed. The test runner emits its existing VSTEST_ARTIFACTS_DIRECTORY fallback notice, but tests complete successfully.

No game launched, no xvfb used, no Inspector edits, no Unity asset unpacking. Process inspection found no leftover hollow_knight; all Xvfb processes were left alone. No commit, push, rebase, reset or branch change. Pre-existing untracked REVIEW.md and TASK062.md were left untouched. No new charm/acquisition/overheat system or nail-input interception was added; 冷静施法 remains unimplemented.
