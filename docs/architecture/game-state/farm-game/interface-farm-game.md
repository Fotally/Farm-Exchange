# FarmGame 对外接口

对应类型：FarmExchange.Gameplay.FarmGame，代码位于 scripts/gameplay/FarmGame.cs。调用方为主场景和地图；测试也通过该公开接口验证行为。当前只有一个实现，未声明 C# interface 类型。

| 用途 | 公开成员 | 调用方需要知道的约定 |
| --- | --- | --- |
| 定义 | Crops、GetCrop、GetBuildingCostCents(building) | 作物定义委托 `CropCatalog`；七种作物的规则来自[作物表](../../../gameplay/production/crop-growth.md)。静态建造费查询为唯一类型价格入口：农田/加工场地 1000 分，道路 100 分；`None` 或未知类型抛 `ArgumentOutOfRangeException` |
| 状态查询 | GetPlot、TryGetPlot、GetStock、GetRawStock、GetRawReserve、GetProductStock、GetRawPriceCents、GetProductPriceCents | 返回指定格的只读快照、统一或分类公共库存、原料保留底线和两类当前报价；农田地块快照含 `HasWater`，`TryGetPlot` 对地图外返回 `OutOfBounds` |
| 行情查询 | GetQuote(CommodityId)、GetMarketSnapshot() | 委托 `MarketQuotes` 返回十四商品当前价、上次报价及实际涨跌；完整快照还含本次与下次实际报价日期和已公布消息。查询不推进时间或抽随机 |
| 工人查询 | GetWorkers() | 按编号 1、2、3 返回独立只读 `WorkerSnapshot` 集合，包含分数格位置、可空目标格与空闲/移动/播种/浇水状态；查询不分配任务或推进时间 |
| 详情查询 | GetFarmDetails、GetProcessorDetails | 分别返回现有农田的等待工人、已湿润待播种、待水、生长中、不适季或剩余时间不足状态，或加工场地的无原料等待、保留底线限制、待领取原料或加工中状态，同时附带对应周期、当日售价和公共库存；两类详情只服务农田/加工场地，类型不匹配属于调用错误 |
| 原料保留设置 | SetRawReserve(crop, quantity) | 逐种设置非负整数底线，默认 0，允许高于现存库存；返回 `RawReserveFailure.None`、`InvalidCrop` 或 `InvalidQuantity`，失败零修改。设置不触发领取；下次经营领取阶段或新建场地的现有即时领取路径检查新值 |
| 放置 | CheckPlacement、TryPlace | 共用[放置规则](../../land/placement-rules/interface-placement-rules.md)；预检只读，执行时重新检查并返回实际扣费或稳定失败原因；道路使用 `BuildingKind.Road`，作物参数不参与道路语义。非法建筑描述先正常返回 `InvalidBuilding`，不会调用价格查询抛异常 |
| 旧命令与编辑 | BuildFarm、BuildProcessor、SetFarmCrop、RemoveBuilding | 旧建造命令转发 `TryPlace`；成功返回 null，失败返回给玩家的原因；选种和拆除对地图外都返回“地图外地块” |
| 时间与降雨 | AdvanceTick(bool isRaining = false)、Calendar、IsPaused、SetPaused | 每次未暂停步进推进一模拟秒并返回 `TickResult`；显式降雨在步进开始时供水，默认无雨；生产后让三名工人各推进完整一秒，`WorkerActed` 只表示本秒至少一人完成播种或供水，单纯移动为 false；之后推进日期，跨季时自动清理禁生作物；`Calendar` 为只读日期快照，暂停期间不推进降雨、生产、工人、日期或行情 |
| 交易 | Buy、Sell、SellCommodityAll、SellRaw、SellAll | `Buy/Sell(CommodityId, int quantity)` 按当前报价买卖指定数量；`SellCommodityAll` 卖出单商品全部，旧 `SellRaw` 卖出该种原料，`SellAll` 一次卖出全部成品。统一委托完整交易检查，失败零修改；空库存全售成功返回零，显式零数量交易拒绝。原料底线不限制主动出售，不出售已投入物 |
| 只读状态 | MoneyCents、BuildingCostCents、CurrentDay、CurrentFlourPriceCents、DailyPriceChangePercent | 余额由 `Wallet` 持有并委托查询，金额以分保存；`BuildingCostCents` 保留原生产建筑 1000 分常量，按类型查询统一用 `GetBuildingCostCents`；`CurrentDay` 从日历已过天数换算，`CurrentFlourPriceCents` 为当前面粉报价，兼容名 `DailyPriceChangePercent` 为本次面粉相对上次报价的实际涨跌，不表示每天变化 |

