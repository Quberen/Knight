# 空洞骑士法术流护符 Mod

设计以 [DESIGN.md](DESIGN.md) 为准。本阶段范围为开发顺序第 1 项：过热系统、按 castId 回魂、灵魂收集者。

## 当前状态：因 Modding API 版本不匹配停止

2026-11-13 核验本机游戏及 hk-modding/api 的公开 GitHub Releases，触发任务中的明确要求：“如果最新游戏版本没有对应的 API，停下来写清楚原因。”

| 项目 | 核验结果 |
| --- | --- |
| 游戏目录 | `/home/box/games/HollowKnight` |
| 本机游戏版本 | `1.5.12620` |
| Unity 版本 | `6000.0.61f1`（globalgamemanagers 中的字符串） |
| 最新公开 API | `1.5.78.11833-77` / ModdingAPI v77 |
| API 声明支持的游戏版本 | `1.5.78.11833` |
| 匹配本机版本的公开 Release | 未找到；查询返回的全部 22 个 Release 均无 `12620` |
| 已安装的 API / mod | 本次未安装 |

本机版本由 `Assembly-CSharp.dll` 的 UTF-16LE 字符串和 `globalgamemanagers` 的字符串交叉核对。不能将面向旧版游戏的补丁程序集当作新版 API 覆盖安装；当前没有满足要求的 Release，因此无法确定匹配的目标框架及程序集引用，也无法按要求构建和安装可用 mod。

来源：

- [官方 Releases](https://github.com/hk-modding/api/releases)
- [v77 发布说明及 Linux 安装包](https://github.com/hk-modding/api/releases/tag/1.5.78.11833-77)：说明明确写明适用于 Hollow Knight 1.5.78.11833。
- [完整发布列表查询](https://api.github.com/repos/hk-modding/api/releases?per_page=100&page=1)：本次返回 22 项，包含公开预发布（若存在）；未发现匹配版本。

## 复核方法

以下命令仅读取游戏文件：

```bash
strings -el /home/box/games/HollowKnight/hollow_knight_Data/Managed/Assembly-CSharp.dll | rg '1\.5\.12620'
strings /home/box/games/HollowKnight/hollow_knight_Data/globalgamemanagers | rg '1\.5\.12620|6000\.0\.61f1'
sha256sum /home/box/games/HollowKnight/hollow_knight_Data/Managed/Assembly-CSharp.dll
sha256sum /home/box/games/HollowKnight/hollow_knight_Data/globalgamemanagers
```

本次读取的文件 SHA-256：

```text
Assembly-CSharp.dll
5c84b8e59dd669c48db1fc426541d0d96643f59d35a72534b21fead7c96a3086

globalgamemanagers
b73a9901b5de07c2cbb526ec9b492c7cce1fb9daa0e83a9f6640c8f09acb9011
```

## 构建、测试与安装

当前没有 C# 工程、构建产物或可执行的构建/安装命令；未安装 .NET SDK，未运行 xUnit 或游戏实测。测试状态为“未执行”，不是通过。

本次未替换任何游戏 DLL，未创建 API 备份，也未向 Mods 目录写入文件。需要先获得官方 Releases 中匹配 `1.5.12620` 的 Linux API；或者由用户另行决定是否切换游戏版本。版本匹配后才能备份原版程序集、安装 API，并按实际 API 目标框架创建工程和配置引用。

## 尚未实现

- 第 1 项：全局过热、按 castId 的回魂与去重、灵魂收集者临时开关或自定义护符、相关原版素材反馈。
- 独立纯数值类及 xUnit 测试：公式数值、第 4 段起减半、冷却时序、castId 去重。
- mod 编译、安装及游戏内验证。
- DESIGN.md 开发顺序第 2–4 项均未开展，仍在本阶段范围之外。

仓库在任务开始时已有 `.git`，尚无提交。本次仅记录原有设计、任务说明及兼容性阻塞原因，不推送远端。
