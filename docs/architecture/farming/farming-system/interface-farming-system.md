# FarmingSystem 对外接口

对应类型：`FarmExchange.Farming.FarmingSystem`，代码位于 `scripts/farming/FarmingSystem.cs`。它唯一维护每块农田的所选作物、播种或生长阶段及剩余精确时间单位；空地与加工格不创建农田状态。

| 成员 | 约定 |
| --- | --- |
| `Place(index, crop)`、`Remove(index)` | 在 `FarmGame` 的建造、拆除协调中创建或清理该格农田状态。 |
| `SetCrop(index, crop)` | 同品种无变化；改种时丢弃未收获进度并清零阶段与剩余时间。 |
| `Get(index)` | 返回独立的只读 `FarmSnapshot`，含作物、阶段和向上取整的剩余秒数。 |
| `AdvanceGrowth(index, out harvestedCrop)` | 每经营秒推进生长中的农田；成熟时清除本轮阶段并报告需入库的作物。收获数量由作物定义决定。 |
| `TryWork(index)` | 有播种或浇水工作时执行一次受控阶段转换；生长中或无农田时返回未执行。 |
| `SetGrowingForBenchmark`、`Clear` | 仅供满地图 50 tick 夹具设置和重建。 |

`WorkerScheduler` 只调用 `TryWork`，不取得可变农田状态。`FarmGame` 按固定相位把成熟结果交给 `Inventory`。季节、水分和 #40 移动耗时仍属于后续任务。
