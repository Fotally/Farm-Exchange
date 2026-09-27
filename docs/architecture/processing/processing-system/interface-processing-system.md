# ProcessingSystem 对外接口

对应类型：`FarmExchange.Processing.ProcessingSystem`，代码位于 `scripts/processing/ProcessingSystem.cs`。它唯一维护每处加工场地匹配的作物和进行中批次的剩余 tick。

| 成员 | 约定 |
| --- | --- |
| `Place(index, crop)`、`Remove(index)` | 在 `FarmGame` 的建造、拆除协调中创建或清理该格加工状态；拆除仍丢弃已投入原料。 |
| `Get(index)` | 返回独立的只读 `ProcessorSnapshot`，含匹配作物和剩余 tick。 |
| `Advance(index, out productCrop)` | 推进进行中的批次；完成时报告需入库的加工品作物。 |
| `TryStart(index, inventory)` | 空闲时从同一份公共库存领取一份匹配原料并立即设定加工剩余 tick；无原料或正忙时不修改状态。 |
| `SetProcessingForBenchmark`、`Clear` | 仅供满地图 50 tick 夹具设置和重建。 |

`FarmGame` 保留旧遍历顺序：先推进已有批次，之后按格索引依次让空闲场地领取。新建场地调用同一领取阶段，继续立即加工已有原料。本轮不引入保留量或新配方。
