# GameCalendar 对外接口

对应 `FarmExchange.Time.GameCalendar`，代码位于 `scripts/time/GameCalendar.cs`，来源为 [T05A issue #55](https://github.com/Fotally/Farm-Exchange/issues/55)。T05B 后由 `FarmGame` 唯一持有，主场景读取其不可变快照显示日期。

| 成员 | 调用约定 |
| --- | --- |
| `GameCalendar(uint elapsedSeconds = 0)` | 从给定累计模拟秒数建立日历；默认第 1 年春 1 月 1 日第 0 秒。不读取现实时间。 |
| `Snapshot` | 返回不可变的 `CalendarSnapshot`：累计模拟秒、已完整经过的游戏日数、年、月、日、周内第几日、季节及暂停状态。年/月/日和周内日均从 1 开始。 |
| `SetPaused(bool paused)` | 设置显式暂停状态；暂停不改变累计秒数。 |
| `TryAdvanceSeconds(uint seconds)` | 未暂停时只加输入秒数；暂停时接受输入但不推进。若未暂停且超过 `uint32` 上限，返回 `false`，状态不变；其余情况返回 `true`。恢复时不会补算现实时间。 |

时间比例为 7 游戏日＝360 秒、28 日/月、3 月/季、12 月/年，共 336 日/年。完整经过的游戏日数按 `floor(累计秒数 × 7 / 360)` 计算，乘法使用更宽整数，不把 360/7 先取整。日期在跨过分数边界后的首个整秒切换：第 51 秒仍为第 1 日，第 52 秒为第 2 日，第 360 秒为第 8 日。第 1 年春 1 月 1 日从第 0 秒开始；每个新周从周内第 1 日开始。

调用方只传模拟推进量，不传动画帧时长或上次打开程序的现实时间。主界面展示 `Snapshot` 的季、年、月、日。`GameTimeUnits` 位于 `scripts/time/GameTimeUnits.cs`，是生产模块共用的比例单位：一模拟秒 7 单位、一日 360 单位、半日 180 单位；`RemainingSeconds` 向上取整，保证到期落在两次整秒之间时按首个到达整秒结算。独立日历测试见 `tests/unit/TestGameCalendar.cs`，经营连接测试见 `tests/unit/TestFarmGame.cs`。
