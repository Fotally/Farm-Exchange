# FarmGame 的 tick 推进实现

对应[FarmGame 对外接口](interface-farm-game.md)的 AdvanceTick。`FarmGame` 只协调推进相位；农田状态由 `FarmingSystem`、加工状态由 `ProcessingSystem`、工人轮转游标由 `WorkerScheduler`、分类库存由 `Inventory` 唯一维护。调用方只获得聚合快照与结果。

一次 tick 依次：

1. 按旧格索引顺序推进 `FarmingSystem` 的生长和 `ProcessingSystem` 的在加工批次；到期结果由 `FarmGame` 交给 `Inventory` 入库。
2. 按旧格索引顺序让 `ProcessingSystem` 的空闲场地从公共库存领取匹配原料。
3. `WorkerScheduler` 轮流寻找农田，并通过 `FarmingSystem.TryWork` 完成一次播种或浇水。
4. 累计当日 tick；第 10 tick 换日，并按新天数更新价格。

新建加工场地时如已有原料，会立即启动加工。地图镜头、可见块和帧率不影响上述遍历；满地图 50 tick 检查覆盖角落实体。作物成熟与加工时长以[作物表](../../../gameplay/production/crop-growth.md)为准，出售结算以[交易规则](../../../gameplay/trading/sales.md)为准。
