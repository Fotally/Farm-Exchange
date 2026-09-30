# PlantingRules 对外接口

对应 `scripts/farming/PlantingRules.cs`。`PlantingRules.Check(crop, calendar, hasWater)` 是只读播种判断入口，返回 `None`、`WrongSeason` 或 `InsufficientTime`。调用方传入当前日历快照和该田当前水分；它不修改农田、日历或库存。

本模块从 `CropCatalog` 读取适宜季节和生长天数，按连续适宜季节计算到下一段不适宜季节的剩余秒数。预计成熟包含本次工人动作到首个生长步进的 1 秒、干田最少 1 秒供水等待，以及生长天数向上取整后的整秒数。预计时刻不晚于不适宜季节起点时返回 `None`，因为到期结算先于该步换季。判断不预测降雨，也不估计工人排队；实际越季失败属于后续任务。

`FarmingSystem.TryWork` 在改变播种阶段前调用本入口；`FarmGame.GetFarmDetails` 复用同一结果生成稳定的中文详情状态。已播种待水及生长中的农田不再重复做播种检查。对应玩家规则见[作物生长与选种](../../../gameplay/production/crop-growth.md)。
