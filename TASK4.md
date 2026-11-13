你在一台 Debian 13 x86_64 Linux 机器上，继续为空洞骑士（Hollow Knight 1.5.78.11833）开发法术流护符 mod。仓库在 /workspace/hk-mod，分支 master，当前提交 59b5f61（第三阶段萨满大师与下砸无敌帧；过热已在 63b43ec 删除，不要再引入过热、HeatMeter、HeatFeedback、+15/+5/+25/+40 过热，或任何 heat 字段）。设计案见 DESIGN.md，以它为准，不要自行新增设计。先读 DESIGN.md、README.md 和现有源码，再动手。

环境已就绪，不要重装、不要改游戏版本：
- 游戏：/home/box/games/HollowKnight-1578（1.5.78.11833，API v77）。不要用 /home/box/games/HollowKnight。
- .NET：~/.dotnet，DOTNET_BIN=$HOME/.dotnet/dotnet。构建 scripts/build.sh，安装 scripts/install-mod.sh。
- 反编译和 FSM 在 /workspace/hk-mod/.cache/decompiled、.cache/fsms、.cache/allfsms。只读这些，不要解包 Unity 资源，不要长时间搜 AssetBundle / sprites / Texture2D，不要跑提取脚本。
- 不要启动 hollow_knight，不要用 xvfb，不要改 Inspector。编译和单元测试即可。若有残留 hollow_knight 必须杀掉。

本阶段只做开发顺序第 4 项里的「影魂连闪」。完成后停止。不要实现「冷静施法」（该护符原效果依赖已删除的过热，待用户重新设计）。不要开始其他未实现内容（改进版国王之魂、极速施法其余效果、正式护符获取）。

目标：影魂连闪（chain shadow dash）

DESIGN：「前置：暗影斗篷。暗影冲刺后 0.3 秒内再冲仍为暗影冲刺，依次耗 11、22、33 魂；落地或停下 1 秒后重置；空中只能连一次；魂不足时改为普通冲刺。同装锋利之影时，冲刺伤害减半且不回魂。」槽 2。过热已删除：只有魂消耗，没有 +15 过热。

行为细则（按 DESIGN，不要加新规则）：

1. 前置：玩家已有暗影斗篷（PlayerData.hasShadowDash）。没有暗影斗篷时，此开关即使打开也不改变冲刺。没有开启本开关时，完全保持原版冲刺（含原版暗影冲刺冷却 shadowDashTimer / SHADOW_DASH_COOLDOWN）。

2. 第一次暗影冲刺仍是原版免费暗影冲刺（shadowDashTimer <= 0 时的那一次），不扣魂。从这一次起进入连闪窗口。

3. 在上一次「实际发生的暗影冲刺」之后 0.3 秒内再冲：若魂够，则这次仍然是暗影冲刺（i-frames、特效、锋利之影路径与原版暗影冲刺相同），并按序列扣魂 11、22、33（第三次及以后若仍在窗口内且允许，耗 33）。魂消耗是 Spellcaster.Core 里的常量（例如 ShadowChain.Costs 或 Cost(index)），便于改数值。扣魂用 HeroController.TakeMP，只扣这一笔。

4. 实现提示（原版 HeroDash，见 .cache/decompiled/HeroController.cs）：
   - 原版只有 `hasShadowDash && shadowDashTimer <= 0` 时才把 cState.shadowDashing 设为 true，然后 shadowDashTimer = SHADOW_DASH_COOLDOWN。连闪要在窗口内、魂足够时，让这一次 HeroDash 走进这条暗影分支（例如在 orig 之前把 shadowDashTimer 临时清到 <= 0，orig 后再设回冷却）。不要改 SHADOW_DASH_COOLDOWN 常量本身，不要改坏原版免费暗影的冷却与充能闪光。
   - 原版 CanDash 在空中要求 `onGround || !airDashed || wallSliding`，默认只能空中冲一次。DESIGN「空中只能连一次」意思是：在空中的那次原版暗影之后，允许再连一次暗影（或魂不够时的普通冲刺），但不能再连第二次。需要在连闪窗口内对 CanDash（或等价处）放行这一次额外空中冲刺；窗口外或已用完空中连闪后，保持原版「空中只能冲一次」。
   - 钩 On.HeroController.HeroDash 和 CanDash（MMHOOK 已生成私有方法钩子，与现有 DoAttack 同类）。用反射读写私有 shadowDashTimer / airDashed，与 MageGripRuntime 同一风格。钩子对称注销。

