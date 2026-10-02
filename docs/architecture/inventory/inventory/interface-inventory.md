# Inventory 对外接口

对应类型：`FarmExchange.Inventory.Inventory`，代码位于 `scripts/inventory/Inventory.cs`。它是每局游戏的十四种公共商品库存与逐种原料保留底线的唯一拥有者，由 `FarmGame`、加工模块与交易模块调用；模块在程序集内可见。

| 成员 | 约定 |
| --- | --- |
| `Get(commodity)`、`Add(commodity, quantity)`、`Remove(commodity, quantity)` | 查询含冻结的总库存、增加或扣减可用库存；数量为非负 `int`，零数量不修改。无效标识或负数抛参数异常，增加超过 `int.MaxValue` 抛溢出异常，移出超过可用库存抛 `InvalidOperationException`；均在修改前拒绝。 |
| `GetFrozen(commodity)`、`GetAvailable(commodity)` | 查询一次卖单冻结的聚合数量，以及总量减冻结的可用数量。 |
| `TryReplaceFrozen(commodity, previousQuantity, nextQuantity)` | 用该单原冻结替换新额度；可用加原额度不足返回 `false` 且零修改。商品合法，数量非负，原额度不得大于聚合冻结；具体归属由委托模块保证。 |
| `RemoveForOrder(commodity, quantity, frozenQuantity)` | 交易模块完整预检后扣除真实库存并释放该单冻结；仅使用可用数量和本单额度，不使用他单冻结。非法提交抛状态异常。 |
| `GetRaw(crop)`、`GetProduct(crop)` | 查询指定作物的原料或加工品数量，不暴露可变数组。 |
| `AddRaw(crop, quantity)`、`AddProduct(crop, quantity)` | 收获或完成加工时入库；拒绝无效作物、负数和整数溢出，失败时数量不变。 |
| `GetRawReserve(crop)`、`SetRawReserve(crop, quantity)` | 查询或设置该种原料的非负整数底线，默认 0；允许高于现存库存。无效作物或负数抛出参数异常，状态不变；设置不触发加工。 |
| `GetProcessingAvailability(crop)` | 只读返回可领取、没有可用原料或受保留底线限制。可用库存为零优先返回没有原料，不消费库存。 |
| `TryTakeRawForProcessing(crop)` | 与只读可领取判断共用规则：可用原料严格多于底线时取出一份，否则失败且不改变总量或冻结。 |
| `TakeAllRaw(crop)`、`TakeAllProducts()` | 清空可用部分，保留冻结；正式交易由 `TradingService` 完整预检后按数量扣减。 |

公开值类型 `CommodityId(CropKind Crop, CommodityKind Kind)` 位于 `scripts/inventory/CommodityId.cs`，类别为 `Raw` 或 `Product`；`IsDefined` 是商品是否合法的唯一判断。统一入口与 `GetRaw`、`AddProduct` 等既有生产入口共用两份分类数组，不新增交易持仓。每种库存上限为 `int.MaxValue`。

`Inventory` 不决定价格，也不持有金币。加工保留底线只限制自动加工；手动出售可扣减底线内的可用部分，且不清除底线。一次卖单冻结与加工底线分别维护，手动交易和加工都不能消费冻结商品。买入原料进入同一公共库存，操作不触发加工。`FarmGame` 按经营领取阶段协调加工；新建场地保持立即执行同一全场领取路径。完整交易由[交易模块](../../trading/trading-service/interface-trading-service.md)协调，玩家规则见[库存、行情与即时交易](../../../gameplay/trading/sales.md)和[委托策略](../../../gameplay/trading/orders.md)。

`tests/unit/TestResources.cs` 验证统一与既有接口共用库存、默认底线、低于/等于/高于底线、逐种独立、非法值与整数上限；`TestTradingService.cs` 验证交易失败零修改、手动出售底线库存与买入后的领取时点；`TestTradeOrders.cs` 验证冻结、编辑释放及手动交易和加工隔离。
