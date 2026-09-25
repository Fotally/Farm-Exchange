# 库存、价格与出售

每种作物分别有原料库存和加工品库存。商店按钮一次卖出当前全部六种加工品；原料不可出售。收入是各加工品“数量 × 当天售价”的总和，金币以分为整数保存并显示两位小数。卖出后已售加工品库存归零。

每秒推进 1 tick，每 10 tick 进入新的一天。第 1 天面粉价为 5.00 金币，此后由[市场曲线](../../architecture/market/market-price-curve/implementation-bounded-curve.md)按种子与天数确定，始终在 1.00～20.00 金币之间，单日涨跌不超过 20%。其余加工品按当天面粉价乘以[作物表](../production/crop-growth.md)中的倍率，以分四舍五入。旧库存若跨日出售，按出售当天售价结算。

面粉当日相对前一天的实际涨跌幅只用于显示分类：小幅低于 5%，中幅为 5% 至不足 15%，大幅为 15% 至 20%。分类不参与价格生成。

交易命令及查询见[FarmGame 接口](../../architecture/game-state/farm-game/interface-farm-game.md)；市场、换日和出售由 tests/unit/TestMarketRules.cs 与 tests/e2e/TestCoreLoop.cs 验证。
