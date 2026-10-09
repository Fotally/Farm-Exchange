# TradingService 完整交易接口

对应类型：`FarmExchange.Trading.TradingService`，代码位于 `scripts/trading/TradingService.cs`。模块在程序集内可见，接受本局唯一的 `Inventory`、`Wallet` 与 `MarketQuotes`；`FarmGame` 是主场景交易入口。它把数量合法性、当前报价、余额与分类库存容量的完整结算收在一个模块内。

| 成员 | 调用约定 |
| --- | --- |
| `Buy(CommodityId, int quantity)` | 买入十四商品之一，数量须为 1～`int.MaxValue`。以执行时当前报价检查库存可容纳、余额可支付后，一次扣款并增加公共库存。 |
| `Sell(CommodityId, int quantity)` | 同一报价卖出指定数量；检查公共库存足够与余额可容纳收入后，一次扣库存并入账。原料保留底线不限制主动出售。 |
| `SellAll(CommodityId)` | 按当前公共库存数量完整卖出指定商品；空库存成功返回零。无效商品正常拒绝。 |
| `SellAllProducts()` | 先汇总七种成品数量与当前价收入，检查聚合收入容量后再提交；任何容量失败保留全部成品，原料始终不受影响。空库存成功返回零。 |
| `SellAllProductsDetailed()` | 同一原子全售实现返回 `ProductSaleResult`：Trade 为原合计，Lines 为按作物目录排列的七条只读真实明细。原 SellAllProducts 只取其中合计，不执行第二笔结算。 |
| `BuyOrder(CommodityId, int quantity, int reserveCents, int frozenCents = 0)` | 委托买入路径；含费后可用现金不得低于锁定保留线。一次单可使用自身冻结资金，持续单传零。成功释放本单全部冻结差额。 |
| `SellOrder(CommodityId, int quantity, int frozenQuantity = 0)` | 委托卖出路径；费用从货值扣除，钱包容量按净收入检查。可使用自身冻结库存；持续单传零；成功释放本单冻结。 |

公开结果保留 `TradeResult(TradeFailure Failure, long Quantity, long TotalCents)` 三参数构造与解构，`FeeCents` 结果属性默认零。`TotalCents` 始终是未扣手续费的货值；买入支出为总额加费用，卖出入账为总额减费用。`Success` 仅在 `Failure.None` 时为真，`ErrorMessage` 给出中文原因。正常拒绝分为 `InvalidCommodity`、`InvalidQuantity`、`InsufficientFunds`、`InsufficientStock`、`InventoryCapacityExceeded`、`WalletCapacityExceeded`、`CashReserveNotMet`；失败数量、总额与费用均为零，金币、全部库存和冻结额度零修改。负现金保留金额是内部调用约定错误，抛参数异常。`SellAll` 的空库存与显式 `Sell(..., 0)` 含义不同，后者拒绝非法数量。

可选结果属性 `int? UnitPriceCents` 表示此次单商品买卖真正读取的单价，单位为分。`Buy/BuyOrder` 的非法商品、非正数量、库存容量提前拒绝尚未读取报价，属性为 null；成功及后续资金不足/现金保留拒绝携带实际价。`Sell/SellOrder` 的非法商品、非正数量、库存不足提前拒绝同样为 null；钱包容量拒绝和成功保留原取价处的实际值。`SellAll` 非空复用卖出，空库存成功没有读取报价，价格为 null。拒绝仍没有成交量、货值或费用，已读取价不表示成交；null 与零不同，现行报价至少 1 分。日志有值才投影，不按失败码猜分支或另查记录时价格。

`ProductSaleResult.Lines` 的七条 `TradeLineResult` 包含 Commodity、Quantity、UnitPriceCents、ValueCents。价格直接保留原全售汇总循环读取值，空库存商品同样保留此真实读取；成功记录实际卖出份数/货值，聚合容量拒绝时每条份数/货值均为零，价格仍为预检实际价。合计 Trade.UnitPriceCents 为 null，不把多商品合計当作一种价格。结果不携带日志字段或资源快照，不重新取价。实际返回只读数组，不与后续经营状态共享可变集合。

