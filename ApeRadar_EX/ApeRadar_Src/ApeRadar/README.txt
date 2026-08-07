免责声明

本软件使用Wargaming公开的API接口进行数据查询，不以任何形式与游戏软件本身进行交互，因此不属于禁用软件/模组。但是
在游戏内公共频道发送违反EULA和/或Game and Clan Rules for World of Warships的信息可能会导致您的账号遭到禁止聊天或禁止游戏的处罚。
作者不对以上情况承担任何责任。


使用说明

1.安装.NET 6.0
https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-6.0.19-windows-x64-installer

2.运行ApeRadar.exe。单击右下角的设置按钮，设置游戏路径。

3.开局将自动显示战绩数据。


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



Disclaimer

This software accesses data via public API provided by Wargaming, and does not interact with the game process in any form, therefore is not a prohibited software/mod. HOWEVER, 
sending messages that violate EULA and/or Game and Clan Rules for World of Warships on in-game public channels may result in a chat ban or game ban on your account. 
The developer does not take any responsibility for those penalties. 


User Guide

1. Install .NET 6.0
https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-6.0.19-windows-x64-installer

2. Run the ApeRadar.exe. Click the Config button in the bottom-right and set the Game Path. 

3. Data will be displayed after battle start. 


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
