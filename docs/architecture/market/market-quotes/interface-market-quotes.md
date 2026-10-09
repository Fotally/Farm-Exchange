# MarketQuotes 对外接口

对应 `FarmExchange.Market.MarketQuotes`，实现位于 `scripts/market/MarketQuotes.cs`，只读结果位于 `MarketSnapshot.cs`。来源为 [T09B #71](https://github.com/Fotally/Farm-Exchange/issues/71)，履行[独立报价与真实消息规则](../../../gameplay/trading/market-quotes.md)。

| 成员 | 调用约定 |
| --- | --- |
| `MarketQuotes(int seed, uint elapsedDays = 0)` | 建立一局十四商品初始行情；输入来自日历的完整经过日数，可从初价依次回放到该模拟日期。同一市场种子和日期得到相同结果，不读取现实时间。 |
| `Advance(CalendarSnapshot, MarketLog? = null)` | 在未暂停时推进到日历的经过日数；可跨过多期，按顺序执行公告与报价。相同日期不修改状态，未暂停时倒退日期抛出 `ArgumentOutOfRangeException`。暂停快照直接保持原状。可选的本局行情记录入口只观察实际公告和正式报价提交。 |
| `GetQuote(CommodityId)` | 返回独立的 `MarketQuoteSnapshot`，含商品标识、当前价分、上次价分与 `decimal` 实际涨跌百分比；无效标识抛出 `ArgumentOutOfRangeException`。 |
| `GetSnapshot()` | 返回最近及下次实际报价日期、目录顺序的只读报价集合，以及可空的最近公告；集合与公告文字均独立于后续内部状态。 |

`MarketNewsSnapshot` 包含 `PublishedDate`、`QuoteDate` 和中文只读 `Lines`；初始行情没有公告，首次公告后保留最近一次直到下一公告替换。`GameDate` 是日历提供的不可变日期，不由 UI 重新计算日期。

经营入口唯一持有本局报价模块，在生产结算和日历推进后传入日历快照；主动买卖和 UI 只读当前报价，不传产量、成交量或库存。暂停主动买卖不调用行情推进。调用方不认识事件类型、随机顺序、因素数组或节日算法。

初始化回放按发生过的报价期数线性耗时，跳过没有公告或报价的日子；正常逐秒推进只在实际前一日和报价日处理本期工作。读取快照复制固定十四项，不推进随机状态；初始化回放不构造历史公告字符串或历史快照，不缓存逐期历史。

内部设计见[排期与报价实现](implementation-quote-cycle.md)。公开行为测试位于 `tests/unit/TestMarketQuotes.cs`，包括因素独立复算、成本传导、整数分限幅、事件资格与跨季持续、节日和暂停、读取不可变性、逐日与跳转一致性以及模拟秒上限回放。

内部 `NextEventDay` 返回当前排期下一次公告准备或正式报价的累计绝对游戏日，已准备时指向报价，否则指向报价前一日。经营批量推进据此定位事件 tick，日历比例由日历模块转换，不由行情维护经营秒数。无行情事件的区间可一次 `Advance` 到终点；每次实际事件仍沿原 `AdvanceTo` 顺序调用，不合并随机抽取或报价递推。参见[批量实现](../../game-state/farm-game/implementation-batched-simulation.md)。

## 行情日志（#127）

`FarmGame` 在两条真实推进路径传入同一 `GameLog.Market`。`MarketQuotes` 仍唯一拥有行情、排期和随机源；`MarketLog` 是日志领域 Adapter，维护事件描述、日期和商品字段投影，不计算价格或维护另一份行情。

公告准备实际完成后产生一次 `NewsPublished`；正式价格与下一排期实际提交后，按目录顺序为十四商品各产生一次 `QuoteUpdated`，即使某商品本次价格未变也保留正式更新记录。正式报价的因素来自本轮已锁定供需、事件及待发布原料价，原料季节按本次实际报价日查询。记录发生在下一期准备之前，不读取下一批随机结果。字段与单位见 [schema v1](../../../project/runtime-log-schema-v1.md)。

构造器按已有日期回放只建立初始化基线，不绑定行情记录入口，也不补造过去的公告或报价事件。初始化日期已处于公告与报价之间时，之后实际发生的正式报价会记录，但不会补发过去公告。查询、重复日期、暂停和已结束的局不产生行情记录；日志关闭或写入故障不改变行情结果。

`NewsPublished` 与 `QuoteUpdated` 为 runtime/debug 共用结果；#130 的 `QuoteCalculated` 仅按下节约定显式采集。测试入口 `TestMarketLogging.RunChecks()` 通过真实经营推进和日志 formatter 核对公告/生效分离、十四商品实际因素、节日和跨年、初始化历史、暂停恢复、局身份、日志开关与写入故障、批量与逐秒等价；执行结果以统一验收记录为准。

## 待发布报价计算诊断（#130）

只有显式选择 `QuoteCalculated` 且 `IncludeGameEvents=true` 的开发采集，才在每种商品的原准备分支完成后构造明细。原料先算、加工品后算，计算面对下一实际报价日，事件头日期仍是当前经营日；不把准备价提前写成正式价。正式 `QuoteUpdated` 的提交点和字段保持原约定。

供需抽样、季节、事件贡献与原分子都由原业务局部变量交付，`RoundAndClamp` 通过额外整数输出给出其实际四舍五入值和最终上下限；日志不重算、不重复抽随机数。`CalculationInputs` 按 schema 区分 `RawFormula/ProductFormula`，两者共用初价、旧价、供需/事件贡献、分子/分母、舍入/上下限/最终价；原料带真实季节贡献，加工品带对应原料的目录初价和本轮待发布价。构造期历史回放仍不绑定日志，也不补记过去计算。

`TestTradeDiagnostics` 在真实公告日检查十四商品计算与尚未提交的旧报价，随后核对正式报价等于先前投影的最终价；关闭、runtime、development 和坏输出的多期报价、完整经营结果与 quiet 计数保持一致，并通过真实 File provider 输出样例。测试中的整数复算仅用于验证日志证据，不进入业务或日志实现；实际执行状态由统一验收记录维护。