成交价不保存于库存，不按历史买入价结算；报价查询不会抽随机。即时买卖和全部出售仍零费；只有委托成交收取[委托手续费](../../../gameplay/trading/orders.md)，不设价差，交易量不影响外部报价。手动交易只能使用可用金币、可用库存，全部出售只卖可用部分。数量、费用和金额聚合用 `long`，钱包余额、单商品库存和建造费仍为 `int`；检查金额不超过可支付或可入账的整数容量后才转换并提交，无事务回滚层。全成品售出的累计数量也以 `long` 汇总，避免预检阶段溢出。

交易可在暂停时执行，但不推进日历、报价、生产或加工领取。买入原料直接进入同一公共库存，下一经营领取阶段使用保留底线与稳定格序；随后新建场地继续使用既有即时领取路径。已经投入加工的原料不在公共库存内，不能出售。

`FarmGame.Buy`、`Sell`、`SellCommodityAll` 原样返回完整结果。旧 `SellRaw` 与 `SellAll` 委托该模块，并返回 `SaleResult(int Quantity, int RevenueCents, TradeFailure Failure)`，保留数量与收入字段、补充 `Success` 和中文失败原因。成功交易的每份价格至少 1 分，因此累计数量不超过可入账收入，旧成功结果也可安全表示为 `int`；失败结果统一为零。

上述经营入口增加可选 CommandOrigin，默认 Player；来源仅允许 Player/Scenario，非法值在任何指令记录与业务提交前抛参数异常。经营层的局部执行手续负责一次业务调用及原异常传播，领域日志观察请求和真实结果，不执行交易。全部加工品只记录一次 AllProductsSold 及七条明细，兼容入口不重复发出单商品交易事件。新增 `TestTradeLogging` 覆盖来源、冻结下全售、实际价、聚合明细和真实 File 日志；经营规则仍由原 `TestTradingService` 验证。

库存接口见[统一公共库存](../../inventory/inventory/interface-inventory.md)，委托配置和相位见[委托模块](../trade-order-book/interface-trade-order-book.md)，经营顺序见[经营步进](../../game-state/farm-game/implementation-tick-order.md)。`tests/unit/TestTradingService.cs` 验证十四商品收支守恒、失败零修改、库存与钱包容量、全成品出售完整性、执行时价格、暂停交易与买入后的加工领取，以及费用取整、含费精确余额、现金保留、净收入容量和即时零费。

## 有界规则诊断（#130）

`SetLogging(TradingLog?)` 在局绑定后接入同一领域观察入口。只有显式选择 `RuleChecked` 且 `IncludeGameEvents=true` 的开发采集才创建 `TradeRuleObservation`；未选择、关闭或 runtime 采集不构造检查字典。公开采集范围与预算归 `DiagnosticCapture`，交易模块不持有第二份采集状态。

`BuyCore` 沿原顺序只执行商品、数量、库存容量、资金、现金保留检查；`SellCore` 只执行商品、数量、可用库存、钱包容量检查。每个原分支把已得到的布尔事实连同 `TradeRuleCheck` 交给观察，首次拒绝立即输出已执行项，未走到的项不出现。全部通过时在实际资源提交前输出检查通过，因此 `RuleChecked.Outcome=Success` 不能证明已经成交，正式结果仍看 `TradeFinished/OrderFilled`。观察不返回判断、不捕获业务提交，也不继续短路或重读报价。

诊断覆盖固定单商品交易、委托及两类全售。`RequestMode` 保留 `Fixed/AllCommodity/AllProducts` 范围：单商品全售非法输入只记录 `Commodity=false`，空库存成功只记录实际执行的 `Commodity=true`；非空复用卖出核心。全部加工品在原聚合容量检查处只记录 `Capacity`，没有单一商品，故省略 `Commodity`，不增加商品遍历或假数量检查。所有路径仍各有原 runtime 完成结果。

`TestTradeDiagnostics` 验证首次拒绝的实际检查字典、成功路径、空/非法/聚合全售及容量拒绝的零资源修改、未选择与停止后的关闭，以及不同采集模式/坏输出下的结果、异常与平静推进等价；统一验收执行结果另行记录。
