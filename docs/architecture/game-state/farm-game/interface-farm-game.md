# FarmGame 对外接口

对应类型：FarmExchange.Gameplay.FarmGame，代码位于 scripts/gameplay/FarmGame.cs。调用方为主场景和地图；测试也通过该公开接口验证行为。当前只有一个实现，未声明 C# interface 类型。

| 用途 | 公开成员 | 调用方需要知道的约定 |
| --- | --- | --- |
| 定义 | Crops、GetCrop | 委托 `CropCatalog`；七种作物的名称、场地、时长、收获量与独立定价倍率来自[作物表](../../../gameplay/production/crop-growth.md) |
| 状态查询 | GetPlot、TryGetPlot、GetRawStock、GetProductStock、GetRawPriceCents、GetProductPriceCents | 返回指定格的只读快照、分类库存和两类当日售价；农田地块快照含 `HasWater`，`TryGetPlot` 对地图外返回 `OutOfBounds` |
| 详情查询 | GetFarmDetails、GetProcessorDetails | 分别返回现有农田的等待工人、已湿润待播种、待水、生长中、不适季或剩余时间不足状态，或加工场地的无原料等待/加工中状态，同时附带对应周期、当日售价和公共库存；类型不匹配属于调用错误 |
| 放置 | CheckPlacement、TryPlace | 共用[放置规则](../../land/placement-rules/interface-placement-rules.md)；预检只读，执行时重新检查并返回实际扣费或稳定失败原因 |
| 旧命令与编辑 | BuildFarm、BuildProcessor、SetFarmCrop、RemoveBuilding | 旧建造命令转发 `TryPlace`；成功返回 null，失败返回给玩家的原因；选种和拆除对地图外都返回“地图外地块” |
| 时间与降雨 | AdvanceTick(bool isRaining = false)、Calendar、IsPaused、SetPaused | 每次未暂停步进推进一模拟秒并返回 `TickResult`；显式降雨在步进开始时供水，默认无雨；`Calendar` 为只读日期快照，暂停期间不推进降雨、生产、日期或行情 |
| 交易 | SellRaw、SellAll | SellRaw 按作物卖出该种全部未投入加工的原料；SellAll 卖出全部加工品。均返回数量及收入分值；空库存返回零 |
| 只读状态 | MoneyCents、BuildingCostCents、CurrentDay、CurrentFlourPriceCents、DailyPriceChangePercent | 余额由 `Wallet` 持有并委托查询，金额以分保存；`CurrentDay` 从日历已过天数换算，旧行情仅在新日更新 |

GetPlot 从 `LandOccupancy`、`FarmingSystem`、`ProcessingSystem` 聚合 `PlotSnapshot`，不泄露可变内部状态；空地快照的作物字段没有经营含义。`TryGetPlot` 返回 `None` 或 `OutOfBounds`，无效时输出默认快照；既有 `GetPlot` 保留“调用方已确认有效格”的便利形式，越界抛 `ArgumentOutOfRangeException`。WorldMap 只对有效格调用 GetPlot 获取外观；它不能驱动 AdvanceTick，也不修改库存。Main 接收玩家操作、调用经营命令并在状态变化后通知地图同步。

`FarmDetailsSnapshot` 与 `ProcessorDetailsSnapshot` 是只读值，包含作物定义、稳定状态原因、对应当日售价和库存。`Main` 先用 `GetPlot` 分派详情面板，再向对应查询索取语义快照；UI 只负责中文文案和控件更新。雨后空田由 `WaitingForWorkerWithWater` 表示；空田的 `WrongSeason` 与 `InsufficientTime` 直接复用[PlantingRules](../../farming/planting-rules/interface-planting-rules.md)的判断。内部测试构造器可指定初始累计秒，公开游戏构造器从零时刻开始。

`FarmGame` 仍负责推进、建造与出售的业务顺序。建造通过 `PlacementRules` 检查后交 `Wallet` 扣款，占用与生产状态在同一调用内创建或清理；加工场地建成后立即按旧规则领取原料。收获和加工品进入 `Inventory`，出售先计算并入账再清空对应库存。无效作物的建造与选种命令返回失败原因，不修改地块和余额。`HasConsistentState` 是测试用内部检查，用于核对每格占用与对应生产状态恰好一致。

时间顺序见[实现](implementation-tick-order.md)；开局布局见[实现](implementation-opening-layout.md)。玩家可观察的生产、交易、土地规则分别以 docs/gameplay/ 下的专题文档为准。
