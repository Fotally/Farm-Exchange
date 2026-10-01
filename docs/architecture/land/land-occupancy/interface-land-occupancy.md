# LandOccupancy 对外接口

对应类型：`FarmExchange.Land.LandOccupancy`，代码位于 `scripts/land/LandOccupancy.cs`。每局由 `FarmGame` 持有一份固定 128×128 格的主要占用表；模块在程序集内可见。

| 成员 | 约定 |
| --- | --- |
| `Get(index)` | 返回一格的主要占用类别：空地、农田、加工场地或道路；只读且不影响工人任务。格坐标由 `FarmGame` 验证后转换为索引。 |
| `Place(index, building)` | 接受农田、加工场地或道路；三者独占主要占用，已占用时拒绝写入。 |
| `Remove(index)` | 清除已有主要占用；空地调用属于内部错误。 |
| `Clear()` | 仅供满地图测试夹具重建占用表。 |

本模块不持有农田或加工的作物、进度，也不判断建造费用。`PlacementRules` 读取本表、地图范围及余额，统一返回放置原因；`FarmGame` 在同一经营命令中协调占用与对应生产状态。道路只登记或释放本表，不创建 `FarmingSystem` / `ProcessingSystem` 状态。主要占用类别由 `BuildingKind` 表示，道路在原枚举之后追加为 `Road`，不改变旧值。

当前道路不影响工人速度、碰撞或可达性，`WorkerScheduler` 不读取道路以改变计时。未来确认道路属性时，可在工人内部移动计时的接入位置读取这份土地信息；保持一秒推进与只读工人快照 Interface，不另复制一份道路占用，也不通过 UI 或动画计算经营速度。
