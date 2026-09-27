# BuildCatalogWindow 接口

对应 `scripts/ui/BuildCatalogWindow.cs`，继承 `DraggableWindow`。`RefreshCards()` 按当前农田/加工分类和搜索词重建目录卡片；`SelectionRequested(BuildingKind, CropKind)` 只报告选择的建筑意图。`Main` 接到意图后进入摆放状态，并由 `FarmGame.TryPlace` 在地图点击时校验与扣费。

节点名保留 `BuildWindow`、`FarmTab`、`ProcessorTab`、`BuildSearch`、`BuildCards`、`FarmCard` 和各 `ProcessorCard`。目录沿用现行建筑名、费用文案、初始位置及底栏避让；搜索只筛加工场地。目录不持有地块、金币或正在摆放的状态。
