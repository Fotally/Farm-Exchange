# Inventory 对外接口

对应类型：`FarmExchange.Inventory.Inventory`，代码位于 `scripts/inventory/Inventory.cs`。它是每局游戏的原料与加工品分类库存的唯一拥有者，由 `FarmGame` 调用；模块在程序集内可见。

| 成员 | 约定 |
| --- | --- |
| `GetRaw(crop)`、`GetProduct(crop)` | 查询指定作物的原料或加工品数量，不暴露可变数组。 |
| `AddRaw(crop, quantity)`、`AddProduct(crop, quantity)` | 收获或完成加工时入库；拒绝无效作物、负数和整数溢出，失败时数量不变。 |
| `TryTakeRawForProcessing(crop)` | 有原料时取出一份供匹配场地加工，返回是否成功。 |
| `TakeAllRaw(crop)`、`TakeAllProducts()` | 在 `FarmGame` 完成出售计价与入账后，清空对应原料或全部加工品。 |

`Inventory` 不决定价格，也不持有金币。现行两类库存没有保留量；`FarmGame` 继续按原顺序协调生产与出售。交易玩家规则见[出售与价格](../../../gameplay/trading/sales.md)。
