# LandOccupancy 对外接口

对应类型：`FarmExchange.Land.LandOccupancy`，代码位于 `scripts/land/LandOccupancy.cs`。每局由 `FarmGame` 持有一份固定 128×128 格的主要占用表；模块在程序集内可见。

| 成员 | 约定 |
| --- | --- |
| `Get(index)` | 返回一格的主要占用类别：空地、农田或加工场地。格坐标由 `FarmGame` 验证后转换为索引。 |
| `Place(index, building)` | 仅接受现有农田或加工场地；已占用时拒绝写入。 |
| `Remove(index)` | 清除已有主要占用；空地调用属于内部错误。 |
| `Clear()` | 仅供满地图测试夹具重建占用表。 |

本模块不持有农田或加工的作物、进度，也不判断建造费用。`PlacementRules` 读取本表、地图范围及余额，统一返回放置原因；`FarmGame` 在同一经营命令中协调占用与对应生产状态。主要占用类别由 `BuildingKind` 表示，目前只允许农田和加工场地；#45 确定道路规则后再扩展，道路尚不可建造。
