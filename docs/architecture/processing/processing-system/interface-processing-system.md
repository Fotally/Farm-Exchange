# ProcessingSystem 对外接口

对应类型：`FarmExchange.Processing.ProcessingSystem`，代码位于 `scripts/processing/ProcessingSystem.cs`。它唯一维护每处加工场地匹配的作物、进行中批次锁定的投入份数、long 精确绝对完工目标及预约句柄。数组仅在锚点持有一份状态，九个子格不复制批次；子格解析与 footprint 由土地模块负责，经营入口只传锚点。

内部可选 `Diagnostics` 由经营局组装为本局生产诊断入口。`TryStart` 只在真实扣除原料成功后记录 RawClaimed，再在批次建立后记录 ProcessingStarted；前者 Quantity 为实际领取量，后者不产生第二笔消耗。门禁通过后才查询 `GetStatus(index, inventory)`，保留真实待料/底线/可领取状态。完工记录由 FarmGame 在实际加工品入库后提交，详细诊断不改变领取顺序、失败资源或批量推进。详见[生产与工人诊断](../../logging/implementation-production-diagnostics.md)。

构造 `ProcessingSystem(cellCount, due)` 接收与农田共用的 [SimulationDueIndex](../../time/simulation-due-index/interface-simulation-due-index.md)。`AdvanceTo(second)` 仅 O(1) 同步生产读取时钟，快照从精确目标只读计算剩余量。忙碌批次最近事件直接查询共享索引；`CanStart(index, inventory)` 只读检查空闲且有可领取原料，仍由现有全场领取扫描使用。`TryStart` 在真实可领取时先登记预约，再原子扣除原料、锁量和目标；预约容量或期限失败不消费原料。每批独立向上取整，九份只占一条预约。委托买入的原料使下一秒成为领取事件，详见[生产到期接入](../../game-state/farm-game/implementation-production-due.md)。

| 成员 | 约定 |
| --- | --- |
| `Place(index, crop)`、`Remove(index)` | 在 `FarmGame` 的建造、拆除协调中创建或清理该锚点加工状态；拆除仍丢弃已投入原料，并撤销本批预约。 |
| `Get(index)` | 返回独立的只读 `ProcessorSnapshot`，含匹配作物、向上取整的剩余秒数和 `BatchQuantity`；空闲量为0。 |
| `GetStatus(index, inventory)` | 只读返回加工中、无可用原料、受保留底线限制或待领取原料；进行中优先，其余原因从当前可用原料实时查询，不缓存旧失败结果。一次卖单全部冻结时与可用量为零同样返回等待原料，冻结不改变已开始批次。 |
| `Complete(index, inventory, out productCrop, out quantity)` | 完成索引交出的到期批次；先检查成品容量，通过后取消预约并清空目标和批量，返回锁量供经营紧邻入库。溢出时目标、锁量、库存和预约不变。未到期或空闲返回 false。 |
| `TryStart(index, inventory)` | 空闲时通过库存唯一受底线限制的领取操作以 `MaxBatchQuantity=9` 为上限原子领取匹配原料，锁定实际份数并立即设定完整单批加工时长；不足九份有可领取量也开工，中途不追加；无原料、受底线限制或正忙时不修改状态。 |
| `SetProcessingForBenchmark`、`Clear` | 仅供满地图 50 tick 夹具设置和重建；满图在加工状态以九份锁量初始化，夹具修改仍按日志未登记资源变化降低覆盖。 |

`FarmGame` 完成全部 FIFO 产出与换季促熟后，按 `Y * 384 + X` 递增让空闲场地各一次领取；新建场地也立即调用同一全场领取阶段。设置底线不启动领取，提高底线不退回已投入原料。

`ProcessingSystem` 是唯一的批量状态拥有者，库存只拥有尚未投入的公共量；`FarmGame` 不保存第二份进行中投入。`TryStart` 成功后协调入口读取同一快照记录实际消耗；`Complete` 返回量用于成品入库、`TickResult.Produced`、宽整数批量总数、`ProductionResult.Quantity` 与生产日志，不能把完成批次数当成份数。完工后紧邻库存提交不存在外部命令插入，容量检查与入库使用同一局库存。

容量异常继续走经营入口既有异常传播和日志观察，不解释成正常待料、不自动调整库存或重试。失败批次不产生成功结果；此前已完成经营秒及此前同一事件相位的真实提交不回滚，完整检查点只在完整事件秒结束后发布。`TestBatchProcessing` 从已到期、剩余显示零秒的真实九份批次验证同一生产时钟容量不足时零修改，明确释放一份成品容量后完成原批且不能重复完成。

生产时钟已经到 E 而日历尚未提交的异常状态中，失败批次剩余显示 0，锁定量仍在且状态仍为加工中；这表示已到期但未成功入库。调用方显式续推同一 E 时继续读取失败项及后续 FIFO，不重做此前成功入库、不自动重试；详见[生产到期接入](../../game-state/farm-game/implementation-production-due.md)。
