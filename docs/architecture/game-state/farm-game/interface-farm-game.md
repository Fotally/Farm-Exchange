# FarmGame 对外接口

## 不可变地形与建局

默认含水局使用 `await FarmGame.CreateAsync(tree, ..., terrainRules=null, cancellationToken=default)`。工厂每次重新读取正式 JSON，完整生成地形并等待 Godot 原生导航同步后，才组装工人、开局设施与日志身份。同步构造器仅接受显式纯陆地配置，调用同一地形 Module 的立即完成分支；不能同步阻塞含水准备。`LoadTerrainRules(source)` 与 `ParseTerrainRules(json, source)` 共用真实 384×384 地图、开局设施完整占地和三名工人起点验证。外部解析配置仍重验尺寸和真实上下文，失败或取消不发布半成品局，不替换地图。宿主退出取消等待；组装前再次检查取消，已准备的地形立即释放。`Dispose()` 关闭本局日志并释放本局地形的原生导航资源。`Terrain` 原样提供不可变权威地形；独立地形随机不消耗开局作物序列。

`IsNavigationReady` 表示本局最新成功建拆的道路成本已完成原生同步。成功单格道路放置、整段放置、通用单格移除道路或整段拆路都会立即使其为 `false`；生产设施变化、只读预检及失败道路命令不改变它。`await WaitForNavigationAsync(tree, cancellationToken)` 使用土地唯一拥有的稳定只读快照，合并同一局实际准备任务；准备期间新增建拆会在旧轮完成后继续处理最新快照，直到提交版本追平才发布就绪。五材质成本相同，道路 1、普通陆地 2；不修改工人速度、既有不可变路线或认领。

`AdvanceTick`／`AdvanceTicks` 要求导航已就绪，未准备时在推进、日志相位和资源修改前抛出 `InvalidOperationException`，同步入口不会阻塞等物理帧。若完整经营检查点内成功建拆道路，即使回调要求继续，也在该已完成检查点返回停止，由宿主先等待准备。`WaitForNavigationAsync` 的调用令牌只取消该等待者，不中断其他宿主正在共用的局准备；`Dispose` 取消本局实际准备，再释放地形。准备失败持续传播该任务异常，由拥有者结束局，不回退旧地图或直线路线。首次铺路的显式纯陆地局同样需要真实 SceneTree 和原生准备；尚无道路且没有导航资源的纯陆地局仍可同步创建。

日志观察可通过本局 `Log.Dispose()` 单独结束，仍存活的局可以继续经营并保持观察静默。`FarmGame.Dispose()` 同时释放地形导航资源，完整释放后不再推进或提交经营命令；日志生命周期夹具先结束观察，最后由局的拥有者释放完整局。

普通建造、免费开局及整段道路均要求占地所有格四角无水；含水岸格也拒绝，稳定原因 `LandFailure.Water`，费用与资源保持零修改。`FillWorldForBenchmark` 只接受建局时明确选择合法纯陆地配置、导航已就绪且没有道路的局，在修改任何状态前检查，不重新生成或替换地形。含道路的局须先通过正式拆路命令清空道路并等待同步，不能直接清占用留下旧导航成本；前置拒绝保持全部状态。满图仍是 16,384 生产实例。

`IsWorkerMovementCurrent(movement)` 只读重验实际路线记录的目标农田、原始完整工作凭据和原动作，使表现能在改种、拆除或雨水满足任务之后立即停止旧路线；到达秒仍有效，不要求快照活动继续为 Moving。该凭据原样来自 FarmingSystem，不在经营或表现模块重新拼接轮次与播种许可；禁播再启用不复活旧播种行程，同轮供水仍按真实需求保留。既有单秒／批量及生产结果回归显式使用纯陆地配置，继续覆盖新合入的工作身份和异常检查点修复。

`DeleteCultivationPlan(id)` 删除共享配置并解除全部引用田的计划安排：保留各田当前作物、当前轮阶段、精确剩余时长和水分，恢复按当前作物自动复种，仍经工人和季节判断。成功返回 `null`；未知或已删除编号返回“耕作表不存在”且零修改。表编号不回收；不改变其他表、其他农田、手动预备、金币库存或日历，具体约定见[计划 Interface](../../cultivation/interface-cultivation-plan-book.md)。

