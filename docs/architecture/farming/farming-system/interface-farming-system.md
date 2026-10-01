# FarmingSystem 对外接口

对应类型：`FarmExchange.Farming.FarmingSystem`，代码位于 `scripts/farming/FarmingSystem.cs`。它唯一维护每块农田的所选作物、水分、播种或生长阶段、剩余精确时间单位和本格工作凭据版本；空地与加工格不创建农田状态。

| 成员 | 约定 |
| --- | --- |
| `Place(index, crop)`、`Remove(index)` | 在 `FarmGame` 的建造、拆除协调中创建或清理该格农田状态。 |
| `SetCrop(index, crop)` | 同品种无变化；改种时丢弃未收获进度并清零阶段与剩余时间，保留田块水分。 |
| `Get(index)` | 返回独立的只读 `FarmSnapshot`，含作物、阶段、向上取整的剩余秒数及是否有水。 |
| `SupplyWater(index)` | 雨水与工人浇水共用的一次供水操作；空田留水、已播种作物开始生长，生长中重复调用不重置进度。 |
| `AdvanceGrowth(index, out harvestedCrop)` | 每经营秒推进生长中的农田；成熟时清除本轮阶段和水分，并报告需入库的作物。收获数量由作物定义决定。 |
| `GetWorkNeed(index, calendar)` | 返回可空只读 `FarmWorkRequest`：空田且 `PlantingRules` 允许时需 `Sow`，干燥已播种田需 `Water`，无田、生长中、不适季或时间不足返回空。只查询、不认领或修改农田；需求不包含工人优先级、游标或路线成本。 |
| `TryCompleteWork(request, calendar)` | 使用执行时日历重新查询需求，并逐项匹配目标格、本格版本与指定动作；不匹配返回 `false` 且零修改。有效 `Sow` 使农田进入已播种，已有水则直接生长；有效 `Water` 调用同一 `SupplyWater` 后生长。不会把过期浇水转为播种。 |
| `TryWork(index, calendar)` | 兼容已有农田模块调用：查询当前需求后立即转发 `TryCompleteWork`，不保留第二套规则。真实经营工人通过需求、移动耗时及指定动作执行，不用本方法跳过移动或工作秒。 |
| `ClearDisallowedCrops(season)` | 换季后检查全部现有农田；只清除新季节不适宜的待水或生长中作物，清零阶段、精确进度与水分，保留田块和所选作物。空田留水及适季作物保持不变；重复调用不会重复失败或产生收成。 |
| `SetGrowingForBenchmark`、`Clear` | 仅供满地图 50 tick 夹具设置和重建。 |

`FarmWorkRequest` 为本模块文件内的只读记录，包含 `CellIndex`、`Revision` 与 `FarmWorkKind`（`Sow`/`Water`）。局部版本在新建、拆除、实际改种、收获进入新轮、越季清理以及夹具清空时失效，避免同格同品种重建或新轮误接旧任务；同品种重复选择不改版本。播种后的紧接浇水仍属于同一版本。雨水不靠改版本取消任务，而是改变真实需求：已开始生长就不再需要人工浇水。该凭据不是全局实体标识，也不承诺调度优先级。

`WorkerScheduler` 只查询工作需求并调用指定工作操作，不取得可变农田状态。`FarmGame` 在步进开始时向全部现有农田传递显式降雨，再按固定相位把成熟结果交给 `Inventory`，累计秒后发现季节变化时传入新季节完成整轮清理。调用方不逐字段清除农田，也不记录历史失败状态；收获发生在换季之前，已收获空田不会被越季清理。移除农田删除其水分状态。播种检查见[PlantingRules 接口](../planting-rules/interface-planting-rules.md)，工人移动与三人认领见[WorkerScheduler 接口](../../workers/worker-scheduler/interface-worker-scheduler.md)。旧对象、旧轮、雨水满足及季末执行重验的专项测试位于 `tests/unit/TestWorkerScheduler.cs`。
