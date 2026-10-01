# Inventory 对外接口

对应类型：`FarmExchange.Inventory.Inventory`，代码位于 `scripts/inventory/Inventory.cs`。它是每局游戏的原料与加工品分类库存的唯一拥有者，由 `FarmGame` 调用；模块在程序集内可见。

| 成员 | 约定 |
| --- | --- |
| `GetRaw(crop)`、`GetProduct(crop)` | 查询指定作物的原料或加工品数量，不暴露可变数组。 |
| `AddRaw(crop, quantity)`、`AddProduct(crop, quantity)` | 收获或完成加工时入库；拒绝无效作物、负数和整数溢出，失败时数量不变。 |
| `GetRawReserve(crop)`、`SetRawReserve(crop, quantity)` | 查询或设置该种原料的非负整数底线，默认 0；允许高于现存库存。无效作物或负数抛出参数异常，状态不变；设置不触发加工。 |
| `GetProcessingAvailability(crop)` | 只读返回可领取、没有原料或受保留底线限制。空库存优先返回没有原料，不消费库存。 |
| `TryTakeRawForProcessing(crop)` | 与只读可领取判断共用规则：公共原料严格多于底线时取出一份，否则失败且不改变库存。 |
| `TakeAllRaw(crop)`、`TakeAllProducts()` | 在 `FarmGame` 完成出售计价与入账后，清空对应原料或全部加工品。 |

`Inventory` 是公共原料、加工品和逐种保留底线的唯一拥有者，不决定价格，也不持有金币。保留只限制自动加工；`TakeAllRaw` 可以出售保留部分，且不清除底线。`FarmGame` 按经营领取阶段协调加工；新建场地保持立即执行同一全场领取路径。交易玩家规则见[出售与价格](../../../gameplay/trading/sales.md)。

`tests/unit/TestResources.cs` 验证默认底线、低于/等于/高于底线、逐种独立、非法值与整数上限，以及出售不受底线限制。
