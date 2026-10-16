# FarmingSystem 对外接口

内部可选 `Diagnostics` 由经营局组装为本局 `ProductionDiagnostics`，不改变公开生产操作。选定实例在实际供水、播种、开始生长和越季清理处交出前后状态，湿田播种保留 Seeded 中间状态；重复已有水分不产生供水转换。前快照必须先经过采集门禁。正常及促熟收获的诊断由 FarmGame 在实际库存入库后提交，不能在仅返回待入库作物时提前宣布完成。详见[生产与工人诊断](../../logging/implementation-production-diagnostics.md)。

`GetWorkNeed` 始终是无诊断输出的任务查询；`TryCompleteWork` 通过同一个私有判断过程执行原有一次重验，仅此时传入播种诊断。排程预检和下一事件探测不消耗采集预算；真实重验的失败保持原农田和任务结果，不为日志追加检查。

`ClearDisallowedCrops(season)` 在原清理遍历中返回 `CropClearResult`，逐作物查询真实清理轮数；空田、仍适季作物和已通过促熟收获的轮次不计入。结果仅描述本次状态提交，不修改清理顺序、不读取库存、不持有日志计数。FarmGame 将结果交给[生产窗口](../../logging/interface-logging.md)，清理轮数不能解释成库存损失份数。

对应类型：`FarmExchange.Farming.FarmingSystem`，代码位于 `scripts/farming/FarmingSystem.cs`。它唯一维护每块农田的所选作物、水分、播种或生长阶段、long 精确绝对成熟目标、成熟预约句柄、本轮浮点随机基础量、天气积分段与已历精确积分、本轮身份及播种许可版本；状态数组只有锚点有值，九个子格不复制状态。空地与加工格不创建农田状态。

| 成员 | 约定 |
| --- | --- |
| `Place(index, crop, weather)`、`Remove(index)` | 在 `FarmGame` 的建造、拆除协调中创建或清理该锚点农田状态；新田显式接收权威整田天气，移除撤销旧成熟预约。 |
| `Indices` | 返回现有农田锚点升序的独立只读缓存；建造、拆除、清空后重建缓存，旧集合不随之改变。调度只扫描真实农田，轮转游标仍按锚点索引保存，不按集合位置保存。 |
| `SetCrop(index, crop)` | 同品种无变化；改种时丢弃未收获进度并清零阶段与剩余时间，保留田块水分。 |
| `RestartCrop(index, crop)` | 强制丢弃本轮，包括同品种；清零阶段和剩余时间，保留水分与当前播种启停。用于手动立即改种及计划切换。 |
| `SetSowingEnabled(index, enabled)` | 控制空田是否可播种，默认允许；改变值仅使旧待执行 `Sow` 凭据失效，再启用也不能复活旧 `Sow`。已播种一轮的 `Water` 凭据、供水、生长及已成功动作的所属轮次保持；重复设置同值零修改。 |
| `Get(index)` | 返回独立只读 `FarmSnapshot`，含作物、阶段、向上取整的 `RemainingSeconds`、`HasWater`、精确 `RemainingTimeUnits` 和 `SowingEnabled`。生长进度仍唯一归属本模块。 |
| `GetExpectedRoundEndTimeUnits(index, now)` | 只读预测本轮结束的累计比例单位：生长中将本轮精确绝对目标向上对齐实际经营 tick，再与 `PlantingRules.GetGrowingWindowEndTimeUnits` 提供的共同禁生边界取早；空田或待水返回空。`now` 为本步推进后的累计单位。适季跨季不截短；禁生边界可能促熟或清理，本结果不承诺收成、不修改农田、不猜测供水。计划直接读取本结果，不复制生长或季节结局规则。 |
| `SupplyWater(index)` | 雨水与工人浇水共用的一次供水操作；空田留水、已播种作物开始生长，生长中重复调用不重置进度。 |
| `CompleteGrowth(index, inventory, out harvest)` | 仅处理索引交出的已到期农田；局部计算截至精确目标的天气产量，先检查原料容量，再清轮、清水和取消预约，返回真实收成供调用方紧邻入库。容量失败不改本轮、水分、随机量、积分和预约。 |
| `AdvanceTo(second)` | 单次 O(1) 同步只读生产时钟，不逐田扣时或积分；调用方不得倒退，不得越过索引未处理到期项。 |
| `SetWeather(index, weather)` | 权威整田天气真实变化时，按当前生产时钟结清旧段并开始新段；相同天气零修改，晴天也显式传入。 |
| `TryMatureBeforeDisallowedSeason(index, season, inventory, out harvest)` | 仅由经营换季相位调用，当前生长中作物在传入季节禁生且精确剩余时间严格小于完整周期的 10% 时，共用正常收获的原料容量检查、终结和预约撤销，并报告实际作物及数量，促熟只统计已经历部分，入库仍由 `FarmGame` 处理。其他阶段、适季跨季及等于或超过阈值返回 `false` 且零修改；同轮不能重复报告收成。 |
| `GetWorkNeed(index, calendar)` | 返回可空只读 `FarmWorkRequest`：空田、启用播种且当前适季时需 `Sow`，干燥已播种田需 `Water`；无田、生长中、休耕空田或不适季返回空。预计时间不足仅提示风险，仍可播种。只查询、不认领或修改农田；需求不包含工人优先级、游标或路线成本。 |
| `TryCompleteWork(request, calendar)` | 使用执行时日历重新查询需求，并逐项匹配目标锚点、本轮身份、指定动作及动作所需许可版本；不匹配返回 `false` 且零修改。有效 `Sow` 使农田进入已播种，已有水则直接生长；有效 `Water` 调用同一 `SupplyWater` 后生长。不会把过期浇水转为播种。 |
| `TryWork(index, calendar)` | 兼容已有农田模块调用：查询当前需求后立即转发 `TryCompleteWork`，不保留第二套规则。真实经营工人通过需求、移动耗时及指定动作执行，不用本方法跳过移动或工作秒。 |
| `ClearDisallowedCrops(season)` | 换季后检查全部现有农田；只清除新季节不适宜的待水或生长中作物，清零阶段、精确进度与水分，保留田块和所选作物。空田留水及适季作物保持不变；重复调用不会重复失败或产生收成。 |
| `SetGrowingForBenchmark`、`Clear` | 仅供满地图 50 tick 夹具设置和重建。 |