对应类型：FarmExchange.Gameplay.FarmGame，代码位于 scripts/gameplay/FarmGame.cs。调用方为主场景和地图；测试也通过该公开接口验证行为。当前只有一个实现，未声明 C# interface 类型。

`FarmGame(seed?, logging?, purpose?, weatherMode=Natural, weatherRules=null)` 默认不采集日志；显式传入 `RuntimeLog` 时通过内部自动绑定在真实开局完成后建立局身份、初始化基线与生产观察。`purpose` 默认为 `GamePurpose.Main`，独立开发流程指定 `ScenarioIndependent`；非法用途在初始化前抛参数异常。天气配置在此建局组装点选择：省略时采用正式 `WeatherRules.Default`，开发／测试可传入 `WeatherRules.Parse` 返回的已验证不可变配置；天气执行器只接收该配置，局创建后不替换规则。只读 `Log` 返回已绑定上下文或 null，不允许重新安装。公开 `RuntimeLog.BindGame` 仍只用于手动领域观察，不自动采集已有局的生产。

主动交易、订单、建拆、原料底线、六个耕作命令和暂停设置关联原指令、真实结果和一次结束，相关命令均接受可选 `origin=Player`。经营语义来源 `FarmExchange.Gameplay.CommandOrigin` 只有 Player/Scenario，其他值在记录与业务提交前抛出 `ArgumentOutOfRangeException(nameof(origin))`，资源零修改且不依赖采集开关。开发流程真实命令显式传 Scenario；自动订单成交、实际等待、行情与生产由所属领域观察，不伪造玩家命令。经营层保留原业务执行权和检查顺序，不持有日志字段或前后投影，日志不执行业务回调。输出失败不改业务结果、不重试命令，业务异常原样传播。`Dispose()` 幂等结束本局关联，先封存订单等待与生产尾段，保留金币、库存和生产数据，再释放地形导航资源；开发宿主先终结流程和报告，再释放独立局，最后由 Main 关闭主局和会话，具体约定见[日志 Interface](../../logging/interface-logging.md)。

