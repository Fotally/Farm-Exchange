# PlantingRules 对外接口

本模块是作物适季时间计算的唯一入口。适宜季节仍由 [CropCatalog](../crop-catalog/interface-crop-catalog.md) 提供，固定月、季和年度长度由 [GameCalendar](../../time/game-calendar/interface-game-calendar.md) 提供；不持有日历、农田或计划状态。

`GetGrowingWindowEndTimeUnits(crop, now)` 接收有效作物与非负累计精确时间单位，返回当前位置连续适季区间的首个禁生季起点；当前已禁生则返回 `now`。它按四季顺序跨越全部相邻适季，包括冬季到下一年春季，不在适季边界截短。当前七种作物都有禁生季；若季节标志全年适宜，则无有限禁生边界，返回 `long.MaxValue`。查询不做播种预算、周期比较、tick 取整或生产结算。

三个实际业务共用上述边界，各自保留判断口径：`Check` 加入播种与最低供水整秒预算；`CultivationPlanBook.ValidateEntries` 用年度整数日起点和完整整数日周期比较，恰到边界不标风险；`FarmingSystem.GetExpectedRoundEndTimeUnits` 先把实际剩余生长量对齐经营 tick，再与禁生边界取早。它们不共用完整校验，不把预计结束解释为收成。

`Check/CanSow` 内部允许附带本局 `ProductionDiagnostics` 和真实锚点索引；仅由 `TryCompleteWork` 的原执行重验传入。详情、任务查询、下一事件探测和计划静态校验保持纯查询，不记录 RuleChecked 或消耗采集预算。受控 RuleChecked 使用这次实际重验结果：禁生短路仅记录 Season=false，适季才记录 Time；InsufficientTime 保留 Time=false 和 Outcome=Success，不改变 CanSow。重验共用原判断，不为记录重复运行；详见[生产与工人诊断](../../logging/implementation-production-diagnostics.md)。

对应 `scripts/farming/PlantingRules.cs`。`PlantingRules.Check(crop, calendar, hasWater)` 是只读播种判断入口，返回 `None`、`WrongSeason` 或 `InsufficientTime`。其中只有 `WrongSeason` 禁止播种；`InsufficientTime` 表示预计来不及成熟的风险，当前适季仍可播种。`CanSow(crop, calendar, hasWater)` 为工人执行提供同一判断的布尔结果。调用方传入当前日历快照和该田当前水分；本模块不修改农田、日历或库存。

`PlantingFailure` 是公开结果枚举，由 `FarmGame.GetPlantingCheck(cell, crop)` 暴露给 UI 的选种与风险提示；`PlantingRules` 判断实现仍为内部类型，UI 不直接调用它。

本模块从 `CropCatalog` 读取生长天数，以日历累计秒乘 `GameTimeUnits.PerSecond` 查询共同适季边界。预计成熟包含本次工人动作到首个生长步进的 1 秒、干田最少 1 秒供水等待，以及生长天数向上取整后的整秒数；整秒预算转换为精确单位后与边界剩余量比较。预计时刻不晚于禁生季节起点时返回 `None`，因为正常到期结算先于该步换季。判断不预测降雨，也不估计工人排队；换季成熟补救不计入风险预算或作物条长度。

`FarmingSystem.GetWorkNeed` 与执行时的 `TryCompleteWork` 共用 `CanSow`，仅空田当前适季且允许播种时提供需求；`FarmGame` 的详情和选种查询复用 `Check` 展示季节与风险。已播种待水及生长中的农田不再重复做播种检查。对应玩家规则见[作物生长与选种](../../../gameplay/production/crop-growth.md)。

`TestProductionState.CheckGrowingWindows` 用七种作物的四季边界对照覆盖两年重复、冬春连续、季末最后一个精确单位与首个禁生单位，验证各作物干湿田的最后安全秒及晚一秒风险。真实农田播种后检查正常成熟 tick、查询零修改和禁生清除零收成；原促熟严格阈值与恰在边界正常收获仍由已有专项检查验证。
