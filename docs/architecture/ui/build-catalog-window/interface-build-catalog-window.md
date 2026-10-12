# BuildCatalogWindow 接口

对应 `scripts/ui/BuildCatalogWindow.cs`，继承 `DraggableWindow`。构造时创建固定的农田、七种加工场地、五种道路材质和拆路工具卡片；`RefreshCards()` 根据当前分类和搜索词更新文字与可见性，不重建控件。`SelectionRequested(BuildingKind, CropKind)` 报告农田／加工意图，`RoadRequested(RoadMaterial)` 报告材质选择，`RoadRemovalRequested()` 报告拆路意图。`Main` 拥有当前工具与未提交段，将第二次道路短按交给 `FarmGame.TryPlaceRoads`／`TryRemoveRoads`；目录不预先修改经营。

节点名保留 `BuildWindow`、`FarmTab`、`ProcessorTab`、`RoadTab`、`BuildSearch`、`BuildCards`、`FarmCard`、`RoadCard` 和各 `ProcessorCard`。`RoadCard` 为土路，其他材质节点为 `RoadCardGravel/Cobble/Brick/Slab`，拆路为 `RoadRemovalCard`。`AllBuildingsTab` 默认显示十四项（八生产设施、五道路材质、一拆路工具）；搜索支持材质中文名称与道路关键词。分类切换保留搜索词，清空恢复分类完整目录。

目录采用共享纸面木框主题，分类与搜索同一行，之后为设施卡片及连续建造说明。卡片标明用途或原料到产物关系、占地和费用，全部价格及底部费用提示读取 `FarmGame.GetBuildingCostCents(BuildingKind)`，与实际放置使用同一查询。首选尺寸 `790×620`，统一避让顶部状态与底部入口；`BuildScroll` 提供卡片纵向滚动。1080P 基准及更大桌面尺寸默认完整显示 `BuildCatalogHint` 两行费用与取消说明，无需滚动整个窗口；卡片长列表通过内部列表滚动到达。分类切换、经营刷新与关闭重开保持卡片身份、搜索词和窗口位置。

目录不持有地块、金币或正在摆放的状态，也不执行扣款。当前工具与道路段由 `Main` 拥有；设施取消直接退出，道路右键／Esc 二级取消，取消按钮直接退出，见[道路规则](../../../gameplay/land/roads.md)。道路只参与布局，不计算工人速度或生产规则。

农田和加工卡片标明 `3×3`，道路卡片标明 `1×1`；费用仍读取唯一查询。这里的占地文字仅帮助玩家选择，完整几何由土地放置规则在实际执行时重验。

道路卡片与搜索共用 [UiElements](../ui-elements/interface-ui-elements.md) 的 `RoadMaterialName` 中文名称，不为共享文字依赖 Main。五材质的名称和同价展示由现有 `TestUiFacilities` 同时核对目录与道路详情。

`tests/e2e/TestUiFacilities.cs` 验证默认生产设施、五道路材质同价与拆路卡片、全类搜索和固定控件身份；`TestRoadPlacement` 验证真实选择事件与道路工具交互。共享布局测试检查窗口边界、费用与操作说明可见，以及末排卡片滚动可达。

原型比例复刻将卡片排列为农田、七类加工场地、道路，复用 [FacilityPreview](../facility-preview/interface-facility-preview.md) 的 UI SVG 插画：图形基准宽 145，文字 12，卡片基准 `220×200`。这些插画不进入地图素材，也不包含原型的示例经营值。列表最多三列；字体或整体倍率调整后，按当前可用宽度与真实按钮最小宽度减少列数，保持信息可读与纵向滚动可达，不扩大地图或人物。
