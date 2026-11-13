你在一台 Debian 13 x86_64 Linux 机器上，继续为空洞骑士（Hollow Knight 1.5.78.11833）开发法术流护符 mod。仓库已在 /workspace/hk-mod，当前分支 master，最新提交 451f3bf（第一阶段已完成）。设计案见 DESIGN.md，以它为准，不要自行新增设计。先读 DESIGN.md、README.md 和现有源码，再动手。

环境（已经就绪，不要重装、不要改游戏版本）：
- 游戏：/home/box/games/HollowKnight-1578（1.5.78.11833，Modding API v77 已安装）。不要用 /home/box/games/HollowKnight。
- .NET SDK：~/.dotnet（global.json 锁定 8.0.428）。构建：scripts/build.sh；安装：scripts/install-mod.sh。DOTNET_BIN=$HOME/.dotnet/dotnet。
- 反编译源码和 FSM 已在 /workspace/hk-mod/.cache/decompiled 和 .cache/fsms、.cache/allfsms。优先读这些，不要再解包 Unity 资源、不要跑资源提取脚本、不要长时间搜索 AssetBundle / resources.assets / sprites / Texture2D。
- 不要启动 hollow_knight、不要用 xvfb 开游戏、不要给游戏加 Inspector。编译和单元测试即可。结束后若有残留 hollow_knight 进程必须杀掉。

本阶段只做开发顺序第 2 项（法师之握），完成后停止，不要开始第 3、4 项（萨满大师挪到攻击键、下砸无敌帧、影魂连闪等）。

目标：实现核心护符「法师之握」（Mage's Grip）。装备后：

1. 轻点攻击键：向当前输入方向发射小复仇之魂（左 / 右 / 上 / 下）。使用原版复仇之魂 / 暗影之魂（Fireball / Fireball2）素材和对象池，按玩家已解锁的等级选择；把它缩小并令伤害约为当前骨钉的 50%（按 DESIGN 的取整规则 Round）。不耗魂。攻击间隔沿用骨钉（含快劈护符的 ATTACK_COOLDOWN_TIME_CH）。回魂为骨钉回魂的一半（含捕魂者 / 噬魂者加成，不受过热影响）。这是替换骨钉的攻击，不要再走「11 × (1 − 过热/100)」的法术回魂公式。若同时开着灵魂收集者，不要把这笔回魂再减半一次（收集者的「骨钉回魂减半」只作用于真正的骨钉命中；法师之握已经规定为骨钉的一半）。过热记为 CastKind.SmallProjectile（+5）。
2. 向下发射：无需命中即获得小幅上弹，升力必须小于原版下劈 pogo（用运行时的 HeroController.BOUNCE_VELOCITY 作参照，不要在 Core 里写死原版数字）。不能悬停：连续向下发射只能缓慢下降。命中敌人、尖刺或可弹跳物体时触发完整 pogo（原版 BOUNCE_VELOCITY），并重置空中连射计数。向上发射使角色略微下移（在地面时不要把角色打进地板）。
3. 按住攻击 0.45 秒后松开：按方向释放原版下砸 / 上吼 / 左右火球（已解锁的升级版：暗影之魂、深渊之嚎、下行暗虚），耗 25 魂，过热记为 CastKind.NormalSpell（+25）。魂不足时不要放这个蓄力法术、不要扣魂；改为一次轻点小火球（+5 过热、不耗魂），行为要合理，可播放原版魂不足音效。禁用骨钉技艺（不要让原版蓄力大招放出）。施法键完全沿用原版：瞬发耗 33 魂，长按回血，不要改。
4. 法术弹要能触发原本只有骨钉才能打到的拉杆、可破坏墙和同类机关，避免软锁。不要改到无关物体。下砸本来就能破地板，保持即可。
5. 槽位数：在全局设置里可配置，默认 1（玩家还没决定 1 还是 2）。本阶段优先做成模组菜单开关（与灵魂收集者同类），把槽位数字显示并写入 Settings，文档说明这还不是正式护符。不要引入 SFCore、ItemChanger 或其他新依赖。不要为了正式护符槽去改库存 UI。获取位置（先祖之丘、蜗牛萨满）本阶段不做。
6. 纯逻辑（下弹弹跳 / 连续下落减速的数学、蓄力时间 0.45、冷却间隔是否允许下一次射击、是否耗魂等）放进 Spellcaster.Core，不引用 Unity。补 xUnit 测试，至少覆盖：
   - 首次下弹的向上速度 < 传入的钉 pogo 速度，且为正（或在下落中给出上弹）。
   - 在典型重力与攻击间隔下，连续下弹不能维持或增加高度（不能悬停），净位移向下；重复下弹只减缓下落，不得把速度重新推到可悬停的正速度。
   - 完整 pogo（命中）恢复到钉 pogo 速度并重置连射计数。
   - 落地重置。
   - 上弹给一个小于钉 pogo 的向下速度增量；已在地面（vy≈0 且视为落地）不向下推。
   - 不同帧率划分下结果稳定（与 HeatMeter 测试同类）。
   - 0.45 秒蓄力阈值、魂不足时不放蓄力法术。
   第一阶段已有的 35 个测试必须继续全部通过。

