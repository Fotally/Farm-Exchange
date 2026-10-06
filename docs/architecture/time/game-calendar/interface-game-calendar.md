# GameCalendar 对外接口

对应 `FarmExchange.Time.GameCalendar`，代码位于 `scripts/time/GameCalendar.cs`，来源为 [T05A issue #55](https://github.com/Fotally/Farm-Exchange/issues/55)。T05B 后由 `FarmGame` 唯一持有，主场景读取其不可变快照显示日期。

| 成员 | 调用约定 |
| --- | --- |
| `GameCalendar(uint elapsedSeconds = 0)` | 从给定累计模拟秒数建立日历；默认第 1 年春 1 月 1 日第 0 秒。不读取现实时间。 |
| `Snapshot` | 返回不可变的 `CalendarSnapshot`：累计模拟秒、已完整经过的游戏日数、年、月、日、周内第几日、季节及暂停状态。年/月/日和周内日均从 1 开始。 |
| `static GetDate(uint elapsedDays)` | 将完整经过的游戏日数转换为不可变 `GameDate(ElapsedDays, Year, Month, Day, Season)`；纯换算，不读取或推进任何日历实例，不受暂停影响。日历快照与市场报价日期共用这一日期数学入口。 |
| `SetPaused(bool paused)` | 设置显式暂停状态；暂停不改变累计秒数。 |
| `TryAdvanceSeconds(uint seconds)` | 未暂停时只加输入秒数；暂停时接受输入但不推进。若未暂停且超过 `uint32` 上限，返回 `false`，状态不变；其余情况返回 `true`。恢复时不会补算现实时间。 |

时间比例为 7 游戏日＝360 秒、28 日/月、3 月/季、12 月/年，共 336 日/年。完整经过的游戏日数按 `floor(累计秒数 × 7 / 360)` 计算，乘法使用更宽整数，不把 360/7 先取整。日期在跨过分数边界后的首个整秒切换：第 51 秒仍为第 1 日，第 52 秒为第 2 日，第 360 秒为第 8 日。第 1 年春 1 月 1 日从第 0 秒开始；每个新周从周内第 1 日开始。

调用方只传模拟推进量，不传动画帧时长或上次打开程序的现实时间。主界面展示 `Snapshot` 的季、年、月、日。`GameTimeUnits` 位于 `scripts/time/GameTimeUnits.cs`，是生产模块共用的比例单位：一模拟秒 7 单位、一日 360 单位、半日 180 单位；`RemainingSeconds` 向上取整，保证到期落在两次整秒之间时按首个到达整秒结算。独立日历测试见 `tests/unit/TestGameCalendar.cs`，经营连接测试见 `tests/unit/TestFarmGame.cs`。

`GameDate` 定义位于 `scripts/time/GameDate.cs`，市场读取它显示本次、下次及公告日期。日期换算在累计 `uint32` 日数范围内同样不会回绕；实际经营范围仍由累计 `uint32` 模拟秒限定。节日改期属于 [MarketQuotes](../../market/market-quotes/interface-market-quotes.md) 的报价排期规则，日历模块只提供日期与季节，不管理商品或事件。T09B 补充的独立测试检查两种日期读取的一致性、节日日期和日数上限。

批量协调使用内部 `SecondsUntilNextSeason` 和 `SecondsUntilDay(elapsedDay)`：前者返回严格晚于当前时刻的季界距离，后者将未来日的起点按 `ceil(day×360/7)` 转成相对秒数。乘法与绝对时刻使用宽整数，先求差再转 `uint`，未来日首次整秒可能大于日历上限但距离不会回绕；实际批量请求仍由经营入口整段检查容量。`TryAdvanceSeconds` 继续唯一提交连续区间累计，生产或行情不直接写日历。参见[批量实现](../../game-state/farm-game/implementation-batched-simulation.md)。
