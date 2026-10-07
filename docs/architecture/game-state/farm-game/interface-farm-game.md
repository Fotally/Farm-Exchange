# FarmGame 对外接口

`DeleteCultivationPlan(id)` 删除共享配置并解除全部引用田的计划安排：保留各田当前作物、当前轮阶段、精确剩余时长和水分，恢复按当前作物自动复种，仍经工人和季节判断。成功返回 `null`；未知或已删除编号返回“耕作表不存在”且零修改。表编号不回收；不改变其他表、其他农田、手动预备、金币库存或日历，具体约定见[计划 Interface](../../cultivation/interface-cultivation-plan-book.md)。

对应类型：FarmExchange.Gameplay.FarmGame，代码位于 scripts/gameplay/FarmGame.cs。调用方为主场景和地图；测试也通过该公开接口验证行为。当前只有一个实现，未声明 C# interface 类型。

| 用途 | 公开成员 | 调用方需要知道的约定 |
| --- | --- | --- |
| 定义 | Crops、GetCrop、GetBuildingCostCents(building) | 作物定义委托 `CropCatalog`；七种作物的规则来自[作物表](../../../gameplay/production/crop-growth.md)。静态建造费查询为唯一类型价格入口：农田/加工场地 1000 分，道路 100 分；`None` 或未知类型抛 `ArgumentOutOfRangeException` |
| 状态查询 | GetPlot、TryGetPlot、GetStock、GetRawStock、GetRawReserve、GetProductStock、GetRawPriceCents、GetProductPriceCents | 任意占用子格解析到锚点，返回同一整座设施的只读生产快照、统一或分类公共库存、原料保留底线和两类当前报价；农田地块快照含 `HasWater`，`TryGetPlot` 对地图外返回 `OutOfBounds` |
| 空间查询 | GetBuildingSpace(cell)、GetBuildingSpaces() | 任意子格返回同一只读空间，空地或地图外返回空；全空间列表为锚点索引升序的独立只读缓存，包含锚点、类型、固定 footprint 与工作中心 |
| 行情查询 | GetQuote(CommodityId)、GetMarketSnapshot() | 委托 `MarketQuotes` 返回十四商品当前价、上次报价及实际涨跌；完整快照还含本次与下次实际报价日期和已公布消息。查询不推进时间或抽随机 |
| 可用与冻结资源 | AvailableMoneyCents、FrozenMoneyCents、GetAvailableStock、GetFrozenStock | 钱包及库存总量仍包含冻结部分，可用查询扣除一次挂单冻结；建造、加工与即时交易只能使用可用部分，查询不释放冻结 |
| 委托管理 | CreateTradeOrder、UpdateTradeOrder、CancelTradeOrder、SetTradeOrderEnabled、GetTradeOrders | 完整创建或同 ID 编辑，失败单据和资源零修改；取消释放冻结，仅持续策略可启停；独立只读快照按建单顺序保留活动及终态，包含原现金基准、保留线、冻结归属、等待原因和最近成交的实际商品、方向、费用与余额，编辑不会用新配置改写旧成交，规则见[委托玩法](../../../gameplay/trading/orders.md) |
| 工人查询 | GetWorkers() | 按编号 1、2、3 返回独立只读 `WorkerSnapshot` 集合，包含分数格位置、可空目标格与空闲/移动/播种/浇水状态；查询不分配任务或推进时间 |
| 详情查询 | GetFarmDetails、GetProcessorDetails | 分别返回现有农田的等待工人、已湿润待播种、待水、生长中、不适季或剩余时间不足状态，或加工场地的无原料等待、保留底线限制、待领取原料或加工中状态，同时附带对应周期、当日售价和公共库存；两类详情只服务农田/加工场地，类型不匹配属于调用错误 |
| 原料保留设置 | SetRawReserve(crop, quantity) | 逐种设置非负整数底线，默认 0，允许高于现存库存；返回 `RawReserveFailure.None`、`InvalidCrop` 或 `InvalidQuantity`，失败零修改。设置不触发领取；下次经营领取阶段或新建场地的现有即时领取路径检查新值 |
| 放置 | CheckPlacement、TryPlace | 共用[放置规则](../../land/placement-rules/interface-placement-rules.md)；生产建筑完整 3×3、道路 1×1 的几何与余额预检只读，执行时重新检查并返回实际扣费或稳定失败原因；道路使用 `BuildingKind.Road`，作物参数不参与道路语义。非法建筑描述先正常返回 `InvalidBuilding`，不会调用价格查询抛异常 |
| 旧命令与编辑 | BuildFarm、BuildProcessor、SetFarmCrop、PrepareFarmCrop、RemoveBuilding | 旧建造命令转发 `TryPlace`；成功返回 null，失败返回给玩家的原因；立即改种强制丢弃当前轮并保水，预备下一轮保留当前轮，两者都只解除本田耕作表；任一占用子格操作同一整座设施；拆除释放全部 footprint、计划引用且不退款；对地图外都返回“地图外地块” |
| 年度耕作表 | CheckCultivationEntries、CheckCultivationPlan、CreateCultivationPlan、UpdateCultivationPlan、DeleteCultivationPlan、ApplyCultivationPlan、GetCultivationPlans、GetFarmCultivation | 条目检查允许未命名草稿编辑；完整提交检查及创建/更新仍要求名称和有效模式，复用同一年度排程判断。快照含每表生命周期下一条编号 `NextEntryId`，保存空表不回退；更新拒绝新条复用历史编号，移动原条保留年度凭据。批量重验全列表且子格去重，失败全部不修改。应用/编辑保留当前轮；计划收获先关闭播种并缓存目标，日历推进后才重验启用，不能借旧季工人相位提前执行。工人完成后、换季清理前先登记实际计划播种，季末播后即清除也计为该条已执行。快照分开计划日期和真实生产，规则与缓存约定见[计划 Interface](../../cultivation/interface-cultivation-plan-book.md) |
| 选种风险 | GetPlantingCheck(cell,crop) | 只读查询 `PlantingFailure`；`InsufficientTime` 是可播种风险提示，`WrongSeason` 才禁止实际播种。手动目标仍可选任一合法作物，不通过查询执行或清理 |
| 时间与降雨 | AdvanceTick(bool isRaining = false)、Calendar、IsPaused、SetPaused | 每次未暂停步进推进一模拟秒并返回 `TickResult`；显式降雨在步进开始时供水，默认无雨；正常成熟与换季内部促熟走原入库，再让三名工人各推进完整一秒；之后推进日期、清理其余禁生作物、更新缓存计划事件并检查委托。`WorkerActed` 仅表示实际播种或供水，移动不计；暂停不执行任何经营相位 |
| 交易 | Buy、Sell、SellCommodityAll、SellRaw、SellAll | `Buy/Sell(CommodityId, int quantity)` 按当前报价买卖指定数量；`SellCommodityAll` 卖出单商品全部，旧 `SellRaw` 卖出该种原料，`SellAll` 一次卖出全部成品。统一委托完整交易检查，失败零修改；空库存全售成功返回零，显式零数量交易拒绝。原料底线不限制主动出售，不出售已投入物 |
| 只读状态 | MoneyCents、BuildingCostCents、CurrentDay、CurrentFlourPriceCents、DailyPriceChangePercent | 余额由 `Wallet` 持有并委托查询，金额以分保存；`BuildingCostCents` 保留原生产建筑 1000 分常量，按类型查询统一用 `GetBuildingCostCents`；`CurrentDay` 从日历已过天数换算，`CurrentFlourPriceCents` 为当前面粉报价，兼容名 `DailyPriceChangePercent` 为本次面粉相对上次报价的实际涨跌，不表示每天变化 |

