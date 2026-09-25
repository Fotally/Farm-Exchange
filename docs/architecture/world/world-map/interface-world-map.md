# WorldMap 对外接口

对应类型：FarmExchange.World.WorldMap，代码位于 scripts/world/WorldMap.cs。该模块只负责地图表现、坐标换算、格子选择及镜头范围，不保存或推进经营状态。

| 成员 | 输入与输出 | 调用约定 |
| --- | --- | --- |
| SetGame | FarmGame | 初始化地图的视觉状态；主场景启动时调用 |
| SyncFromGame | 无参数 | 经营命令或 tick 结束后调用；只读取快照并更新外观 |
| SelectAtScreenPosition | 屏幕坐标 | 选择有效格后发出 SelectionChanged(cell) |
| CellAtWorld | 地图本地坐标 → 格坐标 | 供选格与坐标测试使用 |
| ClampCameraCenter、WorldBounds | 世界坐标或无参数 → 镜头中心、地图范围 | 供镜头控制调用 |

WorldMap 只通过 FarmGame.GetPlot 读取经营状态。SyncFromGame 的次数、镜头位置及可见块数量不得影响农田、加工、库存、价格或日期。绘制缓存的内部结构见[实现](implementation-chunk-cache.md)，格子坐标与玩家操作见[地图规则](../../../gameplay/world/map-and-camera.md)。
