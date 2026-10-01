# ProcessorDetailsPanel 接口

对应 `scripts/ui/ProcessorDetailsPanel.cs`，由 `Main` 放在地块详情窗口内。`Refresh(ProcessorDetailsSnapshot)` 只更新现有状态、原料与产物关系、加工周期、加工品当前报价和库存标签；`RemoveRequested` 把移除意图交给 `Main`。

状态语义由 `FarmGame.GetProcessorDetails` 提供，面板映射为等待对应原料、等待原料超过保留底线、待领取原料和加工中。降低底线后到下次领取前显示待领取原料；不通过库存或倒计时自行重判原因。移除按钮保留 `RemoveButton` 名称；农田字段由独立的农田详情面板显示。
