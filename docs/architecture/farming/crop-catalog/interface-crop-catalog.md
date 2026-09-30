# CropCatalog 对外接口

对应类型：`FarmExchange.Farming.CropCatalog`，代码位于 `scripts/farming/CropCatalog.cs`。当前由 `FarmGame` 使用，模块在程序集内可见；不需要单独的 C# interface 类型。

| 成员 | 约定 |
| --- | --- |
| `Crops` | 返回七种作物的只读定义列表：原六种顺序不变，萝卜列在末尾。 |
| `Get(crop)` | 返回指定作物定义；无效作物抛出 `ArgumentOutOfRangeException`。 |
| `IsDefined(crop)` | 检查作物编号是否在当前定义范围内，供经营命令在修改状态前校验。 |

作物名称、适宜季节、加工场地、生长天数、单次收获量、加工半天数和价格倍率以[作物表](../../../gameplay/production/crop-growth.md)为准。`GrowingSeasons` 标志随作物定义返回，由 `PlantingRules` 解释；`FarmGame.Crops` 和 `FarmGame.GetCrop` 保留查询入口并委托本模块。