| 用途 | 公开成员 | 调用方需要知道的约定 |
| --- | --- | --- |
| 定义 | Crops、GetCrop、GetBuildingCostCents(building) | 作物定义委托 `CropCatalog`；七种作物的规则来自[作物表](../../../gameplay/production/crop-growth.md)。静态建造费查询为唯一类型价格入口：农田/加工场地 1000 分，道路 100 分；`None` 或未知类型抛 `ArgumentOutOfRangeException` |
| 状态查询 | GetPlot、TryGetPlot、GetStock、GetRawStock、GetRawReserve、GetProductStock、GetRawPriceCents、GetProductPriceCents | 任意占用子格解析到锚点，返回同一整座设施的只读生产快照、统一或分类公共库存、原料保留底线和两类当前报价；农田地块快照含 `HasWater`，`TryGetPlot` 对地图外返回 `OutOfBounds` |
| 空间查询 | GetBuildingSpace(cell)、GetBuildingSpaces() | 任意子格返回同一只读空间，空地或地图外返回空；全空间列表为锚点索引升序的独立只读缓存，包含锚点、类型、登记朝向 `Orientation`、固定 footprint 与工作中心；朝向建成后不可修改 |
| 行情查询 | GetQuote(CommodityId)、GetMarketSnapshot() | 委托 `MarketQuotes` 返回十四商品当前价、上次报价及实际涨跌；完整快照还含本次与下次实际报价日期和已公布消息。查询不推进时间或抽随机 |
| 可用与冻结资源 | AvailableMoneyCents、FrozenMoneyCents、GetAvailableStock、GetFrozenStock | 钱包及库存总量仍包含冻结部分，可用查询扣除一次挂单冻结；建造、加工与即时交易只能使用可用部分，查询不释放冻结 |
| 委托管理 | CreateTradeOrder、UpdateTradeOrder、CancelTradeOrder、SetTradeOrderEnabled、GetTradeOrders | 完整创建或同 ID 编辑，失败单据和资源零修改；取消释放冻结，仅持续策略可启停；独立只读快照按建单顺序保留活动及终态，包含原现金基准、保留线、冻结归属、等待原因和最近成交的实际商品、方向、费用与余额，编辑不会用新配置改写旧成交，规则见[委托玩法](../../../gameplay/trading/orders.md) |
| 工人查询 | GetWorkers() | 按编号 1、2、3 返回独立只读 `WorkerSnapshot` 集合，包含分数格位置、可空目标格与空闲/移动/播种/浇水状态；查询不分配任务或推进时间 |
| 详情查询 | GetFarmDetails、GetProcessorDetails | 分别返回现有农田的等待工人、已湿润待播种、待水、生长中、不适季或剩余时间不足状态，或加工场地的无原料等待、保留底线限制、待领取原料或加工中状态，同时附带对应周期、当日售价和公共库存；两类详情只服务农田/加工场地，类型不匹配属于调用错误 |
| 原料保留设置 | SetRawReserve(crop, quantity) | 逐种设置非负整数底线，默认 0，允许高于现存库存；返回 `RawReserveFailure.None`、`InvalidCrop` 或 `InvalidQuantity`，失败零修改。设置不触发领取；下次经营领取阶段或新建场地的现有即时领取路径检查新值 |
| 放置 | CheckPlacement、TryPlace | 共用[放置规则](../../land/placement-rules/interface-placement-rules.md)；生产建筑完整 3×3、道路 1×1 的几何与余额预检只读，执行时重新检查并返回实际扣费或稳定失败原因。`CheckPlacement` 的第4参数为可选 `orientation=Q0`；`TryPlace` 保留第4参数 `origin=Player`，第5参数为可选 `orientation=Q0`，玩家朝向可用命名参数传入。生产设施允许 q0～q3，道路只允许 q0且作物参数不参与道路语义；非法朝向正常返回 `InvalidOrientation`，失败资源和空间零修改。非法建筑描述先正常返回 `InvalidBuilding`，不会调用价格查询抛异常 |
| 旧命令与编辑 | BuildFarm、BuildProcessor、SetFarmCrop、PrepareFarmCrop、RemoveBuilding | 旧建造命令转发 `TryPlace`；成功返回 null，失败返回给玩家的原因；立即改种强制丢弃当前轮并保水，预备下一轮保留当前轮，两者都只解除本田耕作表；任一占用子格操作同一整座设施；拆除释放全部 footprint、计划引用且不退款；对地图外都返回“地图外地块” |
| 年度耕作表 | CheckCultivationEntries、CheckCultivationPlan、CreateCultivationPlan、UpdateCultivationPlan、DeleteCultivationPlan、ApplyCultivationPlan、GetCultivationPlans、GetFarmCultivation | 条目检查允许未命名草稿编辑；完整提交检查及创建/更新仍要求名称和有效模式，复用同一年度排程判断。快照含每表生命周期下一条编号 `NextEntryId`，保存空表不回退；更新拒绝新条复用历史编号，移动原条保留年度凭据。批量重验全列表且子格去重，失败全部不修改。应用/编辑保留当前轮；计划收获先关闭播种并缓存目标，日历推进后才重验启用，不能借旧季工人相位提前执行。工人完成后、换季清理前先登记实际计划播种，季末播后即清除也计为该条已执行。快照分开计划日期和真实生产，规则与缓存约定见[计划 Interface](../../cultivation/interface-cultivation-plan-book.md) |
| 选种风险 | GetPlantingCheck(cell,crop) | 只读查询 `PlantingFailure`；`InsufficientTime` 是可播种风险提示，`WrongSeason` 才禁止实际播种。手动目标仍可选任一合法作物，不通过查询执行或清理 |
| 时间与降雨 | AdvanceTick()、Calendar、IsPaused、SetPaused | 每次未暂停步进推进一模拟秒并返回 `TickResult`；读取唯一地区天气，整田降雨在步进开始时供水；正常成熟与换季内部促熟走原入库，再让三名工人各推进完整一秒；之后推进日期、清理其余禁生作物、更新缓存计划事件并检查委托。`WorkerActed` 仅表示实际播种或供水，移动不计；暂停不执行任何经营相位 |
| 交易 | Buy、Sell、SellCommodityAll、SellRaw、SellAll | `Buy/Sell(CommodityId, int quantity)` 按当前报价买卖指定数量；`SellCommodityAll` 卖出单商品全部，旧 `SellRaw` 卖出该种原料，`SellAll` 一次卖出全部成品。统一委托完整交易检查，失败零修改；空库存全售成功返回零，显式零数量交易拒绝。原料底线不限制主动出售，不出售已投入物 |
| 只读状态 | MoneyCents、BuildingCostCents、CurrentDay、CurrentFlourPriceCents、DailyPriceChangePercent | 余额由 `Wallet` 持有并委托查询，金额以分保存；`BuildingCostCents` 保留原生产建筑 1000 分常量，按类型查询统一用 `GetBuildingCostCents`；`CurrentDay` 从日历已过天数换算，`CurrentFlourPriceCents` 为当前面粉报价，兼容名 `DailyPriceChangePercent` 为本次面粉相对上次报价的实际涨跌，不表示每天变化 |

