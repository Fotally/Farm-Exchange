# BuildCatalogWindow 接口

对应 `scripts/ui/BuildCatalogWindow.cs`，继承 `DraggableWindow`。构造时创建固定的农田、七种加工场地、五种道路材质和拆路工具卡片；`RefreshCards()` 根据当前分类和搜索词更新文字与可见性，不重建控件。`SelectionRequested(BuildingKind, CropKind)` 报告农田／加工意图，`RoadRequested(RoadMaterial)` 报告材质选择，`RoadRemovalRequested()` 报告拆路意图。`Main` 拥有当前工具与未提交段，将第二次道路短按交给 `FarmGame.TryPlaceRoads`／`TryRemoveRoads`；目录不预先修改经营。

节点名保留 `BuildWindow`、`FarmTab`、`ProcessorTab`、`RoadTab`、`BuildSearch`、`BuildCards`、`FarmCard`、`RoadCard` 和各 `ProcessorCard`。`RoadCard` 为土路，其他材质节点为 `RoadCardGravel/Cobble/Brick/Slab`，拆路为 `RoadRemovalCard`。`AllBuildingsTab` 默认显示十四项（八生产设施、五道路材质、一拆路工具）；搜索支持材质中文名称与道路关键词。分类切换保留搜索词，清空恢复分类完整目录。

目录采用共享纸面木框主题，分类与搜索同一行，之后为设施卡片及连续建造说明。卡片标明用途或原料到产物关系、占地和费用，全部价格及底部费用提示读取 `FarmGame.GetBuildingCostCents(BuildingKind)`，与实际放置使用同一查询。首选尺寸 `790×620`，统一避让顶部状态与底部入口；`BuildScroll` 提供卡片纵向滚动。1080P 基准及更大桌面尺寸默认完整显示 `BuildCatalogHint` 两行费用与取消说明，无需滚动整个窗口；卡片长列表通过内部列表滚动到达。分类切换、经营刷新与关闭重开保持卡片身份、搜索词和窗口位置。

目录不持有地块、金币或正在摆放的状态，也不执行扣款。当前工具与道路段由 `Main` 拥有；设施取消直接退出，道路右键／Esc 二级取消，取消按钮直接退出，见[道路规则](../../../gameplay/land/roads.md)。道路参与布局与工人选路偏好；目录不计算选路成本、工人速度或生产规则。

农田和加工卡片标明 `3×3`，道路卡片标明 `1×1`；费用仍读取唯一查询。这里的占地文字仅帮助玩家选择，完整几何由土地放置规则在实际执行时重验。

道路卡片与搜索共用 [UiElements](../ui-elements/interface-ui-elements.md) 的 `RoadMaterialName` 中文名称，不为共享文字依赖 Main。五材质的名称和同价展示由现有 `TestUiFacilities` 同时核对目录与道路详情。

`tests/e2e/TestUiFacilities.cs` 验证默认生产设施、五道路材质同价与拆路卡片、全类搜索和固定控件身份；`TestRoadPlacement` 验证真实选择事件与道路工具交互。共享布局测试检查窗口边界、费用与操作说明可见，以及末排卡片滚动可达。

卡片排列仍为农田、七类加工场地、五道路材质、拆路工具，文字 12，卡片最低基准 `220×200`，窗口 `790×620`。农田继续原 SVG；七加工和五道路复用 [FacilityPreview](../facility-preview/interface-facility-preview.md) 缓存的正式素材缩略图。加工分别对应自身完整 q0 PNG，五道路采用同形三格短直路段，拆路用道路加红色工具角标；图片不含经营状态，不增加动画或旋转入口。

新 PNG 缩略图统一为 145×108 透明展示画布，按非透明内容等比最近邻缩放、水平居中和底对齐。按钮保留原生 `Icon`／`Text` 及原事件；`IconAlignment.Center`、`VerticalIconAlignment.Top` 和最大宽 145 让原生按钮为图和三行文字分别预留区域，不新增隐藏文字或第二份可见文本。最近邻仅显式设在加工、道路和拆路按钮，不改变农田 SVG 与选种图的采样。

列表最多三列；字体或整体倍率调整后，按当前可用宽度与真实按钮最小宽度减少列数，保持信息可读与纵向滚动可达，不扩大地图或人物。`TestFacilityThumbnails` 在真实主场景检查独立正式源、五材质及拆路区别、原分类／搜索／滚动／点击意图，以及整体 1／1.25 和字体 1／1.2 倍率；有窗口证据输出 `build/issue149-validation/`。
