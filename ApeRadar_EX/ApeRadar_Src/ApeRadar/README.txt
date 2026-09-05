免责声明

本软件使用Wargaming公开的API接口进行数据查询，不以任何形式与游戏软件本身进行交互，因此不属于禁用软件/模组。但是
在游戏内公共频道发送违反EULA和/或Game and Clan Rules for World of Warships的信息可能会导致您的账号遭到禁止聊天或禁止游戏的处罚。
作者不对以上情况承担任何责任。


使用说明

1. 解压后运行 ApeRadar.exe。本发布包为 Windows x64 自包含版本，不需要安装 .NET。

2. 单击右下角的设置按钮，设置《战舰世界》游戏路径。

3. 进入对局后会自动显示双方战绩。随机战会额外记录本机舰船、开场阵容和对局结果。


网站：https://lxdev.org/aperadar/
QQ群：1012624096（1群） 272868560（2群）


新功能说明

1.玩家备注：在玩家列表右键点击玩家，选择"编辑备注"即可为该玩家添加备注。
   之后每次遇到该玩家都会在右下角消息栏显示备注提醒，玩家名称旁也会显示📝图标。
   备注与特别关注名单一起保存在WatchList.json中，可在设置 -> 特别关注名单中查看和管理。

2.PR（个人评分）：玩家列表数据列新增PR列，基于玩家全部舰船战绩，使用WoWS Numbers
   公开公式计算（https://wows-numbers.com/personal/rating）。PR计算所需的每船期望值
   数据存放在Resources\Json\expected_values.json中，可在设置 -> 通用 -> 更新PR数据
   中手动更新。

3.手动更新软件：单击主界面右下角的“软件更新”按钮，可立即检查并安装新版本。

4.更新单个玩家：右键点击玩家并选择“更新此玩家数据”，可忽略该玩家的缓存并重新
   获取最新战绩，其他玩家仍使用缓存。

5.近期遇到标记：若一名玩家在此前5局中出现过，其名称后会显示橙色🔁和遇到次数。
   固定队友可通过玩家右键菜单进行勾选；勾选后不会显示近期遇到标记。记录保存在
   EncounterHistory.json中。

6. 对局历史与单船追踪：主窗口底部点击“对局历史”。程序会自动扫描游戏 replays
   目录中的既有录像，并在以后每场随机战结束后导入 Replay。顶部可按服务器、账号、
   舰船和日期筛选；选择一艘船后，汇总和曲线只统计这艘船。可查看 10、20、50 场或
   全部场次的滚动胜率、伤害、击沉和 PR。胜利为绿色、失败为红色；PR 使用与主界面
   一致的分级颜色。录像不完整时，程序会以单船累计数据差值补查；“重新解析录像”和
   “重新补查”可重试失败录像和待处理数据。历史仅保存在本机
   %LocalAppData%\ApeRadar EX\History\history.db，不会因软件更新丢失。



Disclaimer

This software accesses data via public API provided by Wargaming, and does not interact with the game process in any form, therefore is not a prohibited software/mod. HOWEVER, 
sending messages that violate EULA and/or Game and Clan Rules for World of Warships on in-game public channels may result in a chat ban or game ban on your account. 
The developer does not take any responsibility for those penalties. 


User Guide

1. Extract and run ApeRadar.exe. This Windows x64 package is self-contained and does not require .NET.

2. Click Config in the bottom-right and set the World of Warships game path.

3. Player data is displayed after battle start. Random battles also record local ship, opening roster and result.


Website: https://lxdev.org/aperadar/
QQ Group: 1012624096 (1st) 272868560 (2nd)


New Features

1. Player Notes: Right-click a player in the player list and choose "Edit Note" to add a note.
   Whenever that player is encountered again, the note will be shown in the message list, and a 📝
   icon appears next to the player name. Notes are saved together with the watch list in WatchList.json
   and can be managed in Config -> Watch List.

2. PR (Personal Rating): A PR column is added to the statistics. PR is calculated from all of a
   player's ship battle records using the public WoWS Numbers formula
   (https://wows-numbers.com/personal/rating). The per-ship expected values are stored in
   Resources\Json\expected_values.json and can be updated manually via Config -> General ->
   Update PR Data.

3. Manual software update: click "Update app" in the bottom-right corner of the main window to check
   for and install a new version immediately.

4. Update one player: right-click a player and choose "Update This Player" to bypass only that
   player's cache and retrieve the latest statistics. Other players can still use cached data.

5. Recent encounters: a player seen in the previous five battles is marked with an orange 🔁 and the
   encounter count. Regular teammates can be checked in the player context menu and are excluded from
   this marker. The records are stored in EncounterHistory.json.

6. Battle history and ship tracking: click History at the bottom of the main window. Existing Replay
   files are imported from the game's replays folder, and future random-battle Replays are monitored
   automatically. Filter by server, account, ship and date; a ship filter limits every summary and trend
   to that ship. View 10, 20, 50 battle or all-battle rolling win-rate, damage, frags and PR. Wins are
   green, losses red, and PR uses the same rating colors as the main window. Incomplete Replays fall
   back to ship-stat snapshots; Retry replays and Retry API retry failed files and pending results. Data
   remains local at %LocalAppData%\ApeRadar EX\History\history.db and survives app updates.