地图为 384×384 基础格。农田与加工场地各占 3×3，每座一份生产状态；道路为 1×1。`GetPlot` 从 `LandOccupancy`、`FarmingSystem`、`ProcessingSystem` 聚合 `PlotSnapshot`，不泄露可变内部状态；空地与道路快照的作物字段没有经营含义；道路显式返回 `BuildingKind.Road`、零剩余秒与无水，不查询农田或加工状态。`TryGetPlot` 返回 `None` 或 `OutOfBounds`，无效时输出默认快照；既有 `GetPlot` 保留“调用方已确认有效格”的便利形式，越界抛 `ArgumentOutOfRangeException`。WorldMap 按 `GetBuildingSpaces` 遍历空间，在锚点调用 `GetPlot` 获取一次外观并覆盖整个 footprint；它不能驱动 AdvanceTick，也不修改库存。Main 接收玩家操作、调用经营命令并在状态变化后通知地图同步。

`FarmDetailsSnapshot` 与 `ProcessorDetailsSnapshot` 是只读值，包含作物定义、稳定状态原因、对应当日售价和库存。`Main` 先用 `GetPlot` 分派详情面板，再向对应查询索取语义快照；UI 只负责中文文案和控件更新。雨后空田由 `WaitingForWorkerWithWater` 表示；计划空白或本条已执行的空田显示 `Resting`；空田的 `WrongSeason` 与可播风险 `InsufficientTime` 直接复用[PlantingRules](../../farming/planting-rules/interface-planting-rules.md)的判断。越季失败清理后立即查询当前空田原因，不增加历史失败状态或工人清理动作；清理不操作库存和钱包。内部测试构造器可指定初始累计秒，公开游戏构造器从零时刻开始。

`GetCultivationPlan(id)` 返回指定表的独立只读快照，不存在时为 null；它与 `GetCultivationPlans()` 共用计划模块的正式数据。`CreateCultivationPlan/UpdateCultivationPlan/DeleteCultivationPlan/ApplyCultivationPlan/SetFarmCrop/PrepareFarmCrop` 在真实执行前后交由 CultivationLog 观察。批量应用保留原先的表存在、空列表、逐子格有效性和整批提交顺序；只有完整解析完成才提供实际去重目标，提前拒绝不为日志补跑后续校验，实际已检查空列表时提供已知零目标。日志采样不限制合法业务集合。`SetPaused(paused, origin)` 重复设置也形成一对命令记录，但仅实际改变暂停状态时产生 PauseChanged，未完成经营秒仍由驱动保留。

加工详情复用 `ProcessingSystem.GetStatus` 的只读状态：进行中优先；空闲时由库存模块给出没有原料、受底线限制或可领取的原因。设置降低底线后，下一领取阶段之前可返回 `ReadyToProcess`，查询不会开始加工或扣库存。

新建朝向由 `LandOccupancy` 唯一登记，`FarmGame` 不另持一份方向状态，也不提供建成后旋转命令。开局、旧建造调用和满图夹具默认 q0；暂停不阻止建造。旋转没有额外费用，当前 3×3 四向不改变占地、工作中心、工人任务或生产计算。`TestFarmGame` 对各 q 比较完整播种、浇水、收获和加工过程的经营结果、库存、资金与工人快照，并检查朝向预检/执行的拒绝一致性。

经营开局固定创建三名工人，分别位于 `(190,190)`、`(193,190)`、`(196,190)`；出生位置从三座农田锚点经 `BuildingFootprint.WorkCell` 派生，不参与开局随机抽取。`FarmGame` 在原工人相位只调用 `WorkerScheduler.AdvanceOneSecond`，以 `GetWorkers` 包装其快照，不读取轮转游标、认领关系或选择策略。任务选择、移动与完整动作计时、失效重验由[工人调度 Module](../../workers/worker-scheduler/interface-worker-scheduler.md)负责；地图读取经营位置做表现，动画完成不会触发农田操作。

