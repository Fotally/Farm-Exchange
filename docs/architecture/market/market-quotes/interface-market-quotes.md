# MarketQuotes 对外接口

对应 `FarmExchange.Market.MarketQuotes`，实现位于 `scripts/market/MarketQuotes.cs`，只读结果位于 `MarketSnapshot.cs`。来源为 [T09B #71](https://github.com/Fotally/Farm-Exchange/issues/71)，履行[独立报价与真实消息规则](../../../gameplay/trading/market-quotes.md)。

| 成员 | 调用约定 |
| --- | --- |
| `MarketQuotes(int seed, uint elapsedDays = 0)` | 建立一局十四商品初始行情；输入来自日历的完整经过日数，可从初价依次回放到该模拟日期。同一市场种子和日期得到相同结果，不读取现实时间。 |
| `Advance(CalendarSnapshot)` | 在未暂停时推进到日历的经过日数；可跨过多期，按顺序执行公告与报价。相同日期不修改状态，未暂停时倒退日期抛出 `ArgumentOutOfRangeException`。暂停快照直接保持原状。 |
| `GetQuote(CommodityId)` | 返回独立的 `MarketQuoteSnapshot`，含商品标识、当前价分、上次价分与 `decimal` 实际涨跌百分比；无效标识抛出 `ArgumentOutOfRangeException`。 |
| `GetSnapshot()` | 返回最近及下次实际报价日期、目录顺序的只读报价集合，以及可空的最近公告；集合与公告文字均独立于后续内部状态。 |

`MarketNewsSnapshot` 包含 `PublishedDate`、`QuoteDate` 和中文只读 `Lines`；初始行情没有公告，首次公告后保留最近一次直到下一公告替换。`GameDate` 是日历提供的不可变日期，不由 UI 重新计算日期。

经营入口唯一持有本局报价模块，在生产结算和日历推进后传入日历快照；主动买卖和 UI 只读当前报价，不传产量、成交量或库存。暂停主动买卖不调用行情推进。调用方不认识事件类型、随机顺序、因素数组或节日算法。

初始化回放按发生过的报价期数线性耗时，跳过没有公告或报价的日子；正常逐秒推进只在实际前一日和报价日处理本期工作。读取快照复制固定十四项，不推进随机状态；初始化回放不构造历史公告字符串或历史快照，不缓存逐期历史。

内部设计见[排期与报价实现](implementation-quote-cycle.md)。公开行为测试位于 `tests/unit/TestMarketQuotes.cs`，包括因素独立复算、成本传导、整数分限幅、事件资格与跨季持续、节日和暂停、读取不可变性、逐日与跳转一致性以及模拟秒上限回放。

内部 `NextEventDay` 返回当前排期下一次公告准备或正式报价的累计绝对游戏日，已准备时指向报价，否则指向报价前一日。经营批量推进据此定位事件 tick，日历比例由日历模块转换，不由行情维护经营秒数。无行情事件的区间可一次 `Advance` 到终点；每次实际事件仍沿原 `AdvanceTo` 顺序调用，不合并随机抽取或报价递推。参见[批量实现](../../game-state/farm-game/implementation-batched-simulation.md)。
