# LandOccupancy 对外接口

对应类型：`FarmExchange.Land.LandOccupancy`，代码位于 `scripts/land/LandOccupancy.cs`。每局由 `FarmGame` 持有一份固定 128×128 格的主要占用表；模块在程序集内可见。

| 成员 | 约定 |
| --- | --- |
| `Get(index)` | 返回一格的主要占用类别：空地、农田或加工场地。格坐标由 `FarmGame` 验证后转换为索引。 |
| `Place(index, building)` | 仅接受现有农田或加工场地；已占用时拒绝写入。 |
| `Remove(index)` | 清除已有主要占用；空地调用属于内部错误。 |
| `Clear()` | 仅供满地图测试夹具重建占用表。 |

本模块不持有农田或加工的作物、进度，也不判断建造费用。`FarmGame` 在同一经营命令中协调占用与对应生产状态；玩家可见的放置检查仍由现有命令完成，统一结果属于后续 T04C。道路的实际占用类型由 #45 决定，本轮不加入。
