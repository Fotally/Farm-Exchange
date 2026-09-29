# FarmingSystem 对外接口

对应类型：`FarmExchange.Farming.FarmingSystem`，代码位于 `scripts/farming/FarmingSystem.cs`。它唯一维护每块农田的所选作物、水分、播种或生长阶段及剩余精确时间单位；空地与加工格不创建农田状态。

| 成员 | 约定 |
| --- | --- |
| `Place(index, crop)`、`Remove(index)` | 在 `FarmGame` 的建造、拆除协调中创建或清理该格农田状态。 |
| `SetCrop(index, crop)` | 同品种无变化；改种时丢弃未收获进度并清零阶段与剩余时间，保留田块水分。 |
| `Get(index)` | 返回独立的只读 `FarmSnapshot`，含作物、阶段、向上取整的剩余秒数及是否有水。 |
| `SupplyWater(index)` | 雨水与工人浇水共用的一次供水操作；空田留水、已播种作物开始生长，生长中重复调用不重置进度。 |
| `AdvanceGrowth(index, out harvestedCrop)` | 每经营秒推进生长中的农田；成熟时清除本轮阶段和水分，并报告需入库的作物。收获数量由作物定义决定。 |
| `TryWork(index)` | 有播种或浇水工作时执行一次受控阶段转换；湿润空田播种后直接开始生长，干燥已播种田通过 `SupplyWater` 浇水；生长中或无农田时返回未执行。 |
| `SetGrowingForBenchmark`、`Clear` | 仅供满地图 50 tick 夹具设置和重建。 |

`WorkerScheduler` 只调用 `TryWork`，不取得可变农田状态。`FarmGame` 在步进开始时向全部现有农田传递显式降雨，再按固定相位把成熟结果交给 `Inventory`。移除农田删除其水分状态。季节失败和 #40 移动耗时仍属于后续任务。
