# 文档导航

本库正文使用中文，路径使用小写英文和连字符。仓库首页介绍运行入口；本页负责定位文档。文件命名与内容归属由 docs/AGENTS.md 约束。

## 游戏规则

- [作物与选种](gameplay/production/crop-growth.md)：七种作物、适宜季节、播种前检查、越季失败与恢复、收获数量和切换规则。
- [农田供水与降雨](gameplay/production/water-and-rain.md)：留水、雨中播种、改种与收获后的水分处理。
- [季节耕作表](gameplay/production/seasonal-cultivation.md)及[窗口操作](gameplay/production/seasonal-cultivation-ui.md)：共享年度安排、两种执行方式、休耕与手动接管，四季纸面时间图、周刻度、居中拖动、未命名草稿编辑、落位前校验、短条悬浮与可读列表；整表删除解除引用，保留当前轮并恢复自动复种。
- [加工](gameplay/production/processing.md)：对应场地、加工时间、原料保留底线、领取顺序、场地详情与投入损失。
- [独立报价与消息](gameplay/trading/market-quotes.md)：十四商品初价、供需与成本因素、双周行情、节日改期和真实公告。
- [即时买卖与出售](gameplay/trading/sales.md)：同价买卖、公共库存、完整结算与失败原因。
- [委托与自动交易](gameplay/trading/orders.md)：多因素条件、两种一次买单、资源冻结、原现金基准保留和 1% 成交费用。
- [开局与建造](gameplay/land/opening-and-building.md)：预置建筑、建造收费与扩张。
- [道路铺设与移除](gameplay/land/roads.md)：灰色道路、逐格连续铺设、独占与拆除，以及当前不影响生产和移动的规则。
- [地图与操作](gameplay/world/map-and-camera.md)：坐标、选择、镜头与画面含义；窗口和全屏扩大时增加地图视野，保持农田大小。
- [经营速率与暂停](gameplay/world/simulation-rate.md)：发布0.5×/1×/2×、暂停保留未完成tick进度与工人动画；开发额外倍率入口及现场流程中断语义。

## 代码架构