GetPlot 从 `LandOccupancy`、`FarmingSystem`、`ProcessingSystem` 聚合 `PlotSnapshot`，不泄露可变内部状态；空地与道路快照的作物字段没有经营含义；道路显式返回 `BuildingKind.Road`、零剩余秒与无水，不查询农田或加工状态。`TryGetPlot` 返回 `None` 或 `OutOfBounds`，无效时输出默认快照；既有 `GetPlot` 保留“调用方已确认有效格”的便利形式，越界抛 `ArgumentOutOfRangeException`。WorldMap 只对有效格调用 GetPlot 获取外观；它不能驱动 AdvanceTick，也不修改库存。Main 接收玩家操作、调用经营命令并在状态变化后通知地图同步。

`FarmDetailsSnapshot` 与 `ProcessorDetailsSnapshot` 是只读值，包含作物定义、稳定状态原因、对应当日售价和库存。`Main` 先用 `GetPlot` 分派详情面板，再向对应查询索取语义快照；UI 只负责中文文案和控件更新。雨后空田由 `WaitingForWorkerWithWater` 表示；空田的 `WrongSeason` 与 `InsufficientTime` 直接复用[PlantingRules](../../farming/planting-rules/interface-planting-rules.md)的判断。越季失败清理后立即查询当前空田原因，不增加历史失败状态或工人清理动作；清理不操作库存和钱包。内部测试构造器可指定初始累计秒，公开游戏构造器从零时刻开始。

加工详情复用 `ProcessingSystem.GetStatus` 的只读状态：进行中优先；空闲时由库存模块给出没有原料、受底线限制或可领取的原因。设置降低底线后，下一领取阶段之前可返回 `ReadyToProcess`，查询不会开始加工或扣库存。

经营开局固定创建三名工人，分别位于 `(63,63)`、`(64,63)`、`(65,63)`；工人构造与开局建筑共用这三个中心坐标，不参与开局随机抽取。`FarmGame` 在原工人相位只调用 `WorkerScheduler.AdvanceOneSecond`，以 `GetWorkers` 包装其快照，不读取轮转游标、认领关系或选择策略。任务选择、移动与完整动作计时、失效重验由[工人调度 Module](../../workers/worker-scheduler/interface-worker-scheduler.md)负责；地图读取经营位置做表现，动画完成不会触发农田操作。

`FarmGame` 仍负责推进、建造与交易的业务顺序。建造通过 `PlacementRules` 检查后交 `Wallet` 扣款，占用与生产状态在同一调用内创建或清理；加工场地建成后立即按旧规则领取原料；道路只写 `LandOccupancy`，不创建生产状态、不触发领取，拆除只释放占用且不退款。收获和加工品进入 `Inventory`，买卖委托[TradingService](../../trading/trading-service/interface-trading-service.md)先检查数量、资金、库存与整数容量，再完整提交。买入原料不主动领取；下一经营领取阶段或随后新建场地的现有即时路径使用它。暂停时可交易但不推进行情或生产。生产建筑使用无效作物时，建造与选种命令返回失败原因，不修改地块和余额；道路忽略无生产含义的作物参数。`HasConsistentState` 是测试用内部检查，用于核对每格占用与对应生产状态恰好一致。

完整交易返回 `TradeResult`（数量和累计金额为 `long`）。旧出售入口返回补充 `Failure`、`Success`、`ErrorMessage` 的 `SaleResult`，原数量和收入仍为 `int`；容量失败时返回零，不抛出溢出异常。无效商品在交易命令中正常拒绝，只读查询中的无效标识属于调用错误。正式行情由 `MarketQuotes` 唯一维护，历史 `MarketPriceCurve` 不参与经营；内部初始时间夹具按对应经过日重放行情至一致状态。

时间顺序见[实现](implementation-tick-order.md)；开局布局见[实现](implementation-opening-layout.md)。玩家可观察的生产、交易、土地规则分别以 docs/gameplay/ 下的专题文档为准。
