# ProcessingSystem 对外接口

对应类型：`FarmExchange.Processing.ProcessingSystem`，代码位于 `scripts/processing/ProcessingSystem.cs`。它唯一维护每处加工场地匹配的作物和进行中批次的剩余精确时间单位。数组仅在锚点持有一份状态，九个子格不复制批次；子格解析与 footprint 由土地模块负责，经营入口只传锚点。

| 成员 | 约定 |
| --- | --- |
| `Place(index, crop)`、`Remove(index)` | 在 `FarmGame` 的建造、拆除协调中创建或清理该锚点加工状态；拆除仍丢弃已投入原料。 |
| `Get(index)` | 返回独立的只读 `ProcessorSnapshot`，含匹配作物和向上取整的剩余秒数。 |
| `GetStatus(index, inventory)` | 只读返回加工中、缺少原料、受保留底线限制或待领取原料；进行中优先，其余原因从当前公共库存实时查询，不缓存旧失败结果。 |
| `Advance(index, out productCrop)` | 推进进行中的批次；完成时报告需入库的加工品作物。 |
| `TryStart(index, inventory)` | 空闲时通过库存唯一受底线限制的领取操作取一份匹配原料，并立即设定加工时长；无原料、受底线限制或正忙时不修改状态。 |
| `SetProcessingForBenchmark`、`Clear` | 仅供满地图 50 tick 夹具设置和重建。 |

`FarmGame` 保留旧遍历顺序：先推进已有批次，之后按 `Y * 384 + X` 递增，按空间实例让全部空闲场地各一次依次领取；后一个场地读取前一个扣减后的库存。新建场地立即调用同一全场领取阶段，并检查当前底线。设置底线不启动领取，下一经营步领取阶段按新值检查；提高底线不退还进行中投入物，也不重置时长。配方、时长与建造费保持。
