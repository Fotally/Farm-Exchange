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

## 日志观察

`FarmGame` 通过 `GameLog.Orders` 观察四种命令的原输入与真实返回，复用共用命令身份和幂等终结。`TradeOrderBook.AttachLog` 只组装本局观察入口；订单仍唯一拥有条件判断、执行顺序、冻结归属和状态变化。`TradeOrderLog` 自己读取必要只读快照并维护字段投影，业务不组装日志字典。字段含义和预算以[运行时 schema v1](../../../project/runtime-log-schema-v1.md)为准。

创建/编辑记录原请求与生效配置；拒绝编辑的有效前后配置保持一致。同 ID 跨商品编辑同时观察原商品与请求新商品的库存变化，顶层库存始终针对原商品，两种商品的总量、可用量、冻结量放入 `OrderCommodityStocks`；非法商品保留原标识，库存为 null，不进入库存查询。原现金基准、本单冻结与全局冻结分别记录，不互相代替。

成功编辑在 `OrderEdited` 前封存旧配置的待合并等待尾段并清理旧基线；新配置的首次实际等待重新记录，即使 ID 和原因类别未变。配置替换本身不产生 `Resolved`。拒绝编辑不封窗、不重置原基线或中间变化额度，原配置的后续真实观察继续进入同一窗口。

`Execute` 在原判断位置形成 `TradeOrderEvaluation`：保留组内全部条件实际求值、组间首个成功组返回的行为。全部组失败才保留实际失败因素；后续限价、零数量与结算失败仅记录真实走到的阻塞类别。`TradeOrderBlocker` 表达业务阻塞，结算失败沿用 `TradeFailure`；中文等待说明保持原行为，日志不反向解析，也不补求值。`Complete` 仅在全部条件组确已访问且真实结算成功时使用，其余为 `ActualShortCircuit`。价格只使用真实 `TradeResult.UnitPriceCents`。

自动成交在资源完整结算、冻结差额释放和订单状态更新后输出一次 `OrderFilled`；不额外输出主动交易或玩家命令。实际等待首次出现、稳定类别集合变化及成交恢复才输出 `OrderWaitChanged`；创建的“等待下一经营秒检查”、暂停及平静批量段不构成实际检查。同原因数值变化不新增变化事件；取消/停用使用自身生命周期事件结束观察，不伪造 `Resolved`。重新启用后第一次实际等待重建基线。

每单每现实 60 秒最多逐条输出 8 次中间变化，首次和成交恢复不占该额度。超过额度后累计省略的变化次数及合并段内每次真实观察的原因次数，同类重复观察也计入观察次数但不计变化次数；不把次数换算为经营秒或精确等待时长。窗口在下一次该单真实观察时检查到期，输出合并尾段并重置额度；真实成交、成功编辑、取消、停用和局结束前也先输出待合并尾段。每局独立持有去重状态，成交恢复、成功编辑、取消、停用和局结束清理相应观察。

配置投影仅保留原顺序前 8 组、总 32 条条件。截断元数据记录准确字段路径和数组原 `ItemCount`，不修改合法业务配置或阻止命令执行；收到参数和领域结果分别标明各自路径。`OrderEvaluated` 专项诊断按下节的 #130 约定采集逐条件实际值。

`TestOrderLogging` 验证真实命令、冻结/成交采样、跨商品编辑、短路与等待原因、预算及尾段、成功编辑的旧尾段顺序与新基线、拒绝编辑保持原窗口、配置截断与非法输入、跨局隔离和关闭采集等价；窗口测试通过内部时间依赖推进现实窗口，不等待真实一分钟。生命周期尾段通过固定配置下的真实库存变化产生，不通过反复编辑制造原因变化。

## 数据和结果

`TradeOrderRequest` 包含 `Commodity`、`Side`、`Frequency`、`QuantityMode`、`Quantity`、`BudgetMode`、`BudgetCents`、`LimitPriceCents`、`ReserveMode`、`ReserveValue` 和 `ConditionGroups`。金额为整数分，数量为整数份，比例为 0～100 整数百分比。一次买单选择 `FixedBudget` 或 `LimitPrice`；其他单使用 `None`。固定预算买单在成交时算数量，要求数量模式为 `Fixed`，`Quantity` 不参与计算。限价一次单必须设置正最高买价；持续买入靠价格条件表达，不设置冻结预算。

`TradeOrderCondition(Factor, Comparison, Value)` 支持价格、总库存、季节；价格和库存为非负阈值并支持五种比较，季节为 `Season` 枚举值且仅等于。至少一组，每组至少一个条件；不提供任意嵌套策略语言。

`TradeOrderSnapshot` 包含 ID、只读请求、`Waiting/Disabled/Completed/Cancelled` 状态、原现金基准、锁定保留额、一次锁定数量、冻结金币和数量、当前等待原因和可空的最近成交 `LastFill`。`TradeOrderFillSnapshot(Commodity, Side, Trade, BalanceCents)` 保存成交当时的真实商品、方向、结算结果及成交后总余额；编辑配置保留原成交对象，下一次成功成交才替换。显示旧成交必须读取该对象，不能按当前请求推测方向或商品。固定预算数量在成交前未知，`LockedQuantity` 为零；成交真实数量看 `LastFill.Trade.Quantity`。持续单数量每次动态计算，不依赖该锁定字段。终态清零冻结归属并保留结果。仅保存最近一条，不新增交易历史列表。

