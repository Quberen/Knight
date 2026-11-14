# TASK 070 report — Improved King's Soul (0.7.0)

## Authorship

Codex (`gpt-6-astra`) was asked to write all code. Both launches failed before producing any patch:

1. `/tmp/codex-070.log` — `ERROR: workspace routing discovery failed` (exit 1, ~13s).
2. `/tmp/codex-070b.log` — same error after retry with a normal `HOME` / `CODEX_HOME` (exit 1, ~13s).

Neither run wrote source, tests, or docs. The executor then implemented the feature, built it, live-verified it, and is committing it. **All 0.7.0 code in this commit was written by the executor, not Codex.**

## Design ambiguity

DESIGN.md lists 改进版国王之魂 as its own charm row (cost 5, “前置：原版国王之魂”) rather than a patch of charm 36. Consistent reading:

- Separate charm **ID 44**, default-owned like 41–43, not auto-equipped.
- Prerequisite is ownership of vanilla King's Soul: `royalCharmState == 3` (assembled Kingsoul). Fragments (`< 3`) and Void Heart (`4`) do not qualify, matching the vanilla White Charm FSM.
- Equip is rejected without the prerequisite; if the state later becomes Void Heart, 44 is auto-unequipped.
- It does **not** replace charm 36 in `PlayerData`. When 44 is equipped, vanilla Kingsoul is unequipped if it was on, and the White Charm FSM's `Soul UP` / `AddMPCharge(4)` path is suppressed so regen cannot stack.

Formal acquisition (“后期”) is skipped for now, same policy as the first three charms.

## Numbers

| Band (current `MPCharge`) | Rate |
|---|---|
| 0–23 (and negative, treated as this band) | 6 /s |
| 24–65 | 3 /s |
| 66+ | 2 /s |

Core ticker (`KingSoulRegen`) splits a tick at band boundaries, keeps a fractional remainder, and caps grants at remaining orb + reserve room. Runtime grants through `HeroController.AddMPCharge(1)` so the soul orb animates. Gameplay time only; skipped when paused, not in a gameplay scene, dead, or `disablePause`.

Icon: recolor of raw `spriteList[36]` (Kingsoul, not Void Heart). Default variant **A** (pale royal ivory). B rose gold, C moonlight.

## Live verify (Asia/Shanghai)

Game: `/home/box/games/HollowKnight-1578` under existing Xvfb `:1`, `-force-glcore`, `XDG_CONFIG_HOME=/workspace/hk-mod/.cache/test-home`.

- Without Kingsoul, `EquipCharm(44)` left `equippedCharm_44=False`.
- After `royalCharmState=3`, equip succeeded: `eq44=True`, `eq36=False`, `filled=5`, `cost44=5`.

Measured rates (driver logs in `.cache/shots/070/`):

| Band | Start | Window | Delta | Rate | Expected |
|---|---|---|---|---|---|
| 0–23 | 0 (before=1) | 2.001 s | +12 | **5.996 /s** | 6 |
| 24–65 | 40 (before=41) | 2.005 s | +6 | **2.993 /s** | 3 |
| 66+ (clean run 1, no leftover remainder) | 80 (before=81) | 2.003 s | +4 | **1.997 /s** | 2 |

A later grounded run of the high band read 2.473 /s because remainder from the previous set carried across a `SetMPCharge` jump; the official high-band figure is the clean run-1 window. HUD screenshots show an almost empty orb, a half-full orb with gain particles, and a nearly full orb. The inventory opened after equip (item pane; the Charms pane event was sent too early on that run). Contact sheet: `.cache/shots/charm-070-contact.png`.

## Tests and build

`bash scripts/build.sh -p:GameDir=/home/box/games/HollowKnight-1578`: **0 warnings, 0 errors**. **359** xUnit tests passed, 0 failed, 0 skipped (was 325). `git diff --check` clean. Mod installed to the 1.5.78.11833 Mods folder.

## Files

New: `src/Spellcaster.Core/ImprovedKingSoul.cs`, `src/Spellcaster.Mod/KingSoulRuntime.cs`, `tests/Spellcaster.Core.Tests/ImprovedKingSoulTests.cs`.

Updated: `SpellCharms.cs`, `CharmRuntime.cs`, `CharmIcons.cs`, `CharmBoard.cs`, `SpellRuntime.cs`, `SpellcasterMod.cs`, `SpellCharmsTests.cs`, `RuntimePolicyTests.cs`, `Directory.Build.props`, `DESIGN.md`, `README.md`.

## Residual risk

- The Charms board itself was not captured (inventory opened to the item pane). Equip was confirmed in PlayerData and notch fill.
- Remainder is not reset when something else sets `MPCharge` in one jump; normal regen is fine.
- Vanilla White Charm suppression depends on the FSM name `White Charm` / state `Soul UP`. Uninstall is symmetric.
