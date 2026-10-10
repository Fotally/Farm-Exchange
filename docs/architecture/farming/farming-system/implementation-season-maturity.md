# 禁生换季成熟与收获实现

履行 [FarmingSystem 接口](interface-farming-system.md)，实现位于 `scripts/farming/FarmingSystem.cs`；经营入库顺序见 [FarmGame 推进相位](../../game-state/farm-game/implementation-tick-order.md)。内部阈值只记录在设计文档与测试中，界面不展示阈值或专门收益提示。

每轮生长开始时，以 `CropCatalog` 的完整天数乘 `GameTimeUnits.PerDay` 保存精确周期；每个经营秒减 `GameTimeUnits.PerSecond`。`FarmSnapshot.RemainingTimeUnits` 保留精确值，`RemainingSeconds` 仅供显示。

正常到期先通过 `AdvanceGrowth` 收获。换季相位传入即将进入的新季节：只有 `Growing` 且该作物在新季节禁生时，比较 `RemainingTimeUnits * 10 < GrowthDays * PerDay`。乘法使用宽整数，等于十分之一不促成成熟；待水、空田和适季跨季不触发。调用方仅在实际换季时调用，查询不会触发此规则。

正常成熟与换季补救共用 `FinishHarvest`，一次清空阶段、精确剩余时间和水分，使旧工人凭据失效并返回作物。农田模块不拥有库存；`FarmGame` 仍使用原成熟入库路径取得定义的收获数量、加入公共库存，再按既有相位领取加工原料。终结后再调用补救或生长推进不会报告第二次收成。

其余禁生待水或生长中作物由 `ClearDisallowedCrops` 清理。计划模块随后更新日期安排，已过条不追补。阈值不缩短编辑器作物条，也不改变预计越季风险预算。

`GetExpectedRoundEndTimeUnits(index, now)` 只读预测已开始生长的一轮结束：正常成熟时刻为 `now + ceil(RemainingTimeUnits / PerSecond) × PerSecond`，保持正常按秒收获的实际 tick；再与首个禁生季起点取早。相邻适季边界不截短，所以甘蔗夏秋继续生长，秋冬才可能提前结束。边界结束可以是促熟入库，也可以是无收成清理，两者对后续年度安排均意味着旧轮结束；预测只给结束时刻，不复制或公开促熟阈值，不触发成熟或清理。空田与待水返回空，不推测未来工人或雨水。实际成熟仍优先于换季补救，预计时刻不改变任何生产相位。

计划空白及同种相邻条只关闭新播种，让已经开始的一轮按正常供水、生长及上述禁生规则结束；不能在条尾清零进度，也不能用缓冲跨越禁生季。实际生长与清理始终由本模块执行。计划接续按实际结束日期重验，不将无收成清理记为成熟。

`TestProductionState` 通过真实播种、供水和逐秒推进建立马铃薯 511、504、497 精确单位的剩余时间，覆盖大于、等于、小于完整 5040 单位周期的十分之一，另覆盖适季跨季、待水、空田、重复报告及正常到期优先。`CheckExpectedRoundEnd` 验证未知供水无预测、实际 tick 对齐、正常推进时结束时刻稳定、甘蔗适季跨季与禁生结局、查询零修改、正常成熟与禁生边界重合时仅收一次。经营入口的测试负责验证实际入库与加工相位。
