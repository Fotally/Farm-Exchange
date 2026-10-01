# 文档导航

本库正文使用中文，路径使用小写英文和连字符。仓库首页介绍运行入口；本页负责定位文档。文件命名与内容归属由 docs/AGENTS.md 约束。

## 游戏规则

- [作物与选种](gameplay/production/crop-growth.md)：七种作物、适宜季节、播种前检查、越季失败与恢复、收获数量和切换规则。
- [农田供水与降雨](gameplay/production/water-and-rain.md)：留水、雨中播种、改种与收获后的水分处理。
- [加工](gameplay/production/processing.md)：对应场地、加工时间、原料保留底线、领取顺序、场地详情与投入损失。
- [独立报价与消息](gameplay/trading/market-quotes.md)：十四商品初价、供需与成本因素、双周行情、节日改期和真实公告。
- [即时买卖与出售](gameplay/trading/sales.md)：同价买卖、公共库存、完整结算与失败原因。
- [开局与建造](gameplay/land/opening-and-building.md)：预置建筑、建造收费与扩张。
- [道路铺设与移除](gameplay/land/roads.md)：灰色道路、逐格连续铺设、独占与拆除，以及当前不影响生产和移动的规则。
- [地图与操作](gameplay/world/map-and-camera.md)：坐标、选择、镜头与画面含义。

## 代码架构

- [当前系统组织与状态归属](architecture/implementation-system-organization.md)：已落地模块关系和修改入口。
- [FarmGame 接口](architecture/game-state/farm-game/interface-farm-game.md)及[推进顺序](architecture/game-state/farm-game/implementation-tick-order.md)。
- [CropCatalog 接口](architecture/farming/crop-catalog/interface-crop-catalog.md)、[Inventory 接口](architecture/inventory/inventory/interface-inventory.md)与[Wallet 接口](architecture/economy/wallet/interface-wallet.md)：作物定义、分类库存和余额的唯一拥有者。
- [LandOccupancy 接口](architecture/land/land-occupancy/interface-land-occupancy.md)、[PlacementRules 接口](architecture/land/placement-rules/interface-placement-rules.md)、[FarmingSystem 接口](architecture/farming/farming-system/interface-farming-system.md)、[PlantingRules 接口](architecture/farming/planting-rules/interface-planting-rules.md)、[ProcessingSystem 接口](architecture/processing/processing-system/interface-processing-system.md)和[WorkerScheduler 接口](architecture/workers/worker-scheduler/interface-worker-scheduler.md)：地块占用、放置与播种检查、生产状态和三人工人调度。
- [三人工人移动与调度实施方案](architecture/workers/worker-scheduler/implementation-movement-proposal.md)：已确认初始速度、工作耗时、独占认领、布局保证与稳定接口。
- [商品目录接口](architecture/market/commodity-catalog/interface-commodity-catalog.md)、[报价接口](architecture/market/market-quotes/interface-market-quotes.md)与[报价实现](architecture/market/market-quotes/implementation-quote-cycle.md)：十四商品、实际排期和真实因素消息。
- [完整交易接口](architecture/trading/trading-service/interface-trading-service.md)：一次命令封装资源检查、执行时价格与原子结算。
- [历史市场曲线接口](architecture/market/market-price-curve/interface-market-price-curve.md)及[曲线实现](architecture/market/market-price-curve/implementation-bounded-curve.md)：保留独立曲线测试，正式经营改走报价模块。
- [GameCalendar 接口](architecture/time/game-calendar/interface-game-calendar.md)和[GameTimeUnits 接口](architecture/time/game-time-units/interface-game-time-units.md)：经营日历、暂停与生产共用的精确时间比例。
- [MapCoordinates 接口](architecture/world/map-coordinates/interface-map-coordinates.md)：格坐标与地图本地坐标的统一换算。
- [WorldMap 接口](architecture/world/world-map/interface-world-map.md)及[分块缓存实现](architecture/world/world-map/implementation-chunk-cache.md)。
- [WorkerPresentation 接口](architecture/world/worker-presentation/interface-worker-presentation.md)：三人工人快照、坐标换算、动画与插值展示。
- [镜头输入接口](architecture/world/camera-controller/interface-camera-controller.md)。
- [主界面接口](architecture/ui/main/interface-main.md)、[可拖动窗口](architecture/ui/draggable-window/interface-draggable-window.md)、[建造目录](architecture/ui/build-catalog-window/interface-build-catalog-window.md)、[库存窗口](architecture/ui/inventory-window/interface-inventory-window.md)、[市场窗口](architecture/ui/market-window/interface-market-window.md)与[可点击 HTML 原型](architecture/ui/main/prototype-main.html)：场景命令分发、固定控件刷新、建造摆放、窗口拖动与位置记忆。选种及两类详情的接口由主界面文档继续导航。
- [道路详情接口](architecture/ui/road-details-panel/interface-road-details-panel.md)：固定用途说明与移除意图，不读取作物或加工状态。
- [NPC 角色场景接口](architecture/characters/npc-character/interface-npc-character.md)及[独立预览操作](architecture/ui/npc-preview/interface-npc-preview.md)：角色动画、移动和切换图集。

列出全部接口文档：`rg --files docs/architecture -g 'interface-*.md'`。新增模块时按模块路径添加具名接口文档；实现细节另写 `implementation-` 前缀文件。

## 项目协作与依据

- [系统规划](project/roadmap.md)、[构建与验收](project/build-and-validation.md)、[GitHub 协作](project/contribution-workflow.md)。
- [独立行情与即时交易实施方案](project/market-proposal.md)：T09 的价格、事件、节日排期与交易规则已确认，正在实施。
- [素材来源记录](project/asset-sources.md)：已纳入的第三方素材、出处与授权信息。
- [macOS 构建与验收](project/macos-build.md)：Universal 2 导出、CI 和应用包启动。
- [市场曲线调研](research/market-price-curve.md)、[测试分类调研](research/test-taxonomy.md)。
- [基础格细分调研](research/grid-subdivision.md)：道路 1/4、1/9 面积的同尺度比较与多形状占用建议，尚未更改运行地图。
- [EditorConfig 检查](static-checks/editorconfig.md)、[CI 静态检查](static-checks/ci.md)。
- [规则加载与链接对应表](../.codex/rule-loading.md)。
