# ProcessorDetailsPanel 接口

对应 `scripts/ui/ProcessorDetailsPanel.cs`，由 `Main` 放在地块详情窗口内。`Refresh(ProcessorDetailsSnapshot)` 只更新现有状态、原料与产物关系、加工周期、加工品售价和库存标签；`RemoveRequested` 把移除意图交给 `Main`。

状态语义由 `FarmGame.GetProcessorDetails` 提供，面板把无原料等待与加工中映射为玩家文案。移除按钮保留 `RemoveButton` 名称；农田字段由独立的农田详情面板显示。
