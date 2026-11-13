# Spellcaster 0.6.1 review and repair

Reviewed on `master`, starting at `dff8e47`, against DESIGN.md and the locked 0.6.1 instructions. Read every source, project, test, and test-data file under `src/` and `tests/`; reviewed modules in the requested order. Existing untracked `REVIEW.md` was left untouched. No commits, branch changes, game launches, Inspector edits, asset extraction, or Xvfb operations were performed. No leftover `hollow_knight` process was found.

## 1. Numbers, SoulLedger, SpellRules, CharmNotches

- Reviewed finite-number validation, fractional soul conservation, cast/segment deduplication, cast retirement, damage rounding, quake duration selection, and notch clamping against MechanicsTests, StageThreeTests, and CharmNotchesTests.
- Removed the unused `CastKind.ChainDash` member. Sharp Shadow has no spell-refund path and no caller created this cast kind; existing enum values remain unchanged.
- Simplified notch normalization to compute the requested total once. Earned-count handling and the 16-notch cap are unchanged.
- Added two MechanicsTests cases: rejected accumulator overflow preserves its fractional balance; changing soul charms cannot repay an already rewarded segment.
- Left Numbers and SpellRules unchanged: existing tests cover invalid inputs, final rounding, native quake duration, and fallback behavior. No heat state or heat arithmetic was present.

## 2. MageGrip Core and charge tests

- Existing release tests explicitly require an affordable first-tier fallback when Shaman's second tier is unaffordable. Preserved that behavior, along with no small-shot fallback, fixed 0.45s first charge, and the second-tier-only modifiers.
- Extracted the runtime drop predicate into `GripAttack.MustDropCharge` and introduced `GripPendingCast` to retain a release's direction/tier and its one-second retry deadline. Runtime uses both directly.
- Added 13 MageGripTests cases: each of six drop conditions and the playable case; pending direction/tier retention and inclusive deadline; cancellation/replacement; four modifier combinations showing fixed first-tier timing and indefinitely stored second-tier release.
- Preserved recoil, cooldown, white-projectile selection, soul amounts, round-to-even damage, and all existing StageThreeTests. Damage cancellation remains an actual-damage event, not a generic damage attempt; dash is not a drop condition.

## 3. ShadowChain Core

- Reviewed decision/commit separation, 11/22/33 costs, inclusive window, air usage, landing, standstill reset, and rejected dashes.
- Added the two missing activation truth-table cases; all eight combinations are now covered.
- Left activation and mechanics unchanged. Numeric charm IDs remain **31 + 19 + Shade Cloak**. DESIGN.md and the cached game implementation use charm 31 (Dashmaster); the request also calls it Sprintmaster. Preserved the established numeric rule rather than changing it to another charm.

## 4. SpellCharms and language tests

- Corrected the English Grip description: the small shot restores half nail soul; Grip does not halve vanilla nail soul gain. The old wording implied that it did.
- Existing language tests already cover live CurrentLanguage overriding stale saved language, regional Chinese codes, fallback, single-language strings, and vanilla Chinese glyph availability. Kept these rules unchanged.
- Extracted fifth-row lift geometry into `CharmBoardLayout`, with the same 0.55 lift and 0.55 minimum gap. Added five geometry cases for full lift, limited lift, minimum/insufficient clearance, and translated coordinates.
- Preserved IDs, costs, parsing, selected default icons, and default ownership semantics.

## 5. MageGripRuntime

Issues repaired:

- A pending release previously re-read direction on each retry, allowing a later input to redirect the released spell. It now retains the release direction.
- TrySpell did not independently reject an unlocked-level value of zero. It now rechecks the chosen spell's unlock before payment or FSM entry.
- A new hold could leave an old pending cast available for replay after the new release. Starting an accepted hold clears the old pending cast.
- HideVisuals protected vanilla nail charging, but winding/ready transitions could still stop its glow/audio. All such teardown paths now share the vanilla-ownership check.

Preserved QC-only swallowing, post-orig visuals, the actual OnTakenDamage subscription, Carefree zero-damage behavior, dash retention, Inactive-FSM gating, prepaid debit suppression, and the one-second retry period. No nail timer/state writes or attack/nail-art interception hooks were added. Install/uninstall hooks and damage subscriptions remain paired. Core tests cover the extracted state decisions; Unity input, audio, and FSM execution were reviewed statically and compiled, not run.

## 6. SpellRuntime, CastTracking, SpellProjectile, ProjectileInteractions, ChargedSpellEffect

