# 主界面目标接口与交互层级

本页对应 [UI issue #24](https://github.com/Fotally/Farm-Exchange/issues/24)，记录 `scenes/main.tscn` 与 `scripts/ui/Main.cs` 的已实现界面接口。具体经营规则仍以 `docs/gameplay/` 为准。

[单文件 HTML 原型](prototype-main.html)用于查看界面层级和点击流程，仅演示视觉与交互；其中的原料价格是首日示意值，库存、交易和建造费不参与真实经营计算。下图为原型市场，实际 Godot 市场采用十四商品固定行、单一数量输入和独立消息滚动区，见[市场窗口接口](../market-window/interface-market-window.md)。

![HTML 原型市场首日示意](prototype-market.png)

![HTML 原型农田原料详情示意](prototype-growth-detail.png)

![HTML 原型加工场地加工品详情示意](prototype-processor-detail.png)

## 当前 Godot 场景

![建造目录](godot-build.png)

![日历与七作物满地图表现](godot-calendar-radish.png)

![雨后未播种农田显示已有水分](godot-rain-water.png)

![春季甘蔗农田显示不适宜播种原因](godot-season-check.png)

## 玩家看到的层级

| 层级 | 内容与作用 |
| --- | --- |
| 顶部常驻状态 | 金币、工人摘要与“季 · 第 N 年 · N 月 · N 日”常驻；人数读取真实工人快照数量，开局显示“3 · 自动照料”；暂停/继续控制经营时间；库存、市场是全局窗口入口；存档、人物状态保留规划位置。不展示内部 `tick`。 |
| 地图 | 平时点击地块选择对象；进入建造摆放后，点击符合条件的空位执行建造。拖动和缩放仍交给镜头与地图模块。 |
| 底部建造 | 一个“建造”入口打开目录。目录可选农田、加工场地或道路；选择后点击目标空位执行。农田与加工场地成功后退出摆放，道路成功后保持逐格连续铺设；占用失败不扣费且保持模式。道路每格费用与其他建筑费用读取统一查询，Esc 或“取消摆放”退出。 |
| 选中详情 | 仅在选中地块或建筑后出现；农田显示作物状态、获水后成熟所需天数、对应原料当前报价和公共原料库存，并提供改种入口。湿润空田显示待播种状态；不适季或剩余时间不足时显示播种等待原因。加工场地显示加工状态、从投入原料到完成所需天数、原料与产物关系、对应加工品当前报价和公共加工品库存。道路显式显示布局用途和拆除不退款说明，并提供拆除按钮。 |
| 全局窗口 | 库存显示两类公共库存并逐作物编辑非负整数保留底线；市场负责已确认的交易命令。窗口不自行计算价格、费用或加工规则。 |

加工场地目录按名称搜索并可滚动；新增建筑只增加目录条目，不增加底栏固定按钮。作物选择列表只展示每种作物的原料当前报价与公共原料库存，便于比较。市场显示十四商品当前/上次报价、实际涨跌与公共库存，选择商品后用一个数量输入买入、卖出或全售该商品；每种原料表行保留按品种全部出售快捷，底部保留出售全部加工品。报价日期与已公布真实因素/节日改期消息直接读取经营快照；无对应库存时全部出售按钮禁用。

## 二级及以下窗口的位置约定

**右侧选中详情也是二级窗口**，与建造目录、作物选择列表、库存窗口和市场窗口一样，标题栏可拖动。正文里的选择、滚动与操作按钮不触发拖动。

- 窗口第一次打开时使用预设位置：选中详情靠右，建造目录靠近底栏，其余窗口在地图中央附近。
- 玩家拖动后，以该窗口的身份分别记录最后停留位置；关闭再打开同一窗口时回到该位置，切换所选地块时选中详情也沿用上一次位置。
- 窗口位置应保持在可见画面内；窗口尺寸或视窗变化时，要保证标题栏仍可抓取，底部建造按钮不被建造目录遮住。
- 打开窗口或拖动标题栏时，该窗口来到其他窗口前方；重新选中地图地块时详情来到前方，库存和市场窗口关闭。
- 位置只属于界面状态，不进入 `FarmGame` 的经营状态。**仅在本次运行内保留**：关闭再打开保持位置，重启游戏恢复默认位置；不写入存档。

## 与经营模块的接口

`Main` 负责组装窗口、保留摆放与所选格、分发玩家命令，并在命令或 tick 完成后统一刷新。建造目录发出建筑意图，地图点击时 `Main` 调用 `FarmGame.TryPlace`，失败显示结果原因，成功显示实际扣费 `ChargedCents`；目录和底栏费用都读取 `GetBuildingCostCents`。连续道路模式在每次成功和失败后保留，直到 Esc、取消或重新打开建造目录；农田与加工场地保持原单次成功退出路径。道路模式下 Esc 优先结束铺设，避免只关窗口却继续扣费铺路。

库存、市场和选种窗口只更新固定控件并发出操作意图；农田与加工详情分别读取 `FarmGame` 的语义快照，道路按 `BuildingKind.Road` 显式显示固定 [RoadDetailsPanel](../road-details-panel/interface-road-details-panel.md)，不对道路调用作物或加工详情查询。地图外观仅在影响地块或 tick 的命令后同步，交易只刷新经营窗口，不同步地图或直接启动加工。

库存的 `RawReserveRequested` 交给 `FarmGame.SetRawReserve`；成功后 `Main` 调用窗口确认输入并统一刷新，失败显示稳定原因。底线修改只刷新经营窗口，不同步地图、不启动加工。库存窗口的草稿、焦点与滚动由窗口自身维护。加工详情区分缺料、底线限制、待领取与进行中；底线编辑与生效规则见[原料加工](../../../gameplay/production/processing.md)。

`Main` 在地图下创建[WorkerPresentation](../../world/worker-presentation/interface-worker-presentation.md)，提供同一局 `FarmGame` 与地图坐标变换；不为显示调用经营推进或接收到达回调。工人摘要标签为 `WorkerCountLabel`，每次经营刷新从 `GetWorkers().Count` 更新。暂停同时停止经营与工人插值/动画，恢复不补现实时间；主地图的三名角色与独立 NPC 预览使用同一角色 Module。

当前农田详情显示“生长周期：获得水后 N 天成熟”，加工详情显示“加工周期：投入原料后 N 天完成”。生产详情各显示自己负责的库存与售价，库存仍按品种共享；道路详情仅显示当前用途和拆除操作。主界面不计算建造费用、价格曲线或生产时间，也不暴露内部 `tick`。窗口实现见 [DraggableWindow](../draggable-window/interface-draggable-window.md)、[BuildCatalogWindow](../build-catalog-window/interface-build-catalog-window.md)、[CropSelectionWindow](../crop-selection-window/interface-crop-selection-window.md)、[InventoryWindow](../inventory-window/interface-inventory-window.md)、[MarketWindow](../market-window/interface-market-window.md)、[FarmDetailsPanel](../farm-details-panel/interface-farm-details-panel.md)、[ProcessorDetailsPanel](../processor-details-panel/interface-processor-details-panel.md)、[RoadDetailsPanel](../road-details-panel/interface-road-details-panel.md) 和 [UiElements](../ui-elements/interface-ui-elements.md)。

[建造规则 issue #26](https://github.com/Fotally/Farm-Exchange/issues/26)已按当前约定取消土地解锁，并实行暂定的 10.00 金币建造费。年月日和季节由 `GameCalendar` 给出；独立双周行情与即时交易对应[市场 issue #25](https://github.com/Fotally/Farm-Exchange/issues/25)及[完整接入 issue #73](https://github.com/Fotally/Farm-Exchange/issues/73)。[原料交易 issue #27](https://github.com/Fotally/Farm-Exchange/issues/27)是按品种出售原料快捷操作的历史来源；当前独立报价与完整结算遵循 #25 / T09 [市场规则](../../../project/market-proposal.md)，详情、作物选择列表与市场均从 `FarmGame` 查询当前报价和公共库存，现行交易规则见[出售与价格](../../../gameplay/trading/sales.md)。

## 验收对应

`tests/e2e/TestCoreLoop.cs` 覆盖目录选择、单次摆放、占用地块不扣费、两类建筑详情的状态、周期、售价与库存、春季甘蔗的不适季提示、窗口拖动、底栏避让、关闭重开位置、市场刷新后滚动与按钮身份，以及两类市场交易。`tests/integration/TestCameraInteraction.cs` 覆盖地图点击与镜头拖动的衔接。窗口只保存当前运行的节点位置，不写入存档。

道路端到端用例另覆盖灰色目录卡片和每格费用、连续两格各扣 100 分、重复铺设失败保持模式且不扣费、Esc/取消退出、道路固定详情和不退款拆除；分类切换保持卡片身份，加工搜索在经营刷新时保留焦点。地图灰色路面与分块缓存由 `tests/integration/TestRoadMap.cs` 验证，仍通过根 `TestSuite` 汇总。

库存编辑用例还验证按钮与回车提交、非法文本拒绝、下一步领取和提高底线不退料，以及经营刷新、失焦、关闭重开时保留草稿、焦点、光标、滚动与控件身份。

市场数量意图由 Main 调用 FarmGame.Buy/Sell/SellCommodityAll；主界面与市场内反馈同时显示实际数量、金额或 TradeResult.ErrorMessage。七原料快捷与全部加工品快捷同样检查 SaleResult.Success，并显示容量不足等原因，不把拒绝当成交。金额格式接受 long，金币和库存经营容量仍为 int。输入非法时窗口不发出交易意图，Main 只同步输入错误消息。暂停允许主动交易；刷新不改数量草稿或商品选择。

#36 从市场“委托与策略”打开 [TradeOrdersWindow](../trade-orders-window/interface-trade-orders-window.md)。Main 组装创建、同 ID 编辑、撤销、持续启停意图到 `FarmGame` 的真实命令，成功和拒绝交回窗口并统一刷新；不计算条件、手续费、保留线或冻结资源。委托命令只刷新经营窗口，不启动加工或重同步地图。每个未暂停 tick 在经营模块完成委托执行后，Main 刷新可见委托窗口；经营暂停只停止 tick，仍允许管理单据。Esc 优先关闭委托窗口，打开库存、市场、建造或选择地图时也关闭它，重新打开保留草稿和位置。顶栏金币沿用总余额，委托窗口明确区分总、可用和冻结资源，建造费用和实际可用资金仍由经营入口重验。

委托端到端验收由 `tests/e2e/TestTradeOrdersWindow.cs` 通过真实主场景完成，包含创建与同单编辑、条件分组、锁定现金基准、单次冻结撤销、持续启停、暂停编辑、真实成交费用、终态记录与市场可售按钮。窗口布局检查读取挂树后的真实矩形和滚动结果，不只检查构造尺寸。

市场端到端检查额外覆盖数量与资金/库存失败零修改、暂停交易、买卖两类商品、全售、报价变化后按执行时报价成交、草稿/焦点/光标/滚动与关闭重开，以及有真实十五行公告时窗口/消息/按钮与最后一行的可见布局。

#75 接入384×384基础格后，`Main._Ready` 用同一地图换算将镜头定位到分数中心 `(191.5,191.5)`。建造目录和摆放提示标明生产设施3×3、道路1×1；任意空小格可作生产设施锚点。`Main` 保留实际点击小格，详情标题通过 `GetBuildingSpace` 显示唯一锚点及占用格数；改种、农田/加工详情和拆除全部将该小格交给经营入口解析整座实例，不自行遍历九份状态。地图同步仍只在改变地块外观的命令和经营tick之后执行，库存/市场/只读详情刷新不全图重同步。

`TestCoreLoop.CheckFootprintUi` 验证真实主场景中心五座免费3×3布局、各田中央工人出生、九子格同详情、非三格对齐锚点一次扣费、子格改种与末端子格拆整座。坐标/输入测试迁移到新中心与四边；生产负载按16,384实例和147,456占用子格分别验收，避免把小格数量作为实体数量。

## 季节耕作表连接

`Main` 从开局底部 `CultivationButton` 和农田 `FarmCultivationButton` 打开共享年度表；接收保存及批量应用意图，调用 `FarmGame` 的同名计划命令并展示真实结果。每秒可见窗口刷新不覆盖编辑草稿；Esc 关闭年度表。

共享表保存成功后同步地图，包括暂停时由表编辑改变空田当期作物的情况，不等待下一经营步。移除选中农田成功时先关闭依赖该田的作物选择窗口，再刷新详情和其他窗口；立即改种与预备下一轮都遵守此生命周期。

农田两个手动按钮分别组装作物窗口模式。立即改种继续调用 `SetFarmCrop`，预备下一轮调用 `PrepareFarmCrop`；窗口选种前告知清空本田安排和立即改种损失。农田详情独立显示实际进度与计划引用。窗口与经营的约定见[耕作表窗口接口](../cultivation-window/interface-cultivation-window.md)。
