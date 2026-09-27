# MapCoordinates 对外接口

对应类型：`FarmExchange.World.MapCoordinates`，代码位于 `scripts/world/MapCoordinates.cs`。本模块只处理固定 128×128 等距地图的格坐标与地图本地坐标，不读取场景节点或经营状态。地图格数引用 `FarmGame.MapSize`，地块宽 64、高 32 像素。

| 成员 | 输入与输出 | 调用约定 |
| --- | --- | --- |
| `ContainsCell` | 格坐标 → 是否在地图内 | 列、行均在 0..127 才有效。 |
| `CellToLocalCenter` | 有效格坐标 → 地图本地中心 | 越界属于调用错误，抛出参数越界异常；全局位置由 `WorldMap.GetCellWorldCenter` 提供。 |
| `LocalPositionToCell` | 地图本地位置 → 候选格坐标 | 最近格中心取整；可返回越界候选，选格前调用 `ContainsCell`。 |
| `ClampLocalCenter` | 地图本地位置 → 范围内本地中心 | 供 `WorldMap` 限制镜头；沿等距网格坐标夹取，不改变格尺寸。 |

屏幕输入先由 `WorldMap` 的画布变换转成地图本地位置，再调用本模块；格坐标到全局位置由 `WorldMap` 应用节点变换。这里不判断建筑占用、金币或工人可达性。现行占用判断仍由 `FarmGame` 持有，后续 #46 的占用部分另行迁移。
