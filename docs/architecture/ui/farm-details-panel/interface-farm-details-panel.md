# FarmDetailsPanel 接口

对应 `scripts/ui/FarmDetailsPanel.cs`，由 `Main` 放在地块详情窗口内。`Refresh(FarmDetailsSnapshot)` 只更新现有状态、周期、每轮产量、原料当前报价和库存标签；`ChangeCropRequested`、`RemoveRequested` 把按钮意图交给 `Main`。

状态语义由 `FarmGame.GetFarmDetails` 提供，面板把等待工人、湿润待播种、等待浇水、生长中、不适季与计划休耕映射为玩家文案；预计剩余时间不足显示“可播种、有枯萎风险”，不表示禁止播种。周期显示“获得水后 N 天成熟”。

`RefreshCultivation(FarmGame, Vector2I)` 读取本田引用、实际剩余时间及预备安排；实际剩余换算为游戏天数并向上取一位小数，不向玩家展示经营秒。待水时明确完成日期尚未确定。`PrepareCropRequested` 报告保留本轮的手动预备意图，`CultivationRequested` 打开共享表窗口。`ChangeCropButton` 和 `RemoveButton` 保留原节点名，新增 `PrepareCropButton` 与 `FarmCultivationButton`。两种手动操作的损失及解除引用提示在作物窗口选择前显示。加工字段由独立加工详情面板显示。

`FarmCultivationStatus` 标签展示预备日期。初年春初引用冬春连续条时，计划原起点可能在游戏时间零点之前；按年度位置显示“上一年度 X月X日”，不转成无符号累计日期、不夹到开局日、不虚构第0年。非负绝对起点继续显示真实的“第N年 X月X日”。

#94 按「当前状态 → 真实剩余与水分 → 本轮时间进度 → 周期与每轮产量 → 原料报价及库存 → 年度表与下一轮 → 下一步操作」组织详情。`FarmActualProgress` 独立显示实际剩余、待水或未播种；`FarmProgress` 仅在生长中可见。进度条依据同一 `PlotSnapshot.RemainingSeconds` 和作物定义周期换算，并受剩余秒向上取整的精度限制，不伪造精确完成百分比或确定收获预测；动画和刷新都不推进经营。

共享年度表标签继续保留 `FarmCultivationStatus` 节点名及预备日期语义。预备下一轮为主按钮，立即改种与年度表为纸色次按钮；末尾低强调移除按钮上方明确说明「丢失本轮未收获作物，不退还建造费」。长详情由外层滚动承载，现有事件签名、按钮节点名和经营命令保持。`tests/e2e/TestUiFacilities.cs` 以真实经营推进后的快照核对剩余天数，原有主场景及年度表测试检查改种、预备和移除效果。