5. 魂不够这次连闪费用：这次改为普通冲刺（不是暗影，不扣魂）。如果这次本来是靠连闪才放行的额外空中冲刺，仍然放行普通冲刺，并计为已用掉那次空中连闪。

6. 重置连闪序列（下次连闪又从 11 魂开始，空中连闪计数清零，窗口关闭）当：
   - 落地（onGround 由假变真，或等价的着陆）；或
   - 停下（水平速度接近 0 且不在冲刺）持续 1 秒；或
   - 场景切换、死亡、读档、新游戏、关闭本开关。
   原版免费暗影再次可用（shadowDashTimer 走完）并实际放出时，视为新的一轮起点（本次免费，下次连闪又是 11）。

7. 「同装锋利之影」（equippedCharm_16）时：本次暗影冲刺（含免费第一次和后续连闪）伤害减半（Round，公式放 Core，例如 SpellDamage 或 ShadowChain.SharpDamage(base)），并且不回魂。只在本开关打开且装备锋利之影时改变；不要改未开本开关时的原版锋利之影。原版 HealthManager 对 AttackTypes.SharpShadow 不调用 SoulGain；若你发现其他回魂路径，一并挡住。伤害减半不要改预制体上的永久数值，在命中时改本次 HitInstance.DamageDealt（或等价），避免对象池把减半伤害留给下次。

8. 模组菜单临时开关，默认关；槽位可配置，默认 2，只保存展示，不占实际护符槽。不要引入 SFCore / ItemChanger。获取位置本阶段不做。Settings 字段建议：ShadowChainEnabled、ShadowChainNotchCost。Initialize 日志包含该开关。关闭开关立即恢复原版冲刺并 Reset 连闪状态。

9. 把窗口（0.3）、重置静止（1.0）、魂序列（11、22、33）、空中最多连一次、魂不足回退为普通冲刺、锋利之影伤害减半取整，全部放进 Spellcaster.Core（新类型，不引用 Unity）。用 Numbers.RequireFiniteNonnegative 校验时间。补 xUnit 测试，原有 92 个必须继续通过。至少覆盖：
   - 0.3 秒窗口内可连、刚过 0.3 不可连（不同时间划分结果稳定）。
   - 费用依次 11、22、33；第 4 次仍为 33（或你按 DESIGN「依次」只允许三次，并在测试里写清楚）。
   - 魂不够 → 普通冲刺、不扣魂；够则暗影并给出费用。
   - 空中只能连一次；落地或静止 1 秒后重置，下次又从 11 开始。
   - 免费原版暗影不扣魂，只开启窗口。
   - Round 伤害减半（含奇数）。

10. TreatWarningsAsErrors。版本号 0.4.0（Directory.Build.props 与 GetVersion）。第一、二、三阶段行为不要回退。不要残留过热引用。钩子对称。场景/死亡/读档重置。

构建：scripts/build.sh -p:GameDir=/home/box/games/HollowKnight-1578，零警告零错误，全部测试通过，然后 scripts/install-mod.sh 安装。更新 README（已实现 / 菜单项 / 连闪规则 / 测试个数 / 已知限制；写明冷静施法仍未实现、待重新设计）。DESIGN.md 开发顺序可注明第 4 项本阶段只做影魂连闪、冷静施法不做；不要改其它规则正文的数值。不要 git commit，不要 git push，不要 rebase / reset。

最后用中文简要汇报：做了什么、连闪窗口与费用、空中限制、魂不足与重置、锋利之影如何减伤、测试个数、菜单项、检查过的风险点、已知问题。