- [当前系统组织与状态归属](architecture/implementation-system-organization.md)：已落地模块关系和修改入口。
- [FarmGame 接口](architecture/game-state/farm-game/interface-farm-game.md)、[推进顺序](architecture/game-state/farm-game/implementation-tick-order.md)及[等价批量实现](architecture/game-state/farm-game/implementation-batched-simulation.md)：单tick和批量共用相位，平静区间直接累计，真实事件提供稳定检查点。
- [CropCatalog 接口](architecture/farming/crop-catalog/interface-crop-catalog.md)、[Inventory 接口](architecture/inventory/inventory/interface-inventory.md)与[Wallet 接口](architecture/economy/wallet/interface-wallet.md)：作物定义、分类库存和余额的唯一拥有者。
- [LandOccupancy 接口](architecture/land/land-occupancy/interface-land-occupancy.md)、[PlacementRules 接口](architecture/land/placement-rules/interface-placement-rules.md)、[FarmingSystem 接口](architecture/farming/farming-system/interface-farming-system.md)、[PlantingRules 接口](architecture/farming/planting-rules/interface-planting-rules.md)、[ProcessingSystem 接口](architecture/processing/processing-system/interface-processing-system.md)和[WorkerScheduler 接口](architecture/workers/worker-scheduler/interface-worker-scheduler.md)：地块占用、放置与播种检查、生产状态和三人工人调度。
- [内部换季成熟实现](architecture/farming/farming-system/implementation-season-maturity.md)：禁生边界的精确比较、本轮结束预测、共同收获终结与原入库路径。
- [耕作表接口](architecture/cultivation/interface-cultivation-plan-book.md)及[年度事件实现](architecture/cultivation/implementation-annual-events.md)：共享配置、独立草稿排程与完整保存检查、生命周期条编号、整表删除及引用解除、逐田引用与预备缓存、年度环绕、休耕缓冲及逐轮执行重验。
- [耕作表窗口接口](architecture/ui/cultivation-window/interface-cultivation-window.md)及[年度时间图实现](architecture/ui/cultivation-window/implementation-annual-timeline.md)：草稿、共享应用、整条居中拖动、跨季片段与候选完整排程预检；可读条内信息及短片段悬浮提示。
- [三人工人移动与调度实施方案](architecture/workers/worker-scheduler/implementation-movement-proposal.md)：已确认初始速度、工作耗时、独占认领、布局保证与稳定接口。
- [商品目录接口](architecture/market/commodity-catalog/interface-commodity-catalog.md)、[报价接口](architecture/market/market-quotes/interface-market-quotes.md)与[报价实现](architecture/market/market-quotes/implementation-quote-cycle.md)：十四商品、实际排期和真实因素消息。
- [完整交易接口](architecture/trading/trading-service/interface-trading-service.md)：一次命令封装资源检查、执行时价格与原子结算。
- [订单接口](architecture/trading/trade-order-book/interface-trade-order-book.md)：单据创建与原 ID 编辑、冻结归属、条件检查和生命周期。
- [委托窗口接口](architecture/ui/trade-orders-window/interface-trade-orders-window.md)：条件分组、订单管理与真实冻结/成交展示，刷新保留编辑草稿。
- [历史市场曲线接口](architecture/market/market-price-curve/interface-market-price-curve.md)及[曲线实现](architecture/market/market-price-curve/implementation-bounded-curve.md)：保留独立曲线测试，正式经营改走报价模块。
- [GameCalendar 接口](architecture/time/game-calendar/interface-game-calendar.md)和[GameTimeUnits 接口](architecture/time/game-time-units/interface-game-time-units.md)：经营日历、暂停与生产共用的精确时间比例。
- [SimulationDriver 接口](architecture/time/simulation-driver/interface-simulation-driver.md)：唯一倍率与未完成tick进度、稳定检查点、预算限额和玩家/流程改速来源。
- [参数化流程接口](architecture/development/interface-parameterized-scenario.md)与[固定流程实现](architecture/development/implementation-buy-process-sell.md)：严格配置、两种运行对象、日期及等待预算、同步结算和证据检查、只输出JSON报告。
- [配置编辑接口](architecture/development/interface-scenario-configuration-editor.md)与[配置库实现](architecture/development/implementation-scenario-configuration-library.md)：可编辑字段元数据、严格扫描与草稿、Git持久配置、另存覆盖及两种删除的文件归属。
- [开发窗口接口](architecture/ui/developer-tools-window/interface-developer-tools-window.md)：文件选择、启动/中止、当前局与独立数据局接入、真实进度及报告保存结果。
- [MapCoordinates 接口](architecture/world/map-coordinates/interface-map-coordinates.md)：格坐标与地图本地坐标的统一换算。
- [WorldMap 接口](architecture/world/world-map/interface-world-map.md)及[分块缓存实现](architecture/world/world-map/implementation-chunk-cache.md)。
- [WorkerPresentation 接口](architecture/world/worker-presentation/interface-worker-presentation.md)：三人工人快照、坐标换算、动画与插值展示。
- [镜头输入接口](architecture/world/camera-controller/interface-camera-controller.md)：内部封装玩家倍率、窗口适配和输入换算，场景与 UI 无需协调缩放步骤。
- [主界面接口](architecture/ui/main/interface-main.md)、[可拖动窗口](architecture/ui/draggable-window/interface-draggable-window.md)、[建造目录](architecture/ui/build-catalog-window/interface-build-catalog-window.md)、[库存窗口](architecture/ui/inventory-window/interface-inventory-window.md)、[市场窗口](architecture/ui/market-window/interface-market-window.md)与[可点击 HTML 原型](architecture/ui/main/prototype-main.html)：场景命令分发、固定控件刷新、建造摆放、窗口拖动与位置记忆。选种及两类详情的接口由主界面文档继续导航。
- [道路详情接口](architecture/ui/road-details-panel/interface-road-details-panel.md)：固定用途说明与移除意图，不读取作物或加工状态。
- [统一 UI 倍率接口](architecture/ui/ui-scaling/interface-ui-scaling.md)：按原始排版独立设置整体与字体倍率、动态继承及1080P固定像素策略。
- [原型图标](architecture/ui/ui-icons/interface-ui-icons.md)与[设施缩略图](architecture/ui/facility-preview/interface-facility-preview.md)：复用主稿SVG并展示实际快照锚点，保持世界素材。
- [NPC 角色场景接口](architecture/characters/npc-character/interface-npc-character.md)及[独立预览操作](architecture/ui/npc-preview/interface-npc-preview.md)：角色动画、移动和切换图集。

列出全部接口文档：`rg --files docs/architecture -g 'interface-*.md'`。新增模块时按模块路径添加具名接口文档；实现细节另写 `implementation-` 前缀文件。

## 项目协作与依据

