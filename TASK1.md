你在一台 Debian 13 x86_64 Linux 机器上，为空洞骑士（Hollow Knight）开发一个法术流护符 mod。设计案见本目录 DESIGN.md，以它为准，不要自行新增设计。

环境：
- 目标游戏版本是 1.5.78.11833（Unity 2020.2.2，Linux 版），在 /home/box/games/HollowKnight-1578，托管 dll 在 hollow_knight_Data/Managed。不要用 /home/box/games/HollowKnight（那是 1.5.12620 新版，不支持）。
- 机器只能走 HTTPS，普通 HTTP 会被拦截。没有 dotnet，需要你自行安装（例如用 dotnet-install.sh 装到 ~/.dotnet）。
- 需要安装与该游戏版本匹配的 Hollow Knight Modding API（hk-modding/api，从 GitHub Releases 获取）。用官方 Release 1.5.78.11833-77 的 Linux 包，校验 sha256 后装进游戏目录（Managed 下的 Assembly-CSharp 替换为 API 版，原版先备份）。

本阶段只做开发顺序第 1 项，完成后停止：
1. 在 /workspace/hk-mod 建 C# mod 工程（目标框架与 Modding API 一致），引用游戏和 API 的 dll，能编译出 mod dll。
2. 实现全局过热系统（0–100，加热和冷却按 DESIGN.md 的规则）和按施法实例（castId）结算的法术回魂公式，以及灵魂收集者护符（先用一个可装备的自定义护符或临时开关，素材尽量复用原版）。
3. 把过热、回魂这类纯数值逻辑放进不依赖 Unity 的类，另建 xUnit 测试项目覆盖：公式数值、第 4 段起减半、冷却时序、castId 去重。
4. 构建并跑通测试。把编译出的 mod 安装到游戏的 Mods 目录。
5. 初始化 git 仓库并提交（不要推送）。写 README.md，说明构建、安装和尚未实现的部分。

最后用中文简要汇报：做了什么、Modding API 版本、测试结果、已知问题。

注意代码质量，主动排查可能的 bug：空引用、场景切换和存档读档后状态没有重置、护符卸下后钩子没有注销、多段法术重复结算、过热值越界、帧率不同导致的计时误差。在汇报里列出你检查过哪些风险点。README 中那段关于版本阻塞的说明，改成当前真实状态。