`FarmGame` 仍负责推进、建造与交易的业务顺序。建造通过 `PlacementRules` 检查后交 `Wallet` 扣款，占用与生产状态在同一调用内创建或清理；加工场地建成后立即按旧规则领取原料；道路只写 `LandOccupancy`，不创建生产状态、不触发领取，拆除只释放占用且不退款。收获和加工品进入 `Inventory`，买卖委托[TradingService](../../trading/trading-service/interface-trading-service.md)先检查数量、资金、库存与整数容量，再完整提交。买入原料不主动领取；下一经营领取阶段或随后新建场地的现有即时路径使用它。暂停时可交易但不推进行情或生产。生产建筑使用无效作物时，建造与选种命令返回失败原因，不修改地块和余额；道路忽略无生产含义的作物参数。`HasConsistentState` 是测试用内部检查，用于核对完整子格映射、稳定空间实例顺序和只有锚点持有对应生产状态。

完整交易返回 `TradeResult`（数量和累计金额为 `long`）。旧出售入口返回补充 `Failure`、`Success`、`ErrorMessage` 的 `SaleResult`，原数量和收入仍为 `int`；容量失败时返回零，不抛出溢出异常。无效商品在交易命令中正常拒绝，只读查询中的无效标识属于调用错误。正式行情由 `MarketQuotes` 唯一维护，历史 `MarketPriceCurve` 不参与经营；内部初始时间夹具按对应经过日重放行情至一致状态。

一次委托在创建时冻结相应资源；固定预算买单在执行时求出完整整数数量，限价数量买单按最高价冻结含费上限。一次目标数量在建单时确定，只编辑价格、条件或现金保留设置时保持原锁量；主动改变商品、方向或数量设置等数量意图时才重新完整检查。持续目标每秒读取实际总库存重新算差额。原现金基准与创建顺序在编辑时保持，修改保留参数仍使用原基准；不使用取消重建实现编辑。实际委托结算在日历与行情推进之后执行，后单读取前单执行后的资源，失败等待，暂停可管理单据但不自动执行。委托买入原料仍不追加本秒领取阶段，下一经营领取或新建场地既有路径使用它。

时间顺序见[实现](implementation-tick-order.md)；开局布局见[实现](implementation-opening-layout.md)。玩家可观察的生产、交易、土地规则分别以 docs/gameplay/ 下的专题文档为准。

## 等价批量推进

`AdvanceTicks(uint maxTicks, Func<SimulationCheckpoint, bool>? checkpoint = null)` 推进没有外部输入的区间。暂停或零请求返回零且不调用检查点；未暂停时先核对完整请求是否超过 `uint32` 日历容量，超过则抛出 `InvalidOperationException` 且资源与时间零修改。单秒和批量读取同一当前天气；自然模式以预排开始／结束事件分隔区间，显式模式由开发宿主在请求间设置或清除区域。没有第二份全局降雨布尔状态。

`SimulationAdvanceResult` 给出实际 `AdvancedTicks`、`long Harvested/Produced`、是否发生工人动作/换日、`StoppedAtCheckpoint`，以及执行成本 `QuietTicks/EventTicks`。平静秒数一次累计；事件秒数按完整相位结算。两种计数之和等于实际推进数，密集持续成交仍逐秒逐笔执行。

每个事件结算完成后及请求终点调用检查点。`SimulationCheckpoint.AdvancedTicks` 是本次请求累计秒数，`IntervalTicks` 是上个检查点以来的秒数，`ElapsedSeconds` 是日历绝对秒数，`Result` 汇总本段结果，`IsEvent` 表示终点是否执行完整事件相位。回调返回 `true` 继续、`false` 停止；宿主可在稳定点提交正式命令，但修改后必须返回 `false`，再按新状态、倍率、日期与预算重算下一请求；不能在回调嵌套推进经营。流程首次达到产品数量的事件秒可直接停下建单，订单随后经营秒才执行。

模块职责、整数口径和对照验证见[批量实现](implementation-batched-simulation.md)。

满地图夹具以三格间距铺设锚点 `(0,0)` 到 `(381,381)`，按标准列奇偶交替农田/加工，保留 8,192+8,192 个生产实例与 147,456 个占用子格。生产、雨水与领取均按空间实例各处理一次。专用人物表现夹具将三座中心右上方农田改为空田并真实推进一秒，以采样移动；普通负载夹具不受影响。

