# ProcessingSystem 对外接口

对应类型：`FarmExchange.Processing.ProcessingSystem`，代码位于 `scripts/processing/ProcessingSystem.cs`。它唯一维护每处加工场地匹配的作物、进行中批次锁定的投入份数和剩余精确时间单位。数组仅在锚点持有一份状态，九个子格不复制批次；子格解析与 footprint 由土地模块负责，经营入口只传锚点。

内部可选 `Diagnostics` 由经营局组装为本局生产诊断入口。`TryStart` 只在真实扣除原料成功后记录 RawClaimed，再在批次建立后记录 ProcessingStarted；前者 Quantity 为实际领取量，后者不产生第二笔消耗。门禁通过后才查询 `GetStatus(index, inventory)`，保留真实待料/底线/可领取状态。完工记录由 FarmGame 在实际加工品入库后提交，详细诊断不改变领取顺序、失败资源或批量推进。详见[生产与工人诊断](../../logging/implementation-production-diagnostics.md)。

| 成员 | 约定 |
| --- | --- |
| `Place(index, crop)`、`Remove(index)` | 在 `FarmGame` 的建造、拆除协调中创建或清理该锚点加工状态；拆除仍丢弃已投入原料。 |
| `Get(index)` | 返回独立的只读 `ProcessorSnapshot`，含匹配作物、向上取整的剩余秒数和 `BatchQuantity`；空闲量为0。 |
| `GetStatus(index, inventory)` | 只读返回加工中、无可用原料、受保留底线限制或待领取原料；进行中优先，其余原因从当前可用原料实时查询，不缓存旧失败结果。一次卖单全部冻结时与可用量为零同样返回等待原料，冻结不改变已开始批次。 |
| `Advance(index, inventory, out productCrop, out quantity)` | 推进进行中的批次；完成时报告需入库的加工品作物和锁定份数，同时清空时间和批量。最后一秒先检查本局成品容量，溢出时抛 `OverflowException`，该批剩余时间、锁量及库存不变；实际入库仍由 FarmGame 紧邻提交。 |
| `TryStart(index, inventory)` | 空闲时通过库存唯一受底线限制的领取操作以 `MaxBatchQuantity=9` 为上限原子领取匹配原料，锁定实际份数并立即设定完整单批加工时长；不足九份有可领取量也开工，中途不追加；无原料、受底线限制或正忙时不修改状态。 |
| `SetProcessingForBenchmark`、`Clear` | 仅供满地图 50 tick 夹具设置和重建；满图在加工状态以九份锁量初始化，夹具修改仍按日志未登记资源变化降低覆盖。 |

`FarmGame` 保留旧遍历顺序：先推进已有批次，之后按 `Y * 384 + X` 递增，按空间实例让全部空闲场地各一次依次领取；后一个场地读取前一个扣减后的库存。新建场地立即调用同一全场领取阶段，并检查当前底线。设置底线不启动领取，下一经营步领取阶段按新值检查；提高底线不退还进行中投入物，也不重置时长。配方、时长与建造费保持。

批量内部接口 `GetNextEventSeconds(index, inventory)` 在忙碌时返回本批向上取整的剩余秒数，空闲且有可领取原料返回 1，无可领取原料返回 `uint.MaxValue`。`AdvanceQuietSeconds(index, seconds)` 只在完工前一次扣除 `7×seconds` 精确单位，不入库、不领取。事件秒仍使用 `Advance` 和稳定锚点顺序的 `TryStart`，每批独立取整，不结转超出的单位；共享原料竞争、冻结与底线由原库存操作重验。委托买入的原料使下一秒成为领取事件，不在成交当秒领取，详见[批量实现](../../game-state/farm-game/implementation-batched-simulation.md)。


`ProcessingSystem` 是唯一的批量状态拥有者，库存只拥有尚未投入的公共量；`FarmGame` 不保存第二份进行中投入。`TryStart` 成功后协调入口读取同一快照记录实际消耗；`Advance` 返回量用于成品入库、`TickResult.Produced`、宽整数批量总数、`ProductionResult.Quantity` 与生产日志，不能把完成批次数当成份数。完工后紧邻库存提交不存在外部命令插入，容量检查与入库使用同一局库存。

容量异常继续走经营入口既有异常传播和日志观察，不解释成正常待料、不自动调整库存或重试。失败批次不产生成功结果；此前已完成经营秒及此前同一事件相位的真实提交不回滚，完整检查点只在完整事件秒结束后发布。`TestBatchProcessing` 从剩余一秒的真实九份批次验证容量不足时零修改，明确释放一份成品容量后完成原批且不能重复完成。