- [UI 视觉升级原型](project/ui-visual-prototype.md)：#94 的田园布局与像素木作主稿、离线 HTML 设计及正式 Godot UI/UX 实施范围；本轮保留地图与人物素材。
- [系统规划](project/roadmap.md)、[构建与验收](project/build-and-validation.md)、[GitHub 协作](project/contribution-workflow.md)：包含分类 issue 模板与提交入口。
- [GitHub 议题模板](../.github/ISSUE_TEMPLATE/)：人工与 Agent 共用的六类中文模板；[开源参照与采用理由](research/issue-template-sources.md)记录字段来源和项目适配。
- [后续功能计划](project/deferred-features-plan.md)：原 T11 的四项后续功能；#36 高级交易已验收并人工合并，#42 季节耕作表已实现并验收，其余两项继续暂缓。
- [季节耕作表方案](project/seasonal-cultivation-proposal.md)：#42 的共享年度时间图、拖动编辑、两种表级模式与手动指令、预备安排、统一播种及内部换季成熟补救的已确认范围。
- [多因素委托与自动交易实施方案](project/advanced-trading-proposal.md)：#36 已确认的条件组合、两种买单预算、冻结、现金保留、执行与 1% 成交费用。
- [已完成系统设计与执行计划归档](archive/farm-exchange-system-design-and-execution-plan.md)：T00～T10 与 T12 的设计、实施和验收历史，PR #70 已人工合并。
- [独立行情与即时交易实施方案](project/market-proposal.md)：T09 的价格、事件、节日排期与交易规则已确认并完成验收，PR #70 已人工合并。
- [素材来源记录](project/asset-sources.md)：已纳入的角色素材授权信息及本项目原型 UI 图标、缩略来源。
- [macOS 构建与验收](project/macos-build.md)：Universal 2 导出、CI 和应用包启动。
- [本地开发构建](project/development-build.md)：单项目开发源码排除、Windows/macOS dev导出、外部持久配置和包旁报告；CI不新增dev包。
- [正式版本发布](project/release.md)：独立手动工作流、指定版本号、Windows 与 macOS 同提交成功 CI 产物共同发布及失败处理。
- [市场曲线调研](research/market-price-curve.md)、[测试分类调研](research/test-taxonomy.md)。
- [基础格细分调研](research/grid-subdivision.md)：用户最终选择 64×32 基础格与 3×3（192×96）生产设施，保留各轮比例示例和最小空间 Interface；#75 已完成新地图实现与完整验收。
- [成熟作品的窗口、地图与 UI 缩放调研](research/window-size-and-map-zoom.md)：以一手资料区分窗口变大、分辨率、地图镜头与界面缩放，记录证实行为和证据边界；用于显示策略讨论。
- [开发者工具与双构建设计草案](project/developer-tools-proposal.md)及[构建机制调研](research/developer-tools-builds.md)：#97的历史设计与构建依据；本次速率、固定流程和dev/release边界已由#100/#101实施，单步、诊断与日志等候选保持独立范围，现状见[主计划](project/developer-tools-parameterized-tests.md)。
- [公开 C# 开发工具案例](research/developer-tools-csharp-cases.md)及[源码组织讨论](project/developer-tools-csharp-organization.md)：历史案例比较与单项目源码组织依据；本次development目录编译排除的实际配置见[本地开发构建](project/development-build.md)。
- [跨模块自动调试设计补充](project/developer-tools-cross-module-debugging.md)：固定流程、两运行对象和报告的历史设计依据；现行接入与验收见[主计划](project/developer-tools-parameterized-tests.md)及[参数化流程接口](architecture/development/interface-parameterized-scenario.md)。
- [固定流程与可变参数的开发测试设计](project/developer-tools-parameterized-tests.md)及[使用说明](project/parameterized-tests-usage.md)：#100 / #101 的主计划与长期用例、文件选择、两对象固定流程、开发窗口、持久配置引用和JSON报告；最新交付证据记在计划顶部。
- [点击倍率与分类流程配置界面设计](project/developer-tools-flow-editor-design.md)：#103的C分步设计及#104实施计划，公共倍率按钮、分类检索、自动字段表单、另存覆盖与删除确认；功能验收与C原型视觉修正完成记录在计划顶部，原型仅保留独立来源分支。
- [跨 tick 等价批量经营推进方案](project/batched-simulation-proposal.md)：#100 按平静区间累计、跳到事件 tick 完整结算，保留生产、工人、计划、行情和订单的时序；密集成交逐笔处理，统一工人位置公式及模块内部行程实现已确认。
- [地图内容放置预览调研](research/build-placement-preview.md)与[设计方案](project/build-placement-preview-proposal.md)：#83 的设计来源与 #87 的实施：占地属性驱动预览、逐格冲突反馈、通用取消及镜头交互。
- [EditorConfig 检查](static-checks/editorconfig.md)、[CI 静态检查](static-checks/ci.md)、[接口注释格式](static-checks/interface-comments.md)。
- [规则加载与链接对应表](../.codex/rule-loading.md)。
