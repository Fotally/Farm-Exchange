# WorldMap 对外接口

对应类型：FarmExchange.World.WorldMap，代码位于 scripts/world/WorldMap.cs。该模块负责地图表现、屏幕输入与节点坐标变换、格子选择及镜头范围，不保存或推进经营状态。等距格坐标公式统一由 [MapCoordinates](../map-coordinates/interface-map-coordinates.md) 提供。

| 成员 | 输入与输出 | 调用约定 |
| --- | --- | --- |
| SetGame | FarmGame | 初始化地图的视觉状态；主场景启动时调用 |
| SyncFromGame | 无参数 | 经营命令或 tick 结束后调用；只读取快照并更新外观 |
| SelectAtScreenPosition | 屏幕坐标 | 选择有效格后发出 SelectionChanged(cell) |
| GetCellWorldCenter | 有效格坐标 → 全局位置 | 在地图本地格中心上应用节点变换 |
| GetGridWorldPosition | 分数格位置 → 全局位置 | 调用统一坐标换算并应用地图节点变换；供工人快照和视觉插值定位，不取整 |
| ClampGlobalCameraCenter | 全局镜头中心 → 限制后的全局位置 | 先换算到地图本地网格，再应用地图节点变换返回 |
| LocalBounds | 无参数 → 地图本地外接矩形 | 供地图范围检查和测试，不表示全局矩形 |

WorldMap 只通过 FarmGame.GetPlot 读取经营状态。SyncFromGame 的次数、镜头位置及可见块数量不得影响农田、加工、库存、价格或日期。绘制缓存的内部结构见[实现](implementation-chunk-cache.md)，格子坐标与玩家操作见[地图规则](../../../gameplay/world/map-and-camera.md)。

主场景在地图下组合[WorkerPresentation](../worker-presentation/interface-worker-presentation.md)，其角色随地图变换且覆盖地块网格。地图块可见性只影响块绘制，不隐藏或停止经营工人；工人经营位置由调度 Module 持有，地图不判断到达或作业完成。
