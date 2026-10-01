# LandOccupancy 对外接口

对应类型：`FarmExchange.Land.LandOccupancy`，代码位于 `scripts/land/LandOccupancy.cs`。每局由 `FarmGame` 持有一份固定 384×384 基础格的主要占用表；模块在程序集内可见。土地唯一拥有建筑锚点、固定占地和子格到实例的映射，不持有作物或加工进度。

| 成员 | 约定 |
| --- | --- |
| `LandOccupancy(cellCount)` | 经营入口传入完整地图格数，索引统一使用 `row * FarmGame.MapSize + col`；读取前由调用方验证格范围。 |
| `Get(index)` | 返回任一基础格的主要占用类别：空地、农田、加工场地或道路；只读且不影响工人任务。 |
| `ResolveAnchorIndex(index)` | 占用子格返回所属整座设施的锚点索引，空地返回 `-1`。 |
| `GetSpace(index)` | 占用子格返回所属整座设施的同一不可变 `BuildingSpaceSnapshot`；空地返回 `null`。 |
| `Instances` | 只读空间实例列表，按锚点索引升序，一座设施仅出现一次。无占用修改时重复读取同一缓存；修改后的首次读取复制列表，旧快照保持原内容，普通经营 tick 不重新排序或分配此列表。 |
| `CheckFootprint(anchorCell, building)` | 只读检查固定占地：先拒绝非法类型，再确认全部偏移在地图内，最后检查全部子格为空。返回 `LandFailure`，不预留格子。 |
| `Place(anchorIndex, building)` | 任意合法基础格可作锚点，无三格对齐要求；完整重验占地后登记一个空间实例及全部子格。非法类型或越界抛出 `ArgumentOutOfRangeException`，冲突抛出 `InvalidOperationException`；失败零修改。 |
| `Remove(anyIndex)` | 从任意占用子格释放所属整座设施的全部占地及空间实例；空地调用抛出 `InvalidOperationException`。 |
| `Clear()` | 仅供满地图测试夹具重建占用表。 |

`BuildingSpaceSnapshot`（`scripts/land/BuildingSpaceSnapshot.cs`）公开 `AnchorIndex`、`Building`、`AnchorCell`、`Footprint` 和 `WorkCell`。锚点只是当前空间实例的位置标识；拆除后同锚点重建得到新快照，生产模块继续维护自身 Revision 来拒绝旧任务，不添加永久实例 ID。

`BuildingFootprint`（`scripts/land/BuildingFootprint.cs`）是固定形状的唯一入口。`Get(building)` 返回不可变 `Offsets` 和 `WorkOffset`：农田与加工场地各为从锚点起 `(0..2, 0..2)` 的九格，工作偏移为中间格 `(1,1)`；道路只有 `(0,0)`，工作偏移也为 `(0,0)`。`WorkCell(anchorCell, building)` 将锚点换算为工作格；空间快照的 `WorkCell` 使用相同定义。未知类型不得获得占地定义。后续固定形状在本模块内部定义，调用方不传任意偏移、不旋转占地。

本模块不判断建造费用。`PlacementRules` 读取本表和余额，统一返回放置原因；`FarmGame` 在同一经营命令中协调完整占地与锚点上的一份生产状态。道路只登记或释放本表，不创建 `FarmingSystem` / `ProcessingSystem` 状态。主要占用类别由 `BuildingKind` 表示，道路在原枚举之后追加为 `Road`，不改变旧值。

`tests/unit/TestLandOccupancy.cs` 通过上述接口验证任意锚点、九子格同实例、固定工作中心、四角道路、贴边生产设施、部分越界和末端冲突零修改、任意子格整体拆除、道路互斥、重建与清理、实例排序及旧快照不可变性。

当前道路不影响工人速度、碰撞或可达性，`WorkerScheduler` 不读取道路以改变计时。未来确认道路属性时，可在工人内部移动计时的接入位置读取这份土地信息；保持一秒推进与只读工人快照 Interface，不另复制一份道路占用，也不通过 UI 或动画计算经营速度。
