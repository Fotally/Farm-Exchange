# Wallet 对外接口

对应类型：`FarmExchange.Economy.Wallet`，代码位于 `scripts/economy/Wallet.cs`。每局游戏仅由 `FarmGame` 持有一份余额；模块在程序集内可见。

| 成员 | 约定 |
| --- | --- |
| 构造参数 `initialCents` | 设置初始余额，拒绝负数；当前开局仍为 50.00 金币。 |
| `BalanceCents` | 查询当前余额，单位为分。 |
| `TrySpend(amountCents)` | 拒绝负数；余额不足返回 `false` 且不扣款，足够时扣款并返回 `true`。 |
| `Credit(amountCents)` | 拒绝负数或余额整数溢出；失败时余额不变。 |

`Wallet` 不决定建筑价格与商品价格。`FarmGame` 仍负责建造条件、交易计价和命令结果，并通过原 `MoneyCents` 属性向界面提供余额。
