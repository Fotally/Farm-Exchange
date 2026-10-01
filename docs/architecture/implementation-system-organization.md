# 当前系统组织与状态归属

本文记录已落地的模块关系，随实施任务更新；未来玩法和目标接口见[系统设计与执行计划](../farm-exchange-system-design-and-execution-plan.md)。

```mermaid
flowchart LR
    Main[Main：场景与窗口] --> FarmGame[FarmGame：经营命令与推进]
    Main --> UiWindows[UI 窗口：固定控件与玩家意图]
    UiWindows --> DragWindow[DraggableWindow：拖动与位置]
    Main --> UiStyle[UiElements：共用视觉元素]
    Main --> WorldMap[WorldMap：地图表现与选格]
    Main --> WorkerPresentation[WorkerPresentation：工人快照与插值]
    WorkerPresentation --> FarmGame
    WorkerPresentation --> WorldMap
    WorkerPresentation --> NpcCharacter[NpcCharacter：角色动画]
    Camera[CameraController：输入] --> WorldMap
    WorldMap --> MapCoordinates[MapCoordinates：格与本地坐标]
    WorldMap --> FarmGame
    FarmGame --> LandOccupancy[LandOccupancy：每格主要占用]
    FarmGame --> PlacementRules[PlacementRules：放置预检与执行规则]
    PlacementRules --> LandOccupancy
    PlacementRules --> MapCoordinates
    FarmGame --> FarmingSystem[FarmingSystem：农田状态]
    FarmGame --> PlantingRules[PlantingRules：播种季节与预计成熟]
    FarmGame --> ProcessingSystem[ProcessingSystem：加工状态]
    FarmGame --> WorkerScheduler[WorkerScheduler：三人工人调度]
    WorkerScheduler --> FarmingSystem
    FarmingSystem --> PlantingRules
    PlantingRules --> CropCatalog
    PlantingRules --> GameCalendar
    FarmGame --> CropCatalog[CropCatalog：作物定义]
    FarmGame --> Inventory[Inventory：分类库存与原料保留]
    ProcessingSystem --> Inventory
    FarmGame --> Wallet[Wallet：金币余额]
    FarmGame --> Market[MarketPriceCurve：面粉曲线]
    FarmGame --> GameCalendar[GameCalendar：唯一日历与暂停状态]
    FarmingSystem --> GameTimeUnits[GameTimeUnits：精确时间比例]
    ProcessingSystem --> GameTimeUnits
    GameCalendar --> GameTimeUnits
    NpcPreview[NpcPreview：独立预览与输入] --> NpcCharacter
    NpcCharacter --> NpcSheets[20 张 NPC 动画图]
```

| 状态或计算 | 当前拥有者 | 其他模块的使用方式 |
| --- | --- | --- |
| 每格主要占用类别 | `LandOccupancy` | `FarmGame` 协调建造、拆除，并聚合地块快照。 |
| 建筑描述、范围、占用与余额的放置检查 | `PlacementRules` | `FarmGame` 预检和执行使用同一规则；执行时重新检查并返回实际扣费。 |
| 农田选种、水分、阶段与剩余精确时长 | `FarmingSystem` | `FarmGame` 在步进开始传入显式降雨，协调按作物收获量入库；换季后通过完整清理操作处理禁生作物，`WorkerScheduler` 只通过受控工作操作照料。 |
| 播种适宜季节与预计成熟判断 | `PlantingRules` | `FarmingSystem` 执行播种和 `FarmGame` 查询详情共用同一只读结果。 |
| 加工场地配对与进行中批次 | `ProcessingSystem` | `FarmGame` 协调完工入库与领取相位；模块从 `Inventory` 领取原料。 |
| 工人位置、当前任务、独占认领与候选游标 | `WorkerScheduler` | `FarmGame` 只推进一秒并提供只读快照；三人按编号推进，当前选择算法留在私有方法，外部不读取或修改游标及认领表。 |
| 累计模拟秒、日历和暂停 | `GameCalendar` | `FarmGame` 每次经营步进推进一秒；主界面读取日期并设置暂停。 |
| 跨日市场更新 | `FarmGame` | 日历进入新日时查询旧市场曲线并刷新当日价格。 |
| 生产共用的时间比例 | `GameTimeUnits` | 日历、农田和加工使用同一整数比例与剩余秒数换算。 |
| 七种作物的定义 | `CropCatalog` | `FarmGame` 查询只读作物表与单种定义。 |
| 各作物的原料、加工品库存与原料保留底线 | `Inventory` | `FarmGame` 在生产、设置和交易时查询或修改；加工领取共用受底线限制的完整操作，界面仍通过 `FarmGame` 查询。 |
| 金币余额 | `Wallet` | `FarmGame` 在建造和交易时扣款或入账；界面仍通过 `FarmGame.MoneyCents` 查询。 |
| 面粉价格曲线 | `MarketPriceCurve` | `FarmGame` 按种子和日期查询价格。 |
| 格坐标范围与等距本地坐标换算 | `MapCoordinates` | `WorldMap` 用于选格、绘制和镜头限制；地图格数引用 `FarmGame.MapSize`。 |
| 地图块缓存、选中格和可见性 | `WorldMap` | `Main` 同步外观；`CameraController` 发起选格与限制镜头。 |
| 摆放模式与所选格 | `Main` | 分发窗口意图，调用 `FarmGame`，在命令完成后刷新。 |
| 窗口位置与拖动 | `DraggableWindow` | 各窗口继承统一标题栏、层级抬升和视窗限制；只在本次运行保留位置。 |
| 目录选择、未提交输入与固定控件 | 各 UI 窗口 | 只更新值和按钮状态，向 `Main` 发出选择、改种、底线设置、出售或移除意图；库存草稿与焦点保留，底线权威值属于库存模块。 |
| 农田与加工详情状态原因 | `FarmGame` 聚合规则模块结果 | 加工复用 `ProcessingSystem` 与库存的实时可领取判断；通过两类详情快照给面板，UI 只映射可见文案。 |
| 独立 NPC 预览的角色选择和键盘输入 | `NpcPreview` | 将图集与移动方向交给 `NpcCharacter`；不修改 `FarmGame`。 |
| 工人视觉位置与插值段 | `WorkerPresentation` | 从经营快照换算位置并显示三人；没有到达、作业或库存权限，渲染帧率不改变经营结果。 |
| NPC 图集帧、朝向与视觉移动 | `NpcCharacter` | 预览按输入移动；主地图通过 `ShowAt` 展示经营位置与朝向，停止自主物理移动。 |

