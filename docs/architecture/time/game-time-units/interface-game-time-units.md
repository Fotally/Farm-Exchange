# GameTimeUnits 时间比例接口

对应 `scripts/time/GameTimeUnits.cs`。日历、农田和加工共用这一组整数比例：`PerSecond = 7`、`PerHalfDay = 180`、`PerDay = 360`。它不持有时间状态，也不决定经营相位。

`RemainingSeconds(units)` 将正的剩余比例单位向上取整为可见秒数；零返回零。`FarmingSystem` 和 `ProcessingSystem` 各自持有剩余单位，每次经营步进扣除 `PerSecond`。因此 0.5 天和整数天的目标即使落在两个整秒之间，也在首次达到目标的整秒结算。日期边界由持有累计秒数的 [GameCalendar](../game-calendar/interface-game-calendar.md) 换算。
