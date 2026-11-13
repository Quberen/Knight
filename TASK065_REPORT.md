# TASK 0.6.5 report

Implemented on master from da635db. Version is 0.6.5 in Directory.Build.props and GetVersion. No commit or push was made.

## Restored constants

| Quantity | 0.6.5 |
| --- | --- |
| First store / ChargeSeconds | 0.45 s accumulated gameplay time |
| GlowSeconds | 0.2 s accumulated gameplay time |
| TapSeconds | 0.2 s; tap strictly below the wall-clock threshold |
| Second store without / with Shaman Stone | 1.5 / 1.1 s total accumulated time |
| Second store with Fast Casting, without / with Stone | 2.25 / 1.65 s total accumulated time |

Second threshold formula: `(shamanStone ? 1.1 : 1.5) * (fastCasting ? 1.5 : 1)`. First store remains independent of Stone, Fast Casting, and Nailmaster's Glory. GlowSeconds remains separate from TapSeconds; Winding still uses GlowSeconds. No invented gap was added.

## Preserved from 0.6.4

- Optional soul / spellTwister parameters and default int.MaxValue; second flash requires time plus 48 soul, or 36 with Twister. First-tier timing feedback is independent of soul.
- Insufficient soul at the second threshold leaves tier 1 and stops winding. Soul gain enables tier 2; soul loss returns tier 1. Existing affordability tests are retained, with explicit gain/loss assertions added to the existing theory.
- Runtime MPCharge/Twister wiring and RuntimePolicyTests are unchanged.
- Release fallback remains NormalSpell at 25 or more soul, otherwise None. Queued ChargedSpell keeps its kind and rechecks cost in TrySpell.
- ZH/EN descriptions retain vanilla unlocked first-store spell (including Shade Soul), vanilla size/damage and 25 soul, versus second-store big spell with ×2 damage, ×1.45 range and 48/36 soul.
- Fire gating, Collector refunds, Shadow Chain, small-shot interval, slash animations, ChargedSpellEffect, prepaidKind, pending-cast policy, and Release implementation are unchanged. 冷静施法 was not implemented.

## Validation

`env -u XDG_CONFIG_HOME HOME=/home/box bash scripts/build.sh`: build succeeded with 0 warnings and 0 errors; all 325 xUnit tests passed, 0 failed, 0 skipped. Coverage was not dropped. Updated boundaries, pause/resume/reset partitions, frame partitions, modifier tables, and description assertions pass, including glyph/length checks and runtime policy checks.

`git diff --check`: passed. Audited 2.25/1.65 literals in current source, tests and docs: they designate Fast Casting second thresholds. README's 0.6.4 entry remains unchanged as historical fact; 0.6.0/0.6.1 examples now correctly use 0.45, and the 0.6.3 history remains intact.

## Files touched

- DESIGN.md
- Directory.Build.props
- README.md
- src/Spellcaster.Core/MageGrip.cs
- src/Spellcaster.Core/SpellCharms.cs
- src/Spellcaster.Mod/SpellcasterMod.cs
- tests/Spellcaster.Core.Tests/GripChargeTests.cs
- tests/Spellcaster.Core.Tests/GripFireGateTests.cs
- tests/Spellcaster.Core.Tests/MageGripTests.cs
- tests/Spellcaster.Core.Tests/SpellCharmsTests.cs
- tests/Spellcaster.Core.Tests/StageThreeTests.cs
- TASK065_REPORT.md

## Residual risk / handoff

Live gameplay and visual timing remain for executor verification. The game, Inspector and Xvfb were not started or accessed, and nothing under .cache/ was touched. Pre-existing untracked REVIEW.md and TASK062.md through TASK065.md were left alone. Changes and this report are uncommitted for executor review.

## Live-verify (executor, 13 Nov 2026 23:29 Asia/Shanghai)

Inspector 065-live, xvfb 960×540, fireballLevel=2, charms 41+42, MP 99. Per-frame sampling. Installed DLL reports ChargeSeconds=0.45, GlowSeconds=0.2, Second=1.5 / 1.1 / 2.25 / 1.65.

| Scenario | Expected | Actual | OK |
| --- | --- | --- | --- |
| Glow start | ~0.200 s | 0.208 s | yes |
| First store | ~0.450 s (not 0.675) | 0.455 s | yes |
| Second store | ~1.500 s (not 2.25) | 1.518 s (glow-off after first store; winding stops at tier 2) | yes |
| T2 release (sanity) | ChargedSpell, −48 MP, scale ~2.61, dmg 60 | MP 99→51, Spiral 2.61, cse=True, dmg=60 | yes |

Soul-gated second flash and charged release are unchanged. Installed SHA-256: Spellcaster.Mod `8b03c98bdfcbd7e0cf1789c0a9304fd8cf4f6424c9dc8e73f8746ff1ad92c8d4`, Spellcaster.Core `e296819069be2bc8f38ac4b0688076939b46f31098f8470df79d5748303790af`. Contact: `.cache/shots/charm-065-contact.png` (not committed). Inspector.dll restored to stage1 (7680).
