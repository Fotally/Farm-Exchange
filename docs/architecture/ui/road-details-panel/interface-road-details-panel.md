# RoadDetailsPanel 接口

对应 `scripts/ui/RoadDetailsPanel.cs`，由 `Main` 在场景创建时放入地块详情窗口，节点名为 `RoadDetailsPanel`。它创建固定的道路名称、当前仅用于布局和外观的用途说明、拆除不退款说明以及 `RemoveRoadButton`；经营刷新不重建这些控件。

`RemoveRequested` 只表达拆除当前道路的玩家意图。`Main` 根据所选格调用 `FarmGame.RemoveBuilding`，再统一同步地图与详情；面板不扣钱、不修改占用，也不持有经营状态。

`Main` 只在 `PlotSnapshot.Building == BuildingKind.Road` 时显示本面板。道路没有当前作物或加工进度，面板无需 `Refresh` 生产数据，不调用 `GetFarmDetails`/`GetProcessorDetails`，不显示价格、库存或作物周期。其他详情面板按明确类型独立分派。

道路规则与费用由土地专题维护，后续速度或属性尚未实施，本面板只展示当前用途。`tests/e2e/TestCoreLoop.cs` 覆盖道路详情显式分派、控件身份、拆除释放占用且不退款，以及相邻道路保持。

#94 沿用共享纸面木框与深棕正文，先显示「道路 · 1×1 基础格」及当前用途，再说明建造目录内的连续铺路入口；末尾说明拆除释放占地且不退费，`RemoveRoadButton` 使用低强调按钮。只调整视觉和信息顺序，经营接口、事件与拆除结果保持。

旧版主场景截图（#94 的纸面详情以本次验收截图为准）：

![道路详情与三名工人](road-details.png)
