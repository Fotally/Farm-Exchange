# FarmGame 的经营步进顺序

对应[FarmGame 对外接口](interface-farm-game.md)的 AdvanceTick。`FarmGame` 只协调推进相位；农田状态由 `FarmingSystem`、加工状态由 `ProcessingSystem`、工人轮转游标由 `WorkerScheduler`、分类库存由 `Inventory` 唯一维护。调用方只获得聚合快照与结果。

每次步进代表一模拟秒，依次：

1. 若本秒输入降雨，按格索引顺序对全部现有农田调用 `FarmingSystem.SupplyWater`；暂停时不应用降雨。
2. 按旧格索引顺序推进 `FarmingSystem` 的生长和 `ProcessingSystem` 的在加工批次；到期结果由 `FarmGame` 交给 `Inventory` 入库。收获清除本轮水分，本秒降雨不立即补水。
3. 按旧格索引顺序让 `ProcessingSystem` 的空闲场地从公共库存领取匹配原料。
4. `WorkerScheduler` 轮流寻找农田，并通过 `FarmingSystem.TryWork` 完成一次播种或浇水；空田播种先按当前日历、水分和连续适宜季节检查预计成熟，未通过时跳过；湿润空田播种后直接生长。
5. `GameCalendar` 累计一秒；跨入新日时按新天数更新旧价格曲线。

生产时长统一用整数比例单位累计：一模拟秒推进 7 单位，一游戏日为 360 单位，半日为 180 单位。作物获得水或场地领取原料时设置目标；之后每秒扣除 7 单位，首次达到目标时收获或完工。步进开始的降雨让待水作物从本秒生长；工人在生长相位之后浇水或使用留水播种，则从下一步开始扣减。同一步到期与换日、跨季同时发生时，先结算成熟、加工、领取和工人动作，最后换日。暂停时全部经营相位不执行，恢复后不补现实时间；窗口失焦不会自动暂停。

新建加工场地时如已有原料，会立即启动加工。地图镜头、可见块和帧率不影响上述遍历；满地图 50 步检查覆盖角落实体。作物成熟与加工时长以[作物表](../../../gameplay/production/crop-growth.md)为准，出售结算以[交易规则](../../../gameplay/trading/sales.md)为准。
