# TradingService 完整交易接口

对应类型：`FarmExchange.Trading.TradingService`，代码位于 `scripts/trading/TradingService.cs`。模块在程序集内可见，接受本局唯一的 `Inventory`、`Wallet` 与 `MarketQuotes`；`FarmGame` 是主场景交易入口。它把数量合法性、当前报价、余额与分类库存容量的完整结算收在一个模块内。

| 成员 | 调用约定 |
| --- | --- |
| `Buy(CommodityId, int quantity)` | 买入十四商品之一，数量须为 1～`int.MaxValue`。以执行时当前报价检查库存可容纳、余额可支付后，一次扣款并增加公共库存。 |
| `Sell(CommodityId, int quantity)` | 同一报价卖出指定数量；检查公共库存足够与余额可容纳收入后，一次扣库存并入账。原料保留底线不限制主动出售。 |
| `SellAll(CommodityId)` | 按当前公共库存数量完整卖出指定商品；空库存成功返回零。无效商品正常拒绝。 |
| `SellAllProducts()` | 先汇总七种成品数量与当前价收入，检查聚合收入容量后再提交；任何容量失败保留全部成品，原料始终不受影响。空库存成功返回零。 |

公开结果为 `TradeResult(TradeFailure Failure, long Quantity, long TotalCents)`，`Success` 仅在 `Failure.None` 时为真，`ErrorMessage` 给出中文原因。正常拒绝分为 `InvalidCommodity`、`InvalidQuantity`、`InsufficientFunds`、`InsufficientStock`、`InventoryCapacityExceeded`、`WalletCapacityExceeded`；失败数量与金额均为零，金币和全部库存零修改。`SellAll` 的空库存与显式 `Sell(..., 0)` 含义不同，后者拒绝非法数量。

成交价不保存于库存，不按历史买入价结算；报价查询不会抽随机。暂不收手续费、不设价差，交易量不影响外部报价。数量与金额聚合用 `long`，钱包余额、单商品库存和建造费仍为 `int`；检查金额不超过可支付或可入账的整数容量后才转换并提交，无事务回滚层。全成品售出的累计数量也以 `long` 汇总，避免预检阶段溢出。

交易可在暂停时执行，但不推进日历、报价、生产或加工领取。买入原料直接进入同一公共库存，下一经营领取阶段使用保留底线与稳定格序；随后新建场地继续使用既有即时领取路径。已经投入加工的原料不在公共库存内，不能出售。

`FarmGame.Buy`、`Sell`、`SellCommodityAll` 原样返回完整结果。旧 `SellRaw` 与 `SellAll` 委托该模块，并返回 `SaleResult(int Quantity, int RevenueCents, TradeFailure Failure)`，保留数量与收入字段、补充 `Success` 和中文失败原因。成功交易的每份价格至少 1 分，因此累计数量不超过可入账收入，旧成功结果也可安全表示为 `int`；失败结果统一为零。

库存接口见[统一公共库存](../../inventory/inventory/interface-inventory.md)，经营顺序见[经营步进](../../game-state/farm-game/implementation-tick-order.md)。`tests/unit/TestTradingService.cs` 验证十四商品收支守恒、失败零修改、库存与钱包容量、全成品出售完整性、执行时价格、暂停交易与买入后的加工领取。
