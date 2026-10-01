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

道路快照显式使用 `BuildingKind.Road`。`SyncFromGame` 将它存入现有块外观值，道路以灰色地块顶点颜色绘制，不生成农田作物圆点或加工场地标记；非农田的作物字段不参与外观。新增与移除道路沿用8×8块变化检测和重建，选框仍独立绘制；道路选择、平移地图与镜头限制没有另一条坐标路径。

`TestRoadMap.RunChecksAsync` 在挂树并等待显示帧后验证相邻块道路同步、平移地图后的道路选择、选框不重建地块、移除与相邻道路保持。headless 覆盖连接与重建；独立有窗口运行还读取实际灰色/空地像素，保存 `coverage/road-map-before.png` 和 `road-map-after.png`。原满地图FPS夹具继续保持三名移动工人与原农田/场地数量，道路图形另由本场景验收。
