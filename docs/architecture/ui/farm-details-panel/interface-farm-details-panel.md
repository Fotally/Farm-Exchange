# FarmDetailsPanel 接口

对应 `scripts/ui/FarmDetailsPanel.cs`，由 `Main` 放在地块详情窗口内。`Refresh(FarmDetailsSnapshot)` 只更新现有状态、周期、原料当前报价和库存标签；`ChangeCropRequested`、`RemoveRequested` 把按钮意图交给 `Main`。

状态语义由 `FarmGame.GetFarmDetails` 提供，面板把等待工人、湿润待播种、等待浇水、生长中、不适季和剩余时间不足映射为玩家文案；周期显示“获得水后 N 天成熟”。改种提示保留田块水分。`ChangeCropButton` 和 `RemoveButton` 保留现有节点名；加工字段由独立的加工详情面板显示。