地图为 384×384 基础格。农田与加工场地各占 3×3，每座一份生产状态；道路为 1×1。`GetPlot` 从 `LandOccupancy`、`FarmingSystem`、`ProcessingSystem` 聚合 `PlotSnapshot`，不泄露可变内部状态；空地与道路快照的作物字段没有经营含义；道路显式返回 `BuildingKind.Road`、零剩余秒与无水，不查询农田或加工状态。`TryGetPlot` 返回 `None` 或 `OutOfBounds`，无效时输出默认快照；既有 `GetPlot` 保留“调用方已确认有效格”的便利形式，越界抛 `ArgumentOutOfRangeException`。WorldMap 按 `GetBuildingSpaces` 遍历空间，在锚点调用 `GetPlot` 获取一次外观并覆盖整个 footprint；它不能驱动 AdvanceTick，也不修改库存。Main 接收玩家操作、调用经营命令并在状态变化后通知地图同步。

`FarmDetailsSnapshot` 与 `ProcessorDetailsSnapshot` 是只读值，包含作物定义、稳定状态原因、对应当日售价和库存。`Main` 先用 `GetPlot` 分派详情面板，再向对应查询索取语义快照；UI 只负责中文文案和控件更新。雨后空田由 `WaitingForWorkerWithWater` 表示；计划空白或本条已执行的空田显示 `Resting`；空田的 `WrongSeason` 与可播风险 `InsufficientTime` 直接复用[PlantingRules](../../farming/planting-rules/interface-planting-rules.md)的判断。越季失败清理后立即查询当前空田原因，不增加历史失败状态或工人清理动作；清理不操作库存和钱包。内部测试构造器可指定初始累计秒，公开游戏构造器从零时刻开始。

