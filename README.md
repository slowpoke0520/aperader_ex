# ApeRadar EX

ApeRadar EX 是《战舰世界》玩家战绩查看工具 ApeRadar 的社区增强分支。程序读取游戏生成的战斗信息文件，并通过公开 API 查询玩家数据；不会读取、修改或注入游戏进程。

> 本项目基于 zylalx1 的 ApeRadar 继续开发，EX 版本并非原项目官方发布版。

版本变化请查看 [更新记录](./CHANGELOG.md)。

## 下载

前往 [GitHub Releases](https://github.com/slowpoke0520/aperader_ex/releases/latest) 下载 `ApeRadar-win-x64.zip`，解压后运行 `ApeRadar.exe`。

运行环境：Windows x64 和 [.NET 6 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/6.0)。

首次运行后，打开“设置”，选择《战舰世界》安装目录。进入战斗时，程序会自动读取对局并显示双方玩家数据。

## 主要功能

- 显示账号和当前舰船的场次、胜率、经验、伤害等数据
- 使用 WoWS Numbers 公式计算账号 PR 和单船 PR
- 按标准 8 档 PR 色阶显示评分颜色
- 支持玩家关注状态、备注和再次遇到提醒
- 标记最近 5 局内遇到过的玩家
- 支持固定队友，避免重复遇到标记干扰
- 缓存玩家数据，并允许单独强制刷新某位玩家
- 支持多服务器和跨服对局
- 支持中英文界面及可配置的列表、图表和输出文本

## 三种更新的区别

设置页面提供三个互相独立的更新按钮：

- **检查软件更新**：从本仓库的 GitHub Releases 下载新版程序，校验 SHA-256 后由独立更新进程安装并重新启动。
- **更新船名列表**：更新 `Resources/Json/ships.json`，用于识别新版本加入或修改的舰船。
- **更新 PR 数据**：更新 `Resources/Json/expected_values.json`，用于计算 PR。

软件更新会保留 `WatchList.json`、玩家缓存、遭遇历史、窗口位置、日志和截图。

## 数据文件

| 文件 | 用途 |
| --- | --- |
| `WatchList.json` | 关注状态和玩家备注 |
| `PlayerDataCache.json` | 玩家战绩缓存 |
| `PlayerIDCache.json` | 玩家名称与 ID 缓存 |
| `EncounterHistory.json` | 最近对局和固定队友记录 |
| `Resources/Json/ships.json` | 舰船名称与基础信息 |
| `Resources/Json/expected_values.json` | PR 期望值数据 |

建议升级或迁移程序前备份 `WatchList.json`。

## 从源码构建

需要安装 .NET 6 SDK：

```powershell
git clone https://github.com/slowpoke0520/aperader_ex.git
cd aperader_ex
dotnet restore .\ApeRadar_EX\ApeRadar_Src\ApeRadar.sln
dotnet build .\ApeRadar_EX\ApeRadar_Src\ApeRadar.sln -c Release
```

Release 构建产物位于：

```text
ApeRadar_EX/ApeRadar_Src/ApeRadar/bin/Release/net6.0-windows/
```

## 发布 EX 版本

版本采用 `原版本-ex.增强版序号` 格式，例如 `2.1.1-ex.3`。生成发布包：

```powershell
.\Publish-Release.ps1 -Version 2.1.1-ex.4
```

提交后创建并推送相同版本的标签：

```powershell
git tag v2.1.1-ex.4
git push origin master
git push origin v2.1.1-ex.4
```

GitHub Actions 会自动构建并创建 Release。更完整的流程参见 [发布更新说明](./发布更新说明.md)。

## 数据来源与声明

- 玩家数据来自 Wargaming 公开 API 或游戏公开数据接口。
- PR 使用 [WoWS Numbers Personal Rating](https://wows-numbers.com/personal/rating) 公式。
- 舰船和期望值数据可能来自第三方公开数据源，其更新时间不完全由本项目控制。
- 在游戏公共频道发送违反游戏 EULA 或社区规则的内容，可能导致聊天或游戏处罚；使用者应自行承担相关责任。
- 本项目与 Wargaming、World of Warships 及 WoWS Numbers 没有隶属或官方合作关系。

## 致谢与许可证

感谢 ApeRadar 原作者 **zylalx1** 及相关开源数据项目。本项目保留原项目版权声明，并依据 [MIT License](./ApeRadar_EX/ApeRadar_Src/ApeRadar/LICENSE.txt) 发布。
