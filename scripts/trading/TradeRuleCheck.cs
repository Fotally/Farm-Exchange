namespace FarmExchange.Trading;

/**
 * <summary>完整交易真实执行的检查种类，供观察实际通过或拒绝事实。</summary>
 */
internal enum TradeRuleCheck { Commodity, Quantity, Capacity, Funds, Reserve, Stock }

/**
 * <summary>真实交易入口采用的数量范围。</summary>
 */
internal enum TradeRuleScope { Fixed, AllCommodity, AllProducts }
