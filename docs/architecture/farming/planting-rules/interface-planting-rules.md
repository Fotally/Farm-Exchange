# PlantingRules 对外接口

`Check/CanSow` 内部允许附带本局 `ProductionDiagnostics` 和真实锚点索引；仅由 `TryCompleteWork` 的原执行重验传入。详情、任务查询、下一事件探测和计划静态校验保持纯查询，不记录 RuleChecked 或消耗采集预算。受控 RuleChecked 使用这次实际重验结果：禁生短路仅记录 Season=false，适季才记录 Time；InsufficientTime 保留 Time=false 和 Outcome=Success，不改变 CanSow。重验共用原判断，不为记录重复运行；详见[生产与工人诊断](../../logging/implementation-production-diagnostics.md)。

对应 `scripts/farming/PlantingRules.cs`。`PlantingRules.Check(crop, calendar, hasWater)` 是只读播种判断入口，返回 `None`、`WrongSeason` 或 `InsufficientTime`。其中只有 `WrongSeason` 禁止播种；`InsufficientTime` 表示预计来不及成熟的风险，当前适季仍可播种。`CanSow(crop, calendar, hasWater)` 为工人执行提供同一判断的布尔结果。调用方传入当前日历快照和该田当前水分；本模块不修改农田、日历或库存。

`PlantingFailure` 是公开结果枚举，由 `FarmGame.GetPlantingCheck(cell, crop)` 暴露给 UI 的选种与风险提示；`PlantingRules` 判断实现仍为内部类型，UI 不直接调用它。

本模块从 `CropCatalog` 读取适宜季节和生长天数，按连续适宜季节计算到下一段不适宜季节的剩余秒数，包括冬春跨年。预计成熟包含本次工人动作到首个生长步进的 1 秒、干田最少 1 秒供水等待，以及生长天数向上取整后的整秒数。预计时刻不晚于禁生季节起点时返回 `None`，因为正常到期结算先于该步换季。判断不预测降雨，也不估计工人排队；换季成熟补救不计入风险预算或作物条长度。

`FarmingSystem.GetWorkNeed` 与执行时的 `TryCompleteWork` 共用 `CanSow`，仅空田当前适季且允许播种时提供需求；`FarmGame` 的详情和选种查询复用 `Check` 展示季节与风险。已播种待水及生长中的农田不再重复做播种检查。对应玩家规则见[作物生长与选种](../../../gameplay/production/crop-growth.md)。