`FarmWorkRequest` 为本模块文件内的只读记录，包含锚点索引 `CellIndex`、本轮身份 `RoundRevision`、`FarmWorkKind`（`Sow`/`Water`）及播种许可版本 `SowingRevision`。轮次槽从空田延续到本轮播种、生长，实际播种不另换身份。轮次计数按格保存且跨拆建不回退，在新建、拆除、改种重启、收获进入新轮、越季清理及夹具清空时改变，避免同锚点同品种重建或新轮误接旧任务。

播种许可版本属于现有农田状态，仅在启停值实际变化时递增。`Sow` 使用当前许可版本；`Water` 的许可版本固定为零，只随轮次或真实需水条件变化失效。`SetCrop` 同品种不重启、重复设置同一启停值不改凭据。雨水通过改变真实需求取消任务：已开始生长就不再需要人工浇水。调用方保存不透明凭据，不读取计数、不解释启停原因。该凭据不是全局实体标识，也不承诺调度优先级。

`WorkerScheduler` 只查询工作需求并调用指定工作操作，不取得可变农田状态。`FarmGame` 在步进开始时查询各农田九格天气，只向整田判定为降雨的实例供水，再按固定相位把正常成熟结果交给 `Inventory`；换季时先报告并沿原路径入库补救成熟，随后清理禁生未成熟作物，并由计划更新后续安排。调用方不逐字段清除农田，也不记录历史失败状态；已收获空田不会被越季清理。移除农田删除其水分状态。精确阈值与共同终结路径见[换季成熟实现](implementation-season-maturity.md)，播种检查见[PlantingRules 接口](../planting-rules/interface-planting-rules.md)，工人移动与三人认领见[WorkerScheduler 接口](../../workers/worker-scheduler/interface-worker-scheduler.md)。旧对象、旧轮、雨水满足及季末执行重验的专项测试位于 `tests/unit/workers/TestWorkerScheduler.cs`。

模块不解析占用子格或决定设施形状；`FarmGame` 在土地接口解析锚点后调用，`WorkerScheduler` 从 `Indices` 取得真实锚点。少量容量测试数组直接验证生产状态，完整空间几何由土地测试负责。

构造 `FarmingSystem(cellCount, due, seed=0)` 接收经营局与加工 Module 共用的 [SimulationDueIndex](../../time/simulation-due-index/interface-simulation-due-index.md)。模块只保存自身预约句柄及精确目标，经营入口不解释这些字段。开始生长先登记预约，成功后才改变播种／供水状态和抽取随机基础量，容量或期限失败资源零修改。正常成熟、促熟、改种、同种重启、拆除、禁生清理和 Clear 撤销旧预约；播种启停不取消已生长轮次。平静段只推进模块时钟，最近成熟直接从共享索引查询。完整相位与异常续读见[生产到期接入](../../game-state/farm-game/implementation-production-due.md)。

晴天是天气模块确认的默认状态，建田及实际天气变化均显式传入。农田保存当前积分段的天气系数、开始精确单位及此前积分；这不是第二份天气区域表。自然天气活动快照实际改变和显式设置／清除成功后，FarmGame 同步真实整田判定；正常成熟及促熟只结算到 `min(当前秒×7, 精确目标)`，天气段变化之外不逐秒写田。持续雨中的收获清水与下一秒再供水保持原操作。

## 已完成工作凭据的表现校验

`IsCompletedWorkCurrent(completedWork)` 只读验证本模块曾确认成功的 `FarmWorkRequest` 仍属于原农田及原轮次。它检查农田仍在和 `RoundRevision` 一致，不调用 `GetWorkNeed` 或比较未来播种许可，因此已成功播种不必仍是待播种任务；计划关闭复种、手动预备及单纯启停不会使成功动作过期。改种（含同种重启）、拆建、收获换轮或禁生换季仍使旧动作失效。它不能代替待执行任务的 `TryCompleteWork` 重验。FarmGame 仅将原凭据保留在非持久结果内部并委托本方法校验，不读取裸版本号、不成为版本拥有者。公开表现只看到成功结果及完整有效性判断。


`FarmingSystem(cellCount, due, seed=0)` 使用独立随机源；FarmGame传入局种子，不消费开局设施或行情随机序列。实际 `StartGrowth` 锁定一次6～9倍浮点基础量，并清零本轮已历单位与天气百分比积分。天气积分段截断到精确目标，正常末秒最多计实际剩余单位；全程整数积分后在终结处除以实际已历单位，避免批次拆分造成逐秒浮点偏差。`CropYieldRules` 只维护已确认的七作物系数与一次取整公式，不拥有天气区域或生长状态。基准和系数的现行数值见[作物规则](../../../gameplay/production/crop-growth.md)。

`SetGrowingForBenchmark` 通过同一开始生长操作锁定随机基础量、清空天气积分，夹具不能只设置阶段而遗漏真实轮状态。新增 `TestWeatherYield` 覆盖七作物系数、均匀浮点基础量、精确末秒、促熟均值与清理零产出，及等待／查询／重复供水不重抽。模块不对 UI 暴露基础量或预测产量。
