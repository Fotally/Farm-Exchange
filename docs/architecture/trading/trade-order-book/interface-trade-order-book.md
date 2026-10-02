# TradeOrderBook 委托模块接口

实现为 `scripts/trading/TradeOrderBook.cs`，程序集内可见；`FarmGame` 提供公共命令。模块唯一维护委托配置、原现金基准、每单冻结归属、稳定 ID、活动状态与最近成交。金币总量和聚合冻结只由 `Wallet` 持有，商品总量和聚合冻结只由 `Inventory` 持有；这里不复制持仓、钱包或第二份库存。

## Interface

| 成员 | 调用约定 |
| --- | --- |
| `Create(TradeOrderRequest)` | 完整检查配置、一次目标差额、预算、资源及保留；成功后冻结一次单并分配稳定 ID，持续单不冻结。基准取建单时总现金。创建不执行交易。 |
| `Update(int id, TradeOrderRequest)` | 活动单整体替换；原 ID、顺序和现金基准不变。同为一次单且商品、方向、数量模式与值、预算模式未变时，保留原锁定数量；只改价格、条件或保留设置不重算目标差额。数量意图发生变化时按新设置确定数量。允许使用该单原冻结；不能使用其他单冻结。先完整检查，再提交；失败原单、资源、结果完全不变。 |
| `Cancel(int id)` | 释放本单冻结，状态变为 `Cancelled`，保留查询记录。终态或不存在的 ID 正常拒绝。 |
| `SetEnabled(int id, bool enabled)` | 仅活动持续策略可启停。一次单通过撤销结束；终态不可操作。 |
| `GetSnapshots()` | 返回独立只读快照列表，按建单顺序，包含终态记录；请求和条件组防御复制为只读集合，不暴露可变内部状态。 |
| `Execute(CalendarSnapshot)` | 每未暂停经营秒调用一次，在生产、领取、工人、日历和行情之后；按顺序每单最多尝试一次，后单读取前单真实提交后的资源。暂停直接返回。 |

公共入口为 `FarmGame.GetTradeOrders`、`CreateTradeOrder`、`UpdateTradeOrder`、`CancelTradeOrder`、`SetTradeOrderEnabled`。命令返回 `TradeOrderCommandResult(Success, Id, ErrorMessage)`；成功无错误消息，正常拒绝有中文原因且零修改。不以提交命令推进时间。

## 数据和结果

`TradeOrderRequest` 包含 `Commodity`、`Side`、`Frequency`、`QuantityMode`、`Quantity`、`BudgetMode`、`BudgetCents`、`LimitPriceCents`、`ReserveMode`、`ReserveValue` 和 `ConditionGroups`。金额为整数分，数量为整数份，比例为 0～100 整数百分比。一次买单选择 `FixedBudget` 或 `LimitPrice`；其他单使用 `None`。固定预算买单在成交时算数量，要求数量模式为 `Fixed`，`Quantity` 不参与计算。限价一次单必须设置正最高买价；持续买入靠价格条件表达，不设置冻结预算。

`TradeOrderCondition(Factor, Comparison, Value)` 支持价格、总库存、季节；价格和库存为非负阈值并支持五种比较，季节为 `Season` 枚举值且仅等于。至少一组，每组至少一个条件；不提供任意嵌套策略语言。

`TradeOrderSnapshot` 包含 ID、只读请求、`Waiting/Disabled/Completed/Cancelled` 状态、原现金基准、锁定保留额、一次锁定数量、冻结金币和数量、当前等待原因和可空的最近成交 `LastFill`。`TradeOrderFillSnapshot(Commodity, Side, Trade, BalanceCents)` 保存成交当时的真实商品、方向、结算结果及成交后总余额；编辑配置保留原成交对象，下一次成功成交才替换。显示旧成交必须读取该对象，不能按当前请求推测方向或商品。固定预算数量在成交前未知，`LockedQuantity` 为零；成交真实数量看 `LastFill.Trade.Quantity`。持续单数量每次动态计算，不依赖该锁定字段。终态清零冻结归属并保留结果。仅保存最近一条，不新增交易历史列表。

## Implementation 约束

条件组内全部满足、组间任一满足；同一单多组成立只执行一次。最高限价同时约束一次买单实际价格。条件成立之后才调用[交易结算](../trading-service/interface-trading-service.md)；数量为零时等待，容量、资源、费用和现金保留未通过时不部分成交。等待不清除最近成功记录。

一次固定预算包含费用，私有整数二分计算 `数量×价格 + ceil(数量×价格/100) ≤ 预算` 的最大数量，不通过缩量绕过容量或现金保留。最高限价冻结与实际成交均使用 `long` 预检；最终冻结不超过真实可用 `int` 余额才转换。比例保留按原现金基准向上取整到分，修改保留参数仍使用原基准。

编辑先只读预检新配置并纳入原单冻结额度，全部通过后释放旧归属和登记新归属；这是单线程经营命令的完整提交，不引入通用事务、回滚或第二份资金存储。

玩家规则和唯一定值见[委托与持续策略](../../../gameplay/trading/orders.md)。`tests/unit/TestTradeOrders.cs` 通过完整命令验证预算、限价、目标、条件、顺序、暂停、冻结隔离、编辑失败不变、现金基准和整数容量；`TestTradingService.cs` 验证实际费用、净入账和即时零费回归。
