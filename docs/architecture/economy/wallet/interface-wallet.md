# Wallet 对外接口

对应类型：`FarmExchange.Economy.Wallet`，代码位于 `scripts/economy/Wallet.cs`。每局游戏仅由 `FarmGame` 持有一份余额；模块在程序集内可见。

| 成员 | 约定 |
| --- | --- |
| 构造参数 `initialCents` | 设置初始余额，拒绝负数；当前开局仍为 50.00 金币。 |
| `BalanceCents` | 查询总余额，包含冻结，单位为分。 |
| `FrozenCents`、`AvailableCents` | 一次买单冻结的聚合金额，以及总余额减冻结的可用金额。 |
| `TrySpend(amountCents)` | 拒绝负数；可用余额不足返回 `false` 且不扣款，足够时扣款并返回 `true`，不能使用冻结金币。 |
| `Credit(amountCents)` | 拒绝负数或余额整数溢出；失败时余额不变。 |
| `TryReplaceFrozen(previousCents, nextCents)` | 用本单原额度替换新冻结额；可用资金加原额度不足时 `false` 且零修改。额度须为非负，原额度不得大于聚合冻结；本单归属由委托模块保证。冻结不扣总余额。 |
| `SpendForOrder(expenseCents, frozenCents)` | 交易模块完整预检后的提交；扣实际含费支出并释放本单全部冻结。只可使用可用现金和本单额度，非法提交抛状态异常。 |

`Wallet` 不决定建筑价格与商品价格。`FarmGame` 通过 `MoneyCents`、`AvailableMoneyCents`、`FrozenMoneyCents` 提供总量、可用和冻结；建造预检使用可用资金。[委托模块](../../trading/trade-order-book/interface-trade-order-book.md)仅记录每单额度归属，不持第二份钱包；[交易模块](../../trading/trading-service/interface-trading-service.md)统一完整结算。总余额与冻结均只在这里维护。
