# ProcessorDetailsPanel 接口

对应 `scripts/ui/ProcessorDetailsPanel.cs`，由 `Main` 放在地块详情窗口内。`Refresh(ProcessorDetailsSnapshot)` 只更新现有状态、原料与产物关系、加工周期、加工品当前报价和库存标签；`RemoveRequested` 把移除意图交给 `Main`。

状态语义由 `FarmGame.GetProcessorDetails` 提供，面板映射为等待对应原料、等待原料超过保留底线、待领取原料和加工中。降低底线后到下次领取前显示待领取原料；不通过库存或倒计时自行重判原因。移除按钮保留 `RemoveButton` 名称；农田字段由独立的农田详情面板显示。

#94 调整为「当前状态 → 本批真实剩余与时间进度 → 一份原料到一份产物的关系与周期 → 加工品报价及库存 → 下一步 → 低强调移除」。下一步说明按照经营入口的 `ProcessorStatus` 展示等待收获或买入原料、调整底线、等待下一次领取、完成后市场出售等实际入口；面板不自行重判状态，也不执行买卖或修改底线。

调用顺序为 `Refresh(ProcessorDetailsSnapshot)` 后立即 `RefreshProgress(PlotSnapshot)`，两份快照必须来自同一加工实例。`Main` 使用当前已选格的地块快照调用进度入口，无需新增经营查询。`ProcessorActualProgress` 把真实剩余秒向上取一位游戏天数；`ProcessorProgress` 从本批剩余秒与定义中的加工周期换算，仅有活动批次时显示，没有批次时明确说明。进度受快照秒数取整精度限制，不显示虚构精确百分比、不在渲染帧计时。

移除按钮前明确说明「丢失已投入原料和未完成批次，不退还建造费」。`tests/e2e/TestUiFacilities.cs` 通过真实买入与经营步进启动批次，核对实际剩余，并验证暂停刷新不推进进度、恢复经营后进度更新。
