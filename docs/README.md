# 文档导航

本库正文使用中文，路径使用小写英文和连字符。仓库首页介绍运行入口；本页负责定位文档。文件命名与内容归属由 docs/AGENTS.md 约束。

## 游戏规则

- [作物与选种](gameplay/production/crop-growth.md)：七种作物、适宜季节、播种前检查、收获数量和切换规则。
- [农田供水与降雨](gameplay/production/water-and-rain.md)：留水、雨中播种、改种与收获后的水分处理。
- [加工](gameplay/production/processing.md)：对应场地、加工时间、场地详情与投入损失。
- [出售与价格](gameplay/trading/sales.md)：原料与加工品的当日定价、库存与结算。
- [开局与建造](gameplay/land/opening-and-building.md)：预置建筑、建造收费与扩张。
- [地图与操作](gameplay/world/map-and-camera.md)：坐标、选择、镜头与画面含义。

## 代码架构

- [当前系统组织与状态归属](architecture/implementation-system-organization.md)：已落地模块关系和修改入口。
- [FarmGame 接口](architecture/game-state/farm-game/interface-farm-game.md)及[推进顺序](architecture/game-state/farm-game/implementation-tick-order.md)。
- [CropCatalog 接口](architecture/farming/crop-catalog/interface-crop-catalog.md)、[Inventory 接口](architecture/inventory/inventory/interface-inventory.md)与[Wallet 接口](architecture/economy/wallet/interface-wallet.md)：作物定义、分类库存和余额的唯一拥有者。
- [LandOccupancy 接口](architecture/land/land-occupancy/interface-land-occupancy.md)、[PlacementRules 接口](architecture/land/placement-rules/interface-placement-rules.md)、[FarmingSystem 接口](architecture/farming/farming-system/interface-farming-system.md)、[PlantingRules 接口](architecture/farming/planting-rules/interface-planting-rules.md)、[ProcessingSystem 接口](architecture/processing/processing-system/interface-processing-system.md)和[WorkerScheduler 接口](architecture/workers/worker-scheduler/interface-worker-scheduler.md)：地块占用、放置与播种检查、生产状态与旧单工人轮转。
- [市场价格接口](architecture/market/market-price-curve/interface-market-price-curve.md)及[曲线实现](architecture/market/market-price-curve/implementation-bounded-curve.md)。
- [GameCalendar 接口](architecture/time/game-calendar/interface-game-calendar.md)和[GameTimeUnits 接口](architecture/time/game-time-units/interface-game-time-units.md)：经营日历、暂停与生产共用的精确时间比例。
- [MapCoordinates 接口](architecture/world/map-coordinates/interface-map-coordinates.md)：格坐标与地图本地坐标的统一换算。
- [WorldMap 接口](architecture/world/world-map/interface-world-map.md)及[分块缓存实现](architecture/world/world-map/implementation-chunk-cache.md)。
- [镜头输入接口](architecture/world/camera-controller/interface-camera-controller.md)。
- [主界面接口](architecture/ui/main/interface-main.md)、[可拖动窗口](architecture/ui/draggable-window/interface-draggable-window.md)、[建造目录](architecture/ui/build-catalog-window/interface-build-catalog-window.md)、[库存窗口](architecture/ui/inventory-window/interface-inventory-window.md)、[市场窗口](architecture/ui/market-window/interface-market-window.md)与[可点击 HTML 原型](architecture/ui/main/prototype-main.html)：场景命令分发、固定控件刷新、建造摆放、窗口拖动与位置记忆。选种及两类详情的接口由主界面文档继续导航。
- [NPC 角色场景接口](architecture/characters/npc-character/interface-npc-character.md)及[独立预览操作](architecture/ui/npc-preview/interface-npc-preview.md)：角色动画、移动和切换图集。

列出全部接口文档：`rg --files docs/architecture -g 'interface-*.md'`。新增模块时按模块路径添加具名接口文档；实现细节另写 `implementation-` 前缀文件。

## 项目协作与依据

- [系统规划](project/roadmap.md)、[构建与验收](project/build-and-validation.md)、[GitHub 协作](project/contribution-workflow.md)。
- [素材来源记录](project/asset-sources.md)：已纳入的第三方素材、出处与授权信息。
- [macOS 构建与验收](project/macos-build.md)：Universal 2 导出、CI 和应用包启动。
- [市场曲线调研](research/market-price-curve.md)、[测试分类调研](research/test-taxonomy.md)。
- [EditorConfig 检查](static-checks/editorconfig.md)、[CI 静态检查](static-checks/ci.md)。
- [规则加载与链接对应表](../.codex/rule-loading.md)。
