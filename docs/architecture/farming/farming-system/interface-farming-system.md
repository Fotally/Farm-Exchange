# FarmingSystem 对外接口

对应类型：`FarmExchange.Farming.FarmingSystem`，代码位于 `scripts/farming/FarmingSystem.cs`。它唯一维护每块农田的所选作物、播种或生长阶段及剩余 tick；空地与加工格不创建农田状态。

| 成员 | 约定 |
| --- | --- |
| `Place(index, crop)`、`Remove(index)` | 在 `FarmGame` 的建造、拆除协调中创建或清理该格农田状态。 |
| `SetCrop(index, crop)` | 同品种无变化；改种时丢弃未收获进度并清零阶段与剩余 tick。 |
| `Get(index)` | 返回独立的只读 `FarmSnapshot`，含作物、阶段和剩余 tick。 |
| `AdvanceGrowth(index, out harvestedCrop)` | 每 tick 推进生长中的农田；成熟时清除本轮阶段并报告需入库的作物。 |
| `TryWork(index)` | 有播种或浇水工作时执行一次受控阶段转换；生长中或无农田时返回未执行。 |
| `SetGrowingForBenchmark`、`Clear` | 仅供满地图 50 tick 夹具设置和重建。 |

`WorkerScheduler` 只调用 `TryWork`，不取得可变农田状态。`FarmGame` 按旧 tick 顺序把成熟结果交给 `Inventory`；本轮仍沿用六作物和原生长 tick，不加入季节、水分或 #40 移动耗时。
