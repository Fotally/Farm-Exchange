# WorkerScheduler 对外接口

对应类型：`FarmExchange.Workers.WorkerScheduler`，代码位于 `scripts/workers/WorkerScheduler.cs`。它唯一维护当前单工人的下一块候选农田索引。

| 成员 | 约定 |
| --- | --- |
| `WorkOne(farming, calendar)` | 从当前游标起按格索引循环寻找需求，把同一日历快照交给 `FarmingSystem.TryWork`；最多执行一次播种或浇水，成功后把游标移到该格之后。季节检查未通过的农田被跳过；没有工作时返回 `false` 且游标不变。 |

`WorkerScheduler` 不直接修改农田内部字段，也不持有可变农田容器。本轮仅迁移旧轮转，不建立工人位置、目标或移动时间；#40 规则确认后再扩展。
