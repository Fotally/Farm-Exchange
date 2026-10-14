# LandOccupancy 对外接口

对应类型：`FarmExchange.Land.LandOccupancy`，代码位于 `scripts/land/LandOccupancy.cs`。每局由 `FarmGame` 持有一份固定 384×384 基础格的主要占用表；模块在程序集内可见。土地唯一拥有建筑锚点、固定占地、子格到实例的映射及逐格道路材质，不持有作物或加工进度。

| 成员 | 约定 |
| --- | --- |
| `LandOccupancy(cellCount)` | 经营入口传入完整地图格数，索引统一使用 `row * FarmGame.MapSize + col`；读取前由调用方验证格范围。 |
| `Get(index)` | 返回任一基础格的主要占用类别：空地、农田、加工场地或道路；只读且不影响工人任务。 |
| `ResolveAnchorIndex(index)` | 占用子格返回所属整座设施的锚点索引，空地返回 `-1`。 |
| `GetSpace(index)` | 占用子格返回所属整座设施的同一不可变 `BuildingSpaceSnapshot`；空地返回 `null`。 |
| `Instances` | 不可变空间实例列表，按锚点索引升序，一座设施仅出现一次。占地未变时重复读取必须返回同一对象引用；成功建拆使下一次读取发布新快照，失败操作保留引用，旧快照与其中空间对象保持原内容，普通经营 tick 不重新排序或分配此列表。`Clear` 后空快照可复用。 |
| `CheckFootprint(anchorCell, building, orientation=Q0)` | 只读检查：先拒绝非法类型、非法朝向，再确认全部固定占地偏移在地图内，最后检查全部子格为空。生产设施允许 q0～q3，道路仅允许 q0；非法朝向返回 `InvalidOrientation`。不预留格子。 |
| `Place(anchorIndex, building, orientation=Q0, roadMaterial=Dirt)` | 任意合法基础格可作锚点，无三格对齐要求；完整重验后登记一个朝向固定的空间实例及全部子格。道路同时登记固定材质；非道路不保存材质。非法类型、朝向、道路材质或越界抛出 `ArgumentOutOfRangeException`，冲突抛出 `InvalidOperationException`；失败零修改。 |
| `Remove(anyIndex)` | 从任意占用子格释放所属整座设施的全部占地及空间实例；空地调用抛出 `InvalidOperationException`。 |
| `Clear()` | 仅供满地图测试夹具重建占用表；有实例时完整清除并发布空快照，已空时直接保留既有快照引用，包括最后一次拆除后已经查询过的空快照。 |

稳定引用是生产者的 Interface 约定，`FarmGame.GetBuildingSpaces()` 原样转发，不另做列表复制。地图据此跳过无变化的空间同步；该同步创建／删除设施绘制节点、更新道路地面和清除新占地上的环境装饰，并不负责作物生长。生产进度、水分、天气或镜头变化不使土地快照失效，地图仍独立刷新动态外观和可见性，详见[地图缓存实现](../../world/world-map/implementation-chunk-cache.md)。

修改快照实现时必须继续保证「状态不变复用对象、成功建拆后发布新对象、旧对象不被修改」。每次复制列表会使空间优化失效；原地改写列表而保持引用会使消费者漏掉建拆。同数量或同锚点设施替换仍是空间变更，不能用实例数量代替引用判断。本约定不要求比较多次建拆后的净变化，不为此增加全表比较开销。

`BuildingSpaceSnapshot`（`scripts/land/BuildingSpaceSnapshot.cs`）公开 `AnchorIndex`、`Building`、`Orientation`、`AnchorCell`、`Footprint` 和 `WorkCell`。`BuildingOrientation`（`scripts/land/BuildingOrientation.cs`）的 `Q0/Q1/Q2/Q3` 对应世界素材序号，不表示人物方向或屏幕旋转角。朝向由 `LandOccupancy` 唯一持有，只在登记时赋值，建成后不可修改；任一子格和旧列表都读取同一不可变快照，不需要实例替换或额外身份凭据。锚点只是当前空间实例的位置标识；拆除后同锚点重建得到新快照，旧快照的朝向不变，省略方向的重建恢复 q0。生产模块继续维护自身 Revision 来拒绝旧任务，不添加永久实例 ID。

`BuildingFootprint`（`scripts/land/BuildingFootprint.cs`）是固定形状的唯一入口。`Get(building)` 返回不可变 `Offsets` 和 `WorkOffset`：农田与加工场地各为从锚点起 `(0..2, 0..2)` 的九格，工作偏移为中间格 `(1,1)`；道路只有 `(0,0)`，工作偏移也为 `(0,0)`。`WorkCell(anchorCell, building)` 将锚点换算为工作格；空间快照的 `WorkCell` 使用相同定义。未知类型不得获得占地定义。当前四向共享相同 3×3 占地和工作中心。未来若实际引入 3×4 等非方形设施，朝向必须同步改变真实占地，仍由土地几何模块统一提供；本次不新增未引入的尺寸或通用形状框架，调用方不传任意偏移。

本模块不判断建造费用。`PlacementRules` 读取本表和余额，统一返回放置原因；`FarmGame` 在同一经营命令中协调完整占地与锚点上的一份生产状态。道路只登记或释放本表，不创建 `FarmingSystem` / `ProcessingSystem` 状态。主要占用类别由 `BuildingKind` 表示，道路在原枚举之后追加为 `Road`，不改变旧值。

`tests/unit/land/TestLandOccupancy.cs` 通过上述接口验证任意锚点、九子格同实例、四向子格朝向一致、固定工作中心、非法朝向零登记、道路只允许 q0、四角道路、贴边生产设施、部分越界和末端冲突零修改、任意子格整体拆除、道路互斥、重建恢复 q0 与清理、实例排序及旧快照不可变性。

道路占用与材质由本模块唯一维护；`FarmGame` 将成功建拆后的稳定只读实例快照交给 `TerrainMap`，后者仅派生原生导航区域成本，五材质同等偏好。道路不影响工人速度、碰撞或陆地可达性，`WorkerScheduler` 仍唯一维护移动计时，不读取道路改变速度；`TerrainMap` 不接管计时。保持一秒推进与只读工人快照 Interface，不另复制一份业务道路占用，也不通过 UI 或动画计算经营速度。道路准备及最新版本同步见 [FarmGame Interface](../../game-state/farm-game/interface-farm-game.md)。

## 道路材质与批量提交

`RoadMaterial` 提供 `Dirt/Gravel/Cobble/Brick/Slab`，分别为土路、碎石、卵石、红砖、石板。`BuildingSpaceSnapshot.RoadMaterial` 为只读可空枚举：道路必须有合法材质，农田与加工场地为 null。旧单格建造默认 `Dirt`；建成后没有换材质命令，必须先拆后建。同格重建使用新空间快照，旧对象与旧列表仍保留原材质。

道路始终按 1×1 逐格实例登记，不保存操作段编号、起终点、铺设顺序或历史。材质不改变固定占地、费用、工人速度、通行或生产。四邻格连接和稳定纹理变体由地图表现读取正式布局计算，不写回本模块。

整批新建与范围拆除通过 [FarmGame](../../game-state/farm-game/interface-farm-game.md#整批道路命令) 协调：先确认整个请求合法，再逐格登记或移除；正常失败保留稳定空间快照引用。范围内农田、加工场地与空格不属于待拆道路。`TestLandOccupancy.CheckRoadMaterials` 核对五材质逐格登记、默认材质、非法材质零修改、非道路 null 与拆建后的旧快照不变。