## Implementation 约束

条件组内全部满足、组间任一满足；同一单多组成立只执行一次。最高限价同时约束一次买单实际价格。条件成立之后才调用[交易结算](../trading-service/interface-trading-service.md)；数量为零时等待，容量、资源、费用和现金保留未通过时不部分成交。等待不清除最近成功记录。

一次固定预算包含费用，私有整数二分计算 `数量×价格 + ceil(数量×价格/100) ≤ 预算` 的最大数量，不通过缩量绕过容量或现金保留。最高限价冻结与实际成交均使用 `long` 预检；最终冻结不超过真实可用 `int` 余额才转换。比例保留按原现金基准向上取整到分，修改保留参数仍使用原基准。

编辑先只读预检新配置并纳入原单冻结额度，全部通过后释放旧归属和登记新归属；这是单线程经营命令的完整提交，不引入通用事务、回滚或第二份资金存储。

玩家规则和唯一定值见[委托与持续策略](../../../gameplay/trading/orders.md)。`tests/unit/TestTradeOrders.cs` 通过完整命令验证预算、限价、目标、条件、顺序、暂停、冻结隔离、编辑失败不变、现金基准和整数容量；`TestTradingService.cs` 验证实际费用、净入账和即时零费回归。

## 批量重复检查

`BeginAdvanceRequest()` 标记新请求需要检查当前活动订单，以覆盖请求之间的正式经营命令；`NeedsNextTickCheck` 只返回是否必须在下一秒执行完整订单相位。`Execute` 每次完整检查后保存是否有任意订单真实成交；一次单和持续单都可能改变前序等待单依赖的库存、现金或可用冻结额度，只要仍有等待单，下一秒就继续完整检查。成交不会让已检查的前单在本秒重试，保持每秒创建顺序、每单最多一次、逐笔手续费与最后成交信息；全部订单已结束或停用时不为成交额外安排检查。

全部等待单未成交且库存、资金、报价及季节不变时，平静段可省略等价失败检查；任何其他业务事件仍按原相位无条件调用 `Execute`。新请求重新检查一次，不持有第二份依赖状态或交易公式。`TestBatchedSimulation` 通过公开经营入口覆盖后序一次卖出唤醒补货、一次买入唤醒卖出、限价买单释放冻结后唤醒现金保留策略，并比较逐秒、整段和不同切段结果；全等待用例同时验证平静优化与冻结保持。详见[批量实现](../../game-state/farm-game/implementation-batched-simulation.md)。

## 选定订单真实求值诊断（#130）

`TradeOrderLog.BeginEvaluation` 先按事件名和选定 `OrderId` 请求公共门禁，通过后才建立 `OrderEvaluationObservation`。原 `UnmetConditions` 在每个真实组/条件位置交付 `Factor/Comparison/Value` 和已经算出的 `Actual/Satisfied`；组内仍全部计算，组间仍在首个成功组停止。日志只采集前八组、合计三十二条实际条件样本，采样达到上限也不改变业务循环。未访问的后续 OR 组不伪装成失败或日志截断。

`ConfiguredConditionGroupCount` 为原配置组数；`EvaluatedConditionGroupCount` 是本次真实访问的组数；`EvaluatedConditionCount` 是已访问组中实际求值的条件总数。二维 `Conditions` 保留原顺序，采样截断元数据保留实际访问集合和相应组的原数量。大合法配置仍由原业务完整求值，不以诊断预算拒绝配置。

`ConditionGroupsSatisfied` 直接保存原条件组判断的返回结果；`ConditionSatisfied` 表示本次全部成交前提通过，同时要求条件组通过且实际结果没有限价、数量或结算阻塞。限价、预算不足或资金不足等待时，前者可以为 true，后者仍为 false；条件组未通过时两者均为 false，真实成交时两者均为 true。两项均只投影原分支事实，不重新求值。`ComputedQuantity` 来自原代码本次实际计算的候选量，即使条件失败原业务也已计算；真实零保留为零。`ComputedBudgetCents` 仅在执行了固定预算数量计算时携带该次输入预算，其余分支省略，不用零冒充尚未计算。等待、结算拒绝和成功均在各自真实结果位置提交一次；已提交订单仍用 `OrderFilled` 作成交证据。

`EvaluationCoverage` 沿用 #127 的完整业务检查含义。即使全部配置组都已访问，条件失败后尚未执行后续交易检查，仍为 `ActualShortCircuit`；反过来，首个 OR 组成功并完成成交，也可能因后续 OR 组未访问而保留 `ActualShortCircuit`。只有原业务访问全部条件组且真实结算成功时才标 `Complete`，不为改变覆盖标记补跑检查。专项测试分别核对这些情形与两项满足标记；逐条件 `Satisfied` 断言按独立键匹配，避免误把 `ConditionSatisfied` 或 `ConditionGroupsSatisfied` 的后缀当成样本。

此专项记录使用 VRB，只针对选中订单的真实调用；不为暂停、停用或 quiet 段补做求值。`IsDiagnosticOrderActive(id)` 只查已有订单状态，等待和停用返回 true，终态/不存在返回 false，供公共采集清理对象使用，不复制订单全集。`TestTradeDiagnostics` 覆盖 OR 短路、条件通过后限价/预算/结算等待、真实成交、未选订单、有界大配置样本及 quiet 保持，执行结果以统一验收记录为准。
