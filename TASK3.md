你在一台 Debian 13 x86_64 Linux 机器上，继续为空洞骑士（Hollow Knight 1.5.78.11833）开发法术流护符 mod。仓库在 /workspace/hk-mod，分支 master，当前提交 a217099（第二阶段法师之握，工作区可能还有未提交的小改动：SpellRuntime.GripEnabled 属性和 Initialize 时打印设置，请保留）。设计案见 DESIGN.md，以它为准，不要自行新增设计。先读 DESIGN.md、README.md 和现有源码，再动手。

环境已就绪，不要重装、不要改游戏版本：
- 游戏：/home/box/games/HollowKnight-1578（1.5.78.11833，API v77）。不要用 /home/box/games/HollowKnight。
- .NET：~/.dotnet，DOTNET_BIN=$HOME/.dotnet/dotnet。构建 scripts/build.sh，安装 scripts/install-mod.sh。
- 反编译和 FSM 在 /workspace/hk-mod/.cache/decompiled、.cache/fsms、.cache/allfsms。只读这些，不要解包 Unity 资源，不要长时间搜 AssetBundle / sprites / Texture2D，不要跑提取脚本。
- 不要启动 hollow_knight，不要用 xvfb，不要改 Inspector。编译和单元测试即可。若有残留 hollow_knight 必须杀掉。

本阶段只做开发顺序第 3 项，完成后停止，不要开始第 4 项（影魂连闪、冷静施法）。

目标：

1. 萨满大师挪到攻击键（与法师之握配合）
   - 法师之握已实现：按住攻击 0.45 秒松开放第一段蓄力（下砸 / 上吼 / 左右火球，耗 25 魂，过热 CastKind.NormalSpell +25）。
   - 萨满大师启用且法师之握也启用时：按住攻击到第二段阈值再松开，放同一方向的第二段蓄力。伤害 ×2，范围增大，耗 48 魂（装备原版法术扭曲者 equippedCharm_33 时 36）。过热记为 CastKind.ChargedSpell（+40）。
   - 第二段蓄力时间：默认 1.5 秒；装备萨满之石（equippedCharm_19）时缩至 1.1 秒；若极速施法也开启，蓄力时间再 ×1.5。极速施法本阶段不是正式护符，在 Core 里用布尔参数实现公式即可；模组菜单可加一个临时开关（默认关），不要做极速施法的其余效果（间隔减半、伤害 −60% 等，那是后续阶段）。
   - 松开时只放一段：按满第二段阈值则只放第二段（扣 48/36，+40 过热），不要先放第一段再放第二段。未按满第二段但已按满 0.45 秒则仍放第一段（25 魂，+25 过热）。魂不够第二段但够第一段时放第一段；两段都不够则退回免费小弹。
   - 伤害按 DESIGN 取整：Round(D × 极速倍率 × 蓄力倍率 × 萨满之石倍率)。第二段蓄力倍率 = 2；萨满之石倍率在设计里「满配约 ×3」，因此取 1.5（2 × 1.5 = 3）。极速倍率本阶段默认 1（没有极速施法效果）。把公式放进 Spellcaster.Core，不要在 Unity 代码里现场乘法。
   - 「范围增大」：对第二段施放的下砸 / 尖啸 / 火球做可见的范围放大（例如缩放约 1.4–1.5），只影响这一次蓄力施法，不要改施法键放出的原版法术，不要改小弹。对象池回收时必须还原。
   - 扣魂：与第一段同样，释放时扣 48 或 36，并抑制该次 Spell Control 状态里的原版 TakeMP，避免双扣。施法键路径完全不动，仍耗 33（扭曲者 24）。
   - 禁用骨钉技艺（法师之握已做，保持）。
   - 萨满大师本阶段用模组菜单临时开关（默认关），槽位数可配置，默认 3，只保存展示，不占实际护符槽。不要引入 SFCore / ItemChanger。获取位置本阶段不做。
   - 未开启法师之握时，不要让萨满大师单独改攻击键（没有法师之握就没有攻击键蓄力路径）。

2. 下砸无敌帧参数化（全局规则，不占槽）
   - 原版 HeroController.QuakeInvuln 把 parryInvulnTimer 设为 INVUL_TIME_QUAKE。把它变成可配置参数，写入全局 Settings，默认等于运行时读到的原版 INVUL_TIME_QUAKE（不要写死一个猜的数字作为默认，若读不到再合理回退）。
   - 对所有下砸生效（攻击键蓄力和施法键），因为这是全局规则。下砸破地板保持原版，不要改坏。
   - 纯数值（时长校验、非负、默认值）放 Core 并写测试。

3. 把蓄力阈值、魂消耗（含扭曲者）、伤害公式、时间修正（萨满之石 / 极速）、不足魂时的回退，全部放进 Spellcaster.Core（扩展现有 GripAttack 或新类型），不引用 Unity。补 xUnit 测试，原有 72 个必须继续通过。至少覆盖：
   - 默认 / 萨满之石 / 极速 / 两者同装的第二段阈值（1.5、1.1、2.25、1.65）。
   - 松开时机：不足 0.45、0.45–第二段、达到第二段。
   - 魂：48、扭曲者 36；不够第二段够第一段；两段都不够。
   - Round(D × 2 × 1.5) 以及无萨满之石时 Round(D × 2)；常见骨钉伤害。
   - 无敌帧时长非负、默认传递。

实现提示：
- 现有 MageGripRuntime.OnUpdate 在松开时用 GripAttack.Release 决定 SmallProjectile / NormalSpell。扩展这个判定即可，不要另搞一套输入。
- 第二段施法复用 TrySpell 的 Spell Control 状态（Fireball Antic / Quake Antic / Scream Antic），但伤害、范围、过热、魂消耗不同。标记这次施法是 ChargedSpell，在 Set Damage / 尖啸 / 下砸绑定处放大范围并改伤害，回收时还原（与 SpellProjectile 的 restore 列表同类）。
- 钩子对称注销。场景切换、死亡、读档、关闭开关时重置蓄力。TreatWarningsAsErrors。版本号 0.3.0。
- 第一阶段灵魂收集者、第二阶段法师之握行为不要回退。

构建：scripts/build.sh -p:GameDir=/home/box/games/HollowKnight-1578，零警告零错误，全部测试通过，然后 scripts/install-mod.sh 安装。更新 README（已实现 / 菜单项 / 蓄力规则 / 无敌帧设置 / 已知限制）。不要 git commit，不要 git push，不要 rebase / reset。不要改 DESIGN.md 规则正文。

最后用中文简要汇报：做了什么、蓄力时间与伤害公式、扣 48/36 魂的方式、无敌帧如何参数化、测试个数、菜单项、检查过的风险点、已知问题。