加工详情复用 `ProcessingSystem.GetStatus` 的只读状态：进行中优先；空闲时由库存模块给出没有原料、受底线限制或可领取的原因。设置降低底线后，下一领取阶段之前可返回 `ReadyToProcess`，查询不会开始加工或扣库存。

经营开局固定创建三名工人，分别位于 `(190,190)`、`(193,190)`、`(196,190)`；出生位置从三座农田锚点经 `BuildingFootprint.WorkCell` 派生，不参与开局随机抽取。`FarmGame` 在原工人相位只调用 `WorkerScheduler.AdvanceOneSecond`，以 `GetWorkers` 包装其快照，不读取轮转游标、认领关系或选择策略。任务选择、移动与完整动作计时、失效重验由[工人调度 Module](../../workers/worker-scheduler/interface-worker-scheduler.md)负责；地图读取经营位置做表现，动画完成不会触发农田操作。

`FarmGame` 仍负责推进、建造与交易的业务顺序。建造通过 `PlacementRules` 检查后交 `Wallet` 扣款，占用与生产状态在同一调用内创建或清理；加工场地建成后立即按旧规则领取原料；道路只写 `LandOccupancy`，不创建生产状态、不触发领取，拆除只释放占用且不退款。收获和加工品进入 `Inventory`，买卖委托[TradingService](../../trading/trading-service/interface-trading-service.md)先检查数量、资金、库存与整数容量，再完整提交。买入原料不主动领取；下一经营领取阶段或随后新建场地的现有即时路径使用它。暂停时可交易但不推进行情或生产。生产建筑使用无效作物时，建造与选种命令返回失败原因，不修改地块和余额；道路忽略无生产含义的作物参数。`HasConsistentState` 是测试用内部检查，用于核对完整子格映射、稳定空间实例顺序和只有锚点持有对应生产状态。

完整交易返回 `TradeResult`（数量和累计金额为 `long`）。旧出售入口返回补充 `Failure`、`Success`、`ErrorMessage` 的 `SaleResult`，原数量和收入仍为 `int`；容量失败时返回零，不抛出溢出异常。无效商品在交易命令中正常拒绝，只读查询中的无效标识属于调用错误。正式行情由 `MarketQuotes` 唯一维护，历史 `MarketPriceCurve` 不参与经营；内部初始时间夹具按对应经过日重放行情至一致状态。

一次委托在创建时冻结相应资源；固定预算买单在执行时求出完整整数数量，限价数量买单按最高价冻结含费上限。一次目标数量在建单时确定，只编辑价格、条件或现金保留设置时保持原锁量；主动改变商品、方向或数量设置等数量意图时才重新完整检查。持续目标每秒读取实际总库存重新算差额。原现金基准与创建顺序在编辑时保持，修改保留参数仍使用原基准；不使用取消重建实现编辑。实际委托结算在日历与行情推进之后执行，后单读取前单执行后的资源，失败等待，暂停可管理单据但不自动执行。委托买入原料仍不追加本秒领取阶段，下一经营领取或新建场地既有路径使用它。