- **SpellRuntime:** DESIGN.md defines spell-hit refunds globally, but damage hooks and Reward gated ordinary spells on Collector. Installed damage hooks now cover the runtime lifetime; ordinary spell refunds no longer require Collector. Collector still separately halves nail soul. Small-shot rewards still require Grip. Uninstall explicitly removes the global damage hooks.
- **SpellProjectile:** Launch rotates before Set Damage establishes a scale snapshot. An early recycled activation could leave that rotation behind. Restore now restores small-shot rotation even if scaling never occurred.
- **CastTracking:** reviewed shared source groups and target hit ordinals; left unchanged. Existing ledger tests cover cast/segment deduplication, but native collision-to-segment timing is not exercised by Core tests.
- **ProjectileInteractions:** reviewed temporary Nail Attack retagging, receiver exclusions, finally-based restoration, and paired hooks. Left unchanged; no broad HIT broadcast or permanent tag changes introduced.
- **ChargedSpellEffect:** reviewed native damage-setter substitution, one final multiplier calculation, 1.45 range, per-activation restoration, and root-only enlargement. Left unchanged to avoid changing native FSM/scale ordering without runtime evidence.
- Added ledger tests described in module 1. Existing tests cover additive +3/+8 refunds, later-hit amounts, half-nail soul with charms/reserve, damage multipliers, and quake duration. Global hook wiring and pooled-object restoration are compile/static checks, not simulated Unity integration tests.

## 7. ShadowChainRuntime

- Found that FinishedDashing extended the chain window after any dash, including an unaffordable normal fallback. Capture whether the completed dash was shadow before vanilla CancelDash clears it, and extend only for a shadow dash. This matches the existing Core test that normal fallback must not refresh the window.
- Checked cached HeroController.FinishedDashing to verify cancellation order.
- Preserved numeric activation, cooldown borrowing/restoration, air-dash restrictions, successful-dash-only payment, Sharp Shadow halving, and no soul gain. Hooks remain symmetrically removed.
- No new Unity test harness was introduced; activation tests were completed in module 3.

## 8. CharmRuntime, CharmBoard, CharmIcons, CharmNotchRuntime

- **CharmRuntime:** removed unused Grant/Has/GiveAll/TakeAll helpers and their now-unused private Unequip helper. These were leftover alternate acquisition/removal paths; EnsureOwned remains the default-ownership path. Kept the harmless debug-menu setting and ordinary equip/unequip hooks. Reviewed save stripping/restoration, notch accounting, language hook, DoSwitch refresh, and paired hooks.
- **CharmBoard:** uses the tested lift helper without changing fifth-row-only placement, existing board compression, or the small-lift cutoff. Kept native navigation edits and tray construction.
- **CharmIcons:** removed unused Ready and SaveComparison/export helpers; corrected the stale UV-selection comment. ReadableCopy now destroys full/cropped temporary textures in finally, including failure paths, while transferring ownership of successful output. Default variants remain Grip C, Shaman A, Collector A.
- **CharmNotchRuntime:** unchanged; only GetPlayerIntHook presents the expanded total. No write to PlayerData.charmSlots.
- Added five layout tests in module 4; existing costs, defaults, language, glyph, and notch tests remain passing.

Remaining lifecycle risks left explicit: CharmBoard.Uninstall removes extra notch objects but does not reverse all cloned board/tray objects, sprite-list extensions, navigation edits, or vanilla transforms. Its readiness booleans are not tied to a particular HUD instance. Re-enable or HUD replacement can therefore retain/reapply edits. A complete reversible UI rewrite needs live inventory validation, which this task prohibits; retained the known 0.6.1 layout rather than attempting an unverified broad rewrite. Partial icon-build retries can also retain previously allocated persistent variants; the temporary-texture failure leak was repaired, but persistent icon ownership was not redesigned.

## 9. SpellcasterMod, settings, and projects

- Normalized null nested per-save charm records during OnLoadLocal so malformed/partial serialized settings cannot immediately crash ownership/effect synchronization.
- Corrected the legacy CharmIconVariant comment: fixed user-picked defaults control current icons; retained the serialized field for compatibility.
- Preserved the no-op debug ownership menu, configurable notch costs, notch total, timing-only Fast Casting toggle, and quake setting. No Kingsoul/Fast Casting expansion or 冷静施法 implementation was added.
- Both Directory.Build.props and GetVersion remain **0.6.1**. Reviewed Core's net472/net8.0 targets, Mod's v77/game references, and the xUnit project/glyph content configuration; no project changes needed.

## 10. Final coverage and validation

- All original 235 cases remain passing; added **22 cases**, for **257 passed, 0 failed, 0 skipped**.
- README test count updated to 257. Test glyph data unchanged.
- Final command: `DOTNET_BIN=$HOME/.dotnet/dotnet bash scripts/build.sh`.
- Release build: **0 warnings, 0 errors**; Core builds for net472 and net8.0, Mod for net472.
- `git diff --check` passes.
- The test runner emits an informational VSTEST_ARTIFACTS_DIRECTORY fallback message after success; it is not a compiler warning or test failure. Package versions were left unchanged.
- No game execution was used. Runtime fixes are compiled against the installed game/API and supported by Core tests where extracted logic is testable; visual/FSM/physics behavior and the remaining UI lifecycle issues above still need in-game validation outside this task.