实现提示（已核实，直接用，不要再翻资源包）：
- 钩住 On.HeroController.Attack / DoAttack 或等价输入路径，装备护符时取消 NailSlash.StartSlash，改发射法术弹，但仍要走骨钉的 attacking / attack_cooldown / attackDuration（快劈时用 _CH 值）。不要把攻击完全吞掉导致冷却错乱。
- 原版火球由「Fireball Top」的 Fireball Cast FSM 从对象池生成，用 SetVelocityAsAngle 设方向。左右用 0/180，上用 90，下用 270。生成后可改 Rigidbody2D 速度、旋转和缩放。阶段 1 的 SpellRuntime 已经在 Fireball Cast「L or R」、Fireball Control「Set Damage」绑定 castId，新弹必须沿用同一套绑定，对象池复用时覆盖旧 cast，不能串号。
- 小火球伤害：在 Set Damage 之后改 damages_enemy 的伤害值，或生成后写入对应 FSM 变量。不要误改玩家用施法键放出的原版火球。给小火球打一个标记组件以区分。
- 蓄力法术：松开攻击且按满 0.45 秒、魂 ≥ 25 时，扣 25 魂并触发 Spell Control 里已有的 Fireball / Quake Antic / Scream 状态（按方向和已解锁等级）。注意原版这些状态会按 MP Cost 再扣 33 魂——必须避免双扣，只扣 25。不要改施法键那条路径的消耗。
- 拉杆 / 破墙：对法术弹的碰撞补发原版钉子常用的 HIT / NAIL HIT 一类事件，或把法术伤害类型接到只响应骨钉的 Breakable / 机关上。先读 .cache 里已有的 FSM 和反编译，不要解包。
- 下弹弹跳在物理帧应用到 rb2d.velocity.y。注意与原版 CancelBounce、落地、受击、冲刺、爬墙的互相取消。场景切换、死亡、读档、卸下护符时重置连射计数和蓄力计时。
- 钩子在 Unload、开关关闭、OnDestroy 时必须对称注销。空引用、对象池、场景切换都要处理。保持 TreatWarningsAsErrors。版本号改为 0.2.0。

构建：用 scripts/build.sh -p:GameDir=/home/box/games/HollowKnight-1578，零警告零错误，全部测试通过，然后 scripts/install-mod.sh 装进游戏 Mods。更新 README.md（已实现 / 未实现、菜单开关、槽位设置、弹跳规则、已知限制）。不要 git commit，不要 git push，不要 git rebase / reset。不要改 DESIGN.md 的规则正文。

最后用中文简要汇报：做了什么、关键实现选择（如何生成定向火球、如何扣 25 魂、弹跳公式）、测试结果（测试个数）、菜单/设置项、你检查过的风险点、已知问题。
