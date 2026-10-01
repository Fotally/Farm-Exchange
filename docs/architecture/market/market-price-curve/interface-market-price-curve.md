# MarketPriceCurve 对外接口

对应类型：FarmExchange.Market.MarketPriceCurve，代码位于 scripts/market/MarketPriceCurve.cs。构造函数接受市场种子；GetPriceCents(day) 对任意有效天数直接返回当天面粉价格（分），无需逐日回放。

第 1 天价格为 500 分；有效价格范围为 100～2000 分，单日涨跌幅不超过 20%。同一种子、同一天的结果可重复，查询顺序不改变结果。该模块不管理库存、日期或交易。

这是 T09 接入前的历史独立曲线，保留其实现与独立测试用于记录已交付算法。正式经营行情由 [MarketQuotes](../market-quotes/interface-market-quotes.md) 管理十四商品报价、公告与排期，FarmGame 不再在每日换日时调用这一曲线。

公式、参数和边界依据见[实现](implementation-bounded-curve.md)，候选方案与资料见[调研](../../../research/market-price-curve.md)。
