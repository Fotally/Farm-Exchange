# FarmGame 的 tick 推进实现

对应[FarmGame 对外接口](interface-farm-game.md)的 AdvanceTick。内部 PlotState、库存数组和工人游标仅由 FarmGame 维护，调用方只获得快照与结果。

一次 tick 依次：

1. 推进正在生长的农田和正在加工的场地；到期收获或完成加工品。
2. 将公共原料库存分配给空闲、匹配的加工场地。
3. 单工人轮流寻找农田，完成一次播种或浇水。
4. 累计当日 tick；第 10 tick 换日，并按新天数更新价格。

新建加工场地时如已有原料，会立即启动加工。地图镜头、可见块和帧率不影响上述遍历；满地图 50 tick 检查覆盖角落实体。作物成熟与加工时长以[作物表](../../../gameplay/production/crop-growth.md)为准，出售结算以[交易规则](../../../gameplay/trading/sales.md)为准。
