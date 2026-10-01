# 主界面目标接口与交互层级

本页对应 [UI issue #24](https://github.com/Fotally/Farm-Exchange/issues/24)，记录 `scenes/main.tscn` 与 `scripts/ui/Main.cs` 的已实现界面接口。具体经营规则仍以 `docs/gameplay/` 为准。

[单文件 HTML 原型](prototype-main.html)用于查看界面层级和点击流程，仅演示视觉与交互；其中的原料价格是首日示意值，库存、交易和建造费不参与真实经营计算。下图为原型市场，实际 Godot 市场以卡片显示两类价格并按品种提供原料出售按钮。

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
| 顶部常驻状态 | 金币、工人摘要与“季 · 第 N 年 · N 月 · N 日”常驻；暂停/继续控制经营时间；库存、市场是全局窗口入口；存档、人物状态保留规划位置。不展示内部 `tick`。 |
| 地图 | 平时点击地块选择对象；进入建造摆放后，点击符合条件的空位执行建造。拖动和缩放仍交给镜头与地图模块。 |
| 底部建造 | 一个“建造”入口打开目录。目录先选择农田或加工场地，再选具体建筑；选择后进入摆放状态，点击目标空位即执行，不需要第二次确认。摆放可取消。 |
| 选中详情 | 仅在选中地块或建筑后出现；农田显示作物状态、获水后成熟所需天数、对应原料当日售价和公共原料库存，并提供改种入口。湿润空田显示待播种状态；不适季或剩余时间不足时显示播种等待原因。加工场地显示加工状态、从投入原料到完成所需天数、原料与产物关系、对应加工品当日售价和公共加工品库存。 |
| 全局窗口 | 库存显示两类公共库存并逐作物编辑非负整数保留底线；市场负责已确认的交易命令。窗口不自行计算价格、费用或加工规则。 |

加工场地目录按名称搜索并可滚动；新增建筑只增加目录条目，不增加底栏固定按钮。作物选择列表只展示每种作物的原料当日价格与公共原料库存，便于比较。市场每种原料配有卖出该种全部库存的按钮，底部保留出售全部加工品的按钮；无对应库存时按钮禁用。

## 二级及以下窗口的位置约定

**右侧选中详情也是二级窗口**，与建造目录、作物选择列表、库存窗口和市场窗口一样，标题栏可拖动。正文里的选择、滚动与操作按钮不触发拖动。

- 窗口第一次打开时使用预设位置：选中详情靠右，建造目录靠近底栏，其余窗口在地图中央附近。
- 玩家拖动后，以该窗口的身份分别记录最后停留位置；关闭再打开同一窗口时回到该位置，切换所选地块时选中详情也沿用上一次位置。
- 窗口位置应保持在可见画面内；窗口尺寸或视窗变化时，要保证标题栏仍可抓取，底部建造按钮不被建造目录遮住。
- 打开窗口或拖动标题栏时，该窗口来到其他窗口前方；重新选中地图地块时详情来到前方，库存和市场窗口关闭。
- 位置只属于界面状态，不进入 `FarmGame` 的经营状态。**仅在本次运行内保留**：关闭再打开保持位置，重启游戏恢复默认位置；不写入存档。

## 与经营模块的接口

`Main` 负责组装窗口、保留摆放与所选格、分发玩家命令，并在命令或 tick 完成后统一刷新。建造目录发出建筑意图，地图点击时 `Main` 调用 `FarmGame.TryPlace`，失败显示结果原因，成功显示实际扣费 `ChargedCents`。库存、市场和选种窗口只更新固定控件并发出操作意图；农田与加工详情分别读取 `FarmGame` 的语义快照，不在界面重判经营状态。地图外观仅在影响地块或 tick 的命令后同步，出售只刷新经营窗口。

库存的 `RawReserveRequested` 交给 `FarmGame.SetRawReserve`；成功后 `Main` 调用窗口确认输入并统一刷新，失败显示稳定原因。底线修改只刷新经营窗口，不同步地图、不启动加工。库存窗口的草稿、焦点与滚动由窗口自身维护。加工详情区分缺料、底线限制、待领取与进行中；底线编辑与生效规则见[原料加工](../../../gameplay/production/processing.md)。

当前农田详情显示“生长周期：获得水后 N 天成熟”，加工详情显示“加工周期：投入原料后 N 天完成”。两类详情各显示自己负责的库存与售价，库存仍按品种共享；主界面不计算建造费用、价格曲线或生产时间，也不暴露内部 `tick`。窗口实现见 [DraggableWindow](../draggable-window/interface-draggable-window.md)、[BuildCatalogWindow](../build-catalog-window/interface-build-catalog-window.md)、[CropSelectionWindow](../crop-selection-window/interface-crop-selection-window.md)、[InventoryWindow](../inventory-window/interface-inventory-window.md)、[MarketWindow](../market-window/interface-market-window.md)、[FarmDetailsPanel](../farm-details-panel/interface-farm-details-panel.md)、[ProcessorDetailsPanel](../processor-details-panel/interface-processor-details-panel.md) 和 [UiElements](../ui-elements/interface-ui-elements.md)。

[建造规则 issue #26](https://github.com/Fotally/Farm-Exchange/issues/26)已按当前约定取消土地解锁，并实行暂定的 10.00 金币建造费。年月日和季节由 `GameCalendar` 给出；每周独立行情仍归[市场 issue #25](https://github.com/Fotally/Farm-Exchange/issues/25)。[原料交易 issue #27](https://github.com/Fotally/Farm-Exchange/issues/27)确定了逐作物价格和按品种出售；详情、作物选择列表与市场均从 `FarmGame` 查询实时原料售价和库存，结算规则见[出售与价格](../../../gameplay/trading/sales.md)。

## 验收对应

`tests/e2e/TestCoreLoop.cs` 覆盖目录选择、单次摆放、占用地块不扣费、两类建筑详情的状态、周期、售价与库存、春季甘蔗的不适季提示、窗口拖动、底栏避让、关闭重开位置、市场刷新后滚动与按钮身份，以及两类市场交易。`tests/integration/TestCameraInteraction.cs` 覆盖地图点击与镜头拖动的衔接。窗口只保存当前运行的节点位置，不写入存档。

库存编辑用例还验证按钮与回车提交、非法文本拒绝、下一步领取和提高底线不退料，以及经营刷新、失焦、关闭重开时保留草稿、焦点、光标、滚动与控件身份。