时间顺序见[实现](implementation-tick-order.md)；开局布局见[实现](implementation-opening-layout.md)。玩家可观察的生产、交易、土地规则分别以 docs/gameplay/ 下的专题文档为准。

## 等价批量推进

`AdvanceTicks(uint maxTicks, Func<SimulationCheckpoint, bool>? checkpoint = null)` 推进没有外部降雨输入的区间。暂停或零请求返回零且不调用检查点；未暂停时先核对完整请求是否超过 `uint32` 日历容量，超过则抛出 `InvalidOperationException` 且资源与时间零修改。调用方有显式雨输入时，先推进雨 tick 之前的无雨区间，再调用原 `AdvanceTick(true)`，随后继续无雨区间，不预报或补造天气。

`SimulationAdvanceResult` 给出实际 `AdvancedTicks`、`long Harvested/Produced`、是否发生工人动作/换日、`StoppedAtCheckpoint`，以及执行成本 `QuietTicks/EventTicks`。平静秒数一次累计；事件秒数按完整相位结算。两种计数之和等于实际推进数，密集持续成交仍逐秒逐笔执行。

每个事件结算完成后及请求终点调用检查点。`SimulationCheckpoint.AdvancedTicks` 是本次请求累计秒数，`IntervalTicks` 是上个检查点以来的秒数，`ElapsedSeconds` 是日历绝对秒数，`Result` 汇总本段结果，`IsEvent` 表示终点是否执行完整事件相位。回调返回 `true` 继续、`false` 停止；宿主可在稳定点提交正式命令，但修改后必须返回 `false`，再按新状态、倍率、日期与预算重算下一请求；不能在回调嵌套推进经营。流程首次达到产品数量的事件秒可直接停下建单，订单随后经营秒才执行。

模块职责、整数口径和对照验证见[批量实现](implementation-batched-simulation.md)。

满地图夹具以三格间距铺设锚点 `(0,0)` 到 `(381,381)`，按标准列奇偶交替农田/加工，保留 8,192+8,192 个生产实例与 147,456 个占用子格。生产、雨水与领取均按空间实例各处理一次。专用人物表现夹具将三座中心右上方农田改为空田并真实推进一秒，以采样移动；普通负载夹具不受影响。

## 逐实例成功结果与精确生长比例（#105）

`PlotSnapshot.GrowthProgress` 是农田实际生长精确单位除以本轮总单位所得的 0～1 比例；非生长状态为零。它与 RemainingSeconds 的向上取整剩余秒分别供表现选档和玩家时间展示，不从取整秒反推生长档。

`GetPresentationResults()` 返回最近完整经营秒的独立只读 `IReadOnlyList<ProductionResult>`，字段为 ElapsedSeconds、AnchorCell、CropKind、Kind、Quantity、WorkerNumber。Kind 为 Sow、Water、Harvest、Product；播种/浇水只从实际成功回调记录，自动产出从实际入库路径记录，换季促熟共用 Harvest，工人动作 Quantity=0、自动产出 WorkerNumber=0。完成后立刻领取下一加工批次仍保留本秒 Product。

`IsPresentationResultCurrent(result)` 是开始播放的完整校验：结果须属于本局当前秒及当前保留集合，工人动作还须通过农田原工作凭据版本检查。`IsPresentationTargetCurrent(result)` 单独验证已经开始的片段：内部保存 LandOccupancy 已有的不可变 BuildingSpaceSnapshot 引用，以引用一致性确认原设施仍在，并校验农田原工作凭据；它不限制秒数，让公共倍率的片段自然完成。拆除同格重建产生新引用，不使旧结果复活；改种/重启/换季等通过 FarmingSystem 失效原动作，不清空其他实例结果。没有新增永久 ID、事件历史或第二份土地状态，调用方不能修改集合或内部凭据。

每个新事件秒先覆盖结果，平静秒也清空；批量请求结束仅留下最后经营秒，不是整个请求的历史。初次表现挂接记录当前秒并跳过已有结果，之后按经营秒去重；开发高倍率跳过过期动作不排队。暂停不推进经营或产生结果，表现冻结并在恢复时继续有效当前片段。此 Seam 只供人物和设施表现读取，不承担存档、审计或全局事件总线。测试为 TestProductionResults 与 TestWorkerPresentation。
