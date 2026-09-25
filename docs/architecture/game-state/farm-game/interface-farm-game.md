# FarmGame 对外接口

对应类型：FarmExchange.Gameplay.FarmGame，代码位于 scripts/gameplay/FarmGame.cs。调用方为主场景和地图；测试也通过该公开接口验证行为。当前只有一个实现，未声明 C# interface 类型。

| 用途 | 公开成员 | 调用方需要知道的约定 |
| --- | --- | --- |
| 定义 | Crops、GetCrop | 六种作物的名称、场地、时长与倍率来自[作物表](../../../gameplay/production/crop-growth.md) |
| 状态查询 | GetPlot、GetRawStock、GetProductStock、GetProductPriceCents | 返回指定格的快照、分类库存和当日售价；不暴露可变内部数组 |
| 经营命令 | BuildFarm、BuildProcessor、SetFarmCrop、RemoveBuilding | 空地可直接建造，每座 10.00 金币；成功时返回 null，失败时返回给玩家的原因；执行后界面同步表现 |
| 时间 | AdvanceTick | 完整推进一次世界经营并返回 TickResult；每 10 tick 换日 |
| 交易 | SellAll | 卖出全部加工品并返回数量及收入分值 |
| 只读状态 | MoneyCents、BuildingCostCents、CurrentDay、CurrentFlourPriceCents、DailyPriceChangePercent | 金额以分保存，日期从 1 开始；时间只供内部经营使用，玩家界面暂不显示 |

GetPlot 返回 PlotSnapshot，不泄露内部 PlotState。WorldMap 只通过 GetPlot 获取地图外观；它不能驱动 AdvanceTick，也不修改库存。Main 接收玩家操作、调用经营命令并在状态变化后通知地图同步。

时间顺序见[实现](implementation-tick-order.md)；开局布局见[实现](implementation-opening-layout.md)。玩家可观察的生产、交易、土地规则分别以 docs/gameplay/ 下的专题文档为准。
