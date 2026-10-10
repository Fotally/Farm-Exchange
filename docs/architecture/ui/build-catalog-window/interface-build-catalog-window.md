# BuildCatalogWindow 接口

对应 `scripts/ui/BuildCatalogWindow.cs`，继承 `DraggableWindow`。构造时创建固定的农田、七种加工场地、道路卡片与无匹配提示；`RefreshCards()` 根据当前分类和搜索词更新文字与可见性，不删除或重建控件。`SelectionRequested(BuildingKind, CropKind)` 只报告选择的建筑意图；道路意图的作物字段没有经营含义。`Main` 接到意图后进入摆放状态，并由 `FarmGame.TryPlace` 在地图点击时校验与扣费。

节点名保留 `BuildWindow`、`FarmTab`、`ProcessorTab`、`RoadTab`、`BuildSearch`、`BuildCards`、`FarmCard`、`RoadCard` 和各 `ProcessorCard`。目录包含 `AllBuildingsTab`，默认显示九项设施；搜索对当前分类的设施名称生效，全部分类可同时检索农田、加工场地和道路。分类切换保留搜索词，因此非匹配分类会显示无匹配提示；清空搜索恢复该分类完整目录。

目录采用共享纸面木框主题，分类与搜索同一行，之后为设施卡片及连续建造说明。卡片标明用途或原料到产物关系、占地和费用，全部价格及底部费用提示读取 `FarmGame.GetBuildingCostCents(BuildingKind)`，与实际放置使用同一查询。首选尺寸 `790×620`，统一避让顶部状态与底部入口；`BuildScroll` 提供卡片纵向滚动。1080P 基准及更大桌面尺寸默认完整显示 `BuildCatalogHint` 两行费用与取消说明，无需滚动整个窗口；卡片长列表通过内部列表滚动到达。分类切换、经营刷新与关闭重开保持卡片身份、搜索词和窗口位置。

目录不持有地块、金币或正在摆放的状态，也不执行扣款。所有类型的连续摆放模式由 `Main` 拥有，目录只发出选择意图；右键、Esc 或取消按钮统一退出。后续道路用途不通过目录重新计算工人速度或生产规则。

农田和加工卡片标明 `3×3`，道路卡片标明 `1×1`；费用仍读取唯一查询。这里的占地文字仅帮助玩家选择，完整几何由土地放置规则在实际执行时重验。

`tests/e2e/TestUiFacilities.cs` 验证默认九项、全类名称搜索与分类刷新控件身份；现有主场景摆放测试继续验证真实费用、占用拒绝及连续摆放。共享布局测试检查窗口边界、默认费用及退出说明完整可见，以及最后一排卡片滚动可达。

原型比例复刻将卡片排列为农田、七类加工场地、道路，复用 [FacilityPreview](../facility-preview/interface-facility-preview.md) 的 UI SVG 插画：图形基准宽 145，文字 12，卡片基准 `220×200`。这些插画不进入地图素材，也不包含原型的示例经营值。列表最多三列；字体或整体倍率调整后，按当前可用宽度与真实按钮最小宽度减少列数，保持信息可读与纵向滚动可达，不扩大地图或人物。