## 逐实例成功结果与精确生长比例

`PlotSnapshot.GrowthProgress` 是农田实际生长精确单位除以本轮总单位所得的 0～1 比例；非生长状态为零。它与 RemainingSeconds 的向上取整剩余秒分别供表现选档和玩家时间展示，不从取整秒反推生长档。

`GetPresentationResults()` 返回最近完整经营秒的独立只读 `IReadOnlyList<ProductionResult>`，字段为 ElapsedSeconds、AnchorCell、CropKind、Kind、Quantity、WorkerNumber。Kind 为 Sow、Water、Harvest、Product；播种/浇水只从实际成功回调记录，自动产出从实际入库路径记录，换季促熟共用 Harvest，工人动作 Quantity=0、自动产出 WorkerNumber=0。加工 Product 的 Quantity 是本次锁定批量对应的实际成品份数，完成后立刻领取下一加工批次仍保留本秒 Product。

`IsPresentationResultCurrent(result)` 是开始播放的完整校验：结果须属于本局当前秒及当前保留集合，工人动作还须经 FarmingSystem 的 `IsCompletedWorkCurrent` 确认原轮次。`IsPresentationTargetCurrent(result)` 单独验证已经开始的片段：内部保存 LandOccupancy 已有的不可变 BuildingSpaceSnapshot 引用，以引用一致性确认原设施仍在，并委托同一成功动作轮次判断；它不限制秒数，让公共倍率的片段自然完成。单纯启停播种、计划关闭复种及保留当前轮的预备/应用/编辑不使已成功动作失效。拆除同格重建产生新引用，不使旧结果复活；改种/重启/收获换轮/换季清理通过 FarmingSystem 失效原动作，不清空其他实例结果。待执行任务仍经 `TryCompleteWork` 重验当前需求，不能借成功动作校验跳过执行检查。没有新增永久 ID、事件历史或第二份土地状态，调用方不能修改集合或内部凭据。

每个新事件秒先覆盖结果，平静秒也清空；批量请求结束仅留下最后经营秒，不是整个请求的历史。初次表现挂接记录当前秒并跳过已有结果，之后按经营秒去重；开发高倍率跳过过期动作不排队。暂停不推进经营或产生结果，表现冻结并在恢复时继续有效当前片段。此 Seam 只供人物和设施表现读取，不承担存档、审计或全局事件总线。测试为 TestProductionResults 与 TestWorkerPresentation。


建拆及底线入口 `TryPlace/BuildFarm/BuildProcessor/RemoveBuilding/SetRawReserve` 接受默认 Player 的 `CommandOrigin`，开发流程显式传 Scenario；非法来源在修改前抛参数异常。已启用日志时，预检无命令记录，实际提交记录原输入和真实结果。`AdvanceTick/AdvanceTicks` 请求结束及完整命令结束作为生产汇总安全边界，生命周期尾段先于 GameEnded；详见[日志 Interface](../../logging/interface-logging.md)。这些观察不修改生产推进、领取时点或资源结算。


## 地区天气 Interface

局创建时固定 `WeatherMode.Natural` 或 `Explicit`，默认自然；显式仅供开发／测试，不执行自然预排。FarmGame用同一局种子与组装时选定的天气配置创建独立天气执行器，产量也有独立随机源，开局设施和市场仍使用原抽取顺序。配置值可被多个局共享，运行排程、活动状态、缓存与生产完全隔离。`ActiveWeatherRegions` 原样转发天气模块的不可变快照：状态未变复用同一对象引用，实际变化发布新快照，不改写已返回对象；失败设置／清除不使其失效。`GetWeather(cell)` 查询单格，农田详情的 `CurrentWeather` 按真实九格雨＞云＞晴判定，不提供未来事件、随机倍率或收获预测。

`GetBuildingSpaces()` 同样原样转发土地模块的稳定不可变快照，占地未变时复用同一引用；成功建拆后发布新快照，失败命令保留引用，旧对象不被修改。水分、作物、加工、天气或日历推进不改变占地，因此不使此快照失效；地图使用该约定区分空间同步与逐次动态刷新。生产者约定见[土地 Interface](../../land/land-occupancy/interface-land-occupancy.md)，转发回归在 `TestFarmGame.CheckStableSnapshotForwarding`。