| 要修改的现行行为 | 先查看 | 同时核对 |
| --- | --- | --- |
| 格中心、选格或镜头位置 | `MapCoordinates`、`WorldMap` | `CameraController`、坐标与镜头测试；区分本地和全局位置。 |
| 建造、占用或拆除 | `FarmGame`、`PlacementRules`、`LandOccupancy`、`FarmingSystem`、`ProcessingSystem` | `Main`、`WorldMap` 的快照同步和建造测试。 |
| 作物、播种季节、供水、加工或库存 | `FarmGame`、`FarmingSystem`、`PlantingRules`、`ProcessingSystem`、`WorkerScheduler`、`CropCatalog`、`Inventory` | 步进开始的降雨、工人浇水、预计成熟、收获清水与经营测试。 |
| 工人选择、认领、移动或工作节奏 | `WorkerScheduler`、`FarmingSystem` 的工作需求与指定动作执行 Interface | 局部版本失效、雨水满足、真实等工时间；替换策略优先改内部选择方法，保持一秒推进与快照 Interface。 |
| 工人画面、朝向或插值 | `WorkerPresentation`、`NpcCharacter`、`MapCoordinates` | 主场景组装、地图变换、暂停与镜头外生产；动画回调不写经营状态。 |
| 市场价格或出售 | `MarketPriceCurve`、`FarmGame`、`Inventory`、`Wallet` | 当日成交价、交易测试与玩家规则。 |
| 原料保留底线与加工竞争 | `Inventory`、`ProcessingSystem`、`FarmGame` | 下次经营领取、新建全场即时领取、稳定格序、手动出售与库存输入刷新。 |
| 日期边界与生产时间 | `GameCalendar`、`GameTimeUnits` | 同步核对 `FarmGame` 相位、生产模块和日历测试。 |
| 窗口交互 | `Main`、`DraggableWindow`、对应具体窗口 | 玩家操作、固定控件刷新、场景节点名和端到端测试。 |
| NPC 动画、角色图集或预览输入 | `NpcCharacter`、`NpcPreview` | 角色场景、20 张素材和预览集成测试；不要用动画回调推进经营。 |

T01/T02 已将窗口容器、目录、选种、库存、市场和详情从 `Main` 的构造与重建逻辑中抽出；经营刷新保留控件实例。T03 已统一坐标入口，T04A～T04C 已统一状态归属和放置规则。T05A 完成独立历法，T05B 已接管经营时间并切换七作物参数；T06A 的田块水分由 `FarmingSystem` 唯一持有，T06B 的播种判断由 `PlantingRules` 统一提供。T06C 的 `ClearDisallowedCrops(Season)` 把阶段筛选、适宜季节判断与本轮清理收在农田模块内；经营入口只在成熟结算后的换季相位调用。道路占格留在 #45；实际工人移动由 T07 接入。

T08 的底线与领取判定收在 `Inventory` 中，`ProcessingSystem.TryStart` 完成领取及启动批次，状态查询实时复用同一判定；`FarmGame.SetRawReserve` 仅设置，不泄露内部容器或让 UI 协调加工步骤。默认 0、非法值、稳定竞争、设置时点、完工/出售/拆除守恒与库存编辑刷新由现有资源、生产、经营和端到端测试覆盖。

T07 将真实工人状态收在 `WorkerScheduler`，农田 Interface 负责与策略无关的需求和执行凭据重验，表现只读取快照。当前稳定轮转可在内部任务选择方法替换，经营入口与表现的 Interface 保持稳定；后续扩员方式仍待 #43 的后续范围确认。
