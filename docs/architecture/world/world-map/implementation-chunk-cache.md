# WorldMap 分块缓存实现

对应[WorldMap 对外接口](interface-world-map.md)的地图表现与 SyncFromGame。地图按 8×8 格划成 16×16 个块，每块缓存自身的视觉状态和带顶点颜色的 ArrayMesh。块尺寸是内部参数，不进入对外接口。

镜头或视窗变化时只更新块可见性，已绘制且外观未变的块复用缓存。SyncFromGame 扫描 16,384 格快照，建筑、农田作物或作物阶段发生变化时标记相应块为脏；RemainingTicks 数值不影响地图网格。可见脏块重绘，屏幕外脏块等进入视野时重绘。选中框和菱形边缘单独绘制，选格不重建地块。

性能优化只延后屏幕外的绘制命令重建，不能延后 FarmGame.AdvanceTick 的经营计算。满地图负载与 FPS 口径见[构建与验收](../../../project/build-and-validation.md)；tests/unit/TestWorldMap.cs 和 tests/performance/ 验证坐标及负载。