`TrySetWeatherRegion(cells,kind,out error)` 和 `TryClearWeatherRegion(cells,out error)` 委托天气 Module 完整验证矩形、越界、互斥与模式，失败零修改；自然模式拒绝显式设置。成功只改变天气，供水仍等下一经营秒。设置多云或降雨，清除既有完整矩形恢复默认晴。当前最多3片的规则由天气 Module 维护。设置界面不向玩家提供。

天气快照在完整秒后与 `Calendar.ElapsedSeconds` 同步；刚生效或结束的天气作用于下一秒，不回溯刚结算的生长。同一 `CropHarvestResult.Quantity` 用于正常／促熟入库、TickResult 汇总、ProductionResult 和日志，经营不重算收成。模块职责见[天气 Interface](../../weather/interface-weather-system.md)，作物基准和天气系数见[作物规则](../../../gameplay/production/crop-growth.md)。

## 整批道路命令

`CheckRoadPlacement(IReadOnlyList<Vector2I> cells,RoadMaterial material)` 只读检查五种材质、非空且无重复的候选、全范围、占用与可用金币，返回 `RoadPlacementCheck`。总费用为候选格数乘 `GetBuildingCostCents(Road)`，以 long 分计算；拒绝也保留请求总费用。查询不预留土地或资金，不记录命令日志。

`TryPlaceRoads(cells,material,origin=Player)` 执行时重新调用同一完整检查，全部通过后扣取总费并由 `LandOccupancy` 登记逐格道路。钱包可用金额检查通过后总费才能转为 int 进行一次扣款。范围越界、候选重复、已有道路或任何生产设施子格、资金不足或非法材质均整批拒绝，结果实际格数与扣费为零，库存、土地快照、生产与金币零修改；不得覆盖、跳过或重用占用格。成功返回实际格数、实际扣费，新空间快照只保存每格材质。转角只出现一次；轨迹及 L 形候选由 UI 生成，不成为经营状态。

`TryRemoveRoads(cells,origin=Player)` 先检查整个范围是否在地图内，越界整批拒绝；随后去重并收集其中的道路，只移除这些逐格实例。范围允许跨空格、农田、加工场地及不同材质道路，不要求连续，不动生产设施与其他经营状态，不退款。没有实际道路（包括空范围）返回 `NoRoads` 与“没有可拆除的道路”，成功返回 `RemovedCount`。直线投影与当前段起终点由 UI 维护；旧 `RemoveBuilding(cell)` 仍可拆除单格道路。

所有命令保留来源校验与暂停可操作约定，未定义来源在记录/修改前抛参数异常。旧 `TryPlace(...,Road,...)` 默认土路；五种材质均只改变表现。`BuildingSpaceSnapshot.RoadMaterial` 由土地唯一登记，非道路为 null，不新增经营材质副本、路段归属或铺设历史。道路建拆不触发加工领取，也不改变工人速度或通行。

启用日志时整批提交共用 `GameplayLog` 与 `CommandObservation`，一次 `PlaceRoads/RemoveRoads` 对应一次收到、真实领域结果和一次终结；没有逐格伪造玩家命令或多次扣费事件。拒绝记录零实际格数/零扣费，范围拆除的材料明细由同次真实目标快照提供，日志不重做校验。原请求与移除明细最多采样前 32 项且记录原数量及截断，完整结果数量仍不截断，字段见 [日志 schema](../../../project/runtime-log-schema-v1.md)。

`TestFarmGame.CheckBatchRoadPlacement/Failures/FrozenMoney/Removal` 覆盖五种材质、暂停、L 形去重候选、执行重验、设施冲突、越界、重复/空范围、非法材质与来源、可用金币准确耗尽、冻结金额不可使用、跨设施混材质拆路、非连续道路、重复范围去重、失败稳定引用、旧快照不变及逐格拆除继续可用。


`GetProcessorDetails(cell)` 返回 `ProcessorDetailsSnapshot`，任一子格解析同一场地，包含作物、状态、当期成品报价、公共成品库存及 `BatchQuantity`。最后一项只读转发加工 Module 当前锁量，空闲为0，少量批次不以容量九份冒充投入；本批已完成且同秒重启时详情显示新批量，最近 `ProductionResult` 仍保留刚完成旧批的份数。该快照用于详情及开发流程证据，不转移批次状态归属。
