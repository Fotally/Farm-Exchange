# FarmDetailsPanel 接口

对应 `scripts/ui/FarmDetailsPanel.cs`，由 `Main` 放在地块详情窗口内。`Refresh(FarmDetailsSnapshot)` 只更新现有状态、周期、原料当前报价和库存标签；`ChangeCropRequested`、`RemoveRequested` 把按钮意图交给 `Main`。

状态语义由 `FarmGame.GetFarmDetails` 提供，面板把等待工人、湿润待播种、等待浇水、生长中、不适季与计划休耕映射为玩家文案；预计剩余时间不足显示“可播种、有枯萎风险”，不表示禁止播种。周期显示“获得水后 N 天成熟”。

`RefreshCultivation(FarmGame, Vector2I)` 读取本田引用、实际剩余时间及预备安排；实际剩余换算为游戏天数并向上取一位小数，不向玩家展示经营秒。待水时明确完成日期尚未确定。`PrepareCropRequested` 报告保留本轮的手动预备意图，`CultivationRequested` 打开共享表窗口。`ChangeCropButton` 和 `RemoveButton` 保留原节点名，新增 `PrepareCropButton` 与 `FarmCultivationButton`。两种手动操作的损失及解除引用提示在作物窗口选择前显示。加工字段由独立加工详情面板显示。

`FarmCultivationStatus` 标签展示预备日期。初年春初引用冬春连续条时，计划原起点可能在游戏时间零点之前；按年度位置显示“上一年度 X月X日”，不转成无符号累计日期、不夹到开局日、不虚构第0年。非负绝对起点继续显示真实的“第N年 X月X日”。
