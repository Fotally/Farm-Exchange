# 文档导航

本库正文使用中文，路径使用小写英文和连字符。仓库首页介绍运行入口；本页负责定位文档。文件命名与内容归属由 docs/AGENTS.md 约束。

## 游戏规则

- [作物与选种](gameplay/production/crop-growth.md)：七种作物、适宜季节、播种前检查、越季失败与恢复、收获数量和切换规则。
- [地区天气](gameplay/world/weather.md)：自然预排、矩形互斥、整田天气判定与程序云雨表现；作物整轮产量见作物专题。
- [农田供水与降雨](gameplay/production/water-and-rain.md)：留水、雨中播种、改种与收获后的水分处理。
- [季节耕作表](gameplay/production/seasonal-cultivation.md)及[窗口操作](gameplay/production/seasonal-cultivation-ui.md)：共享年度安排、两种执行方式、休耕与手动接管，四季纸面时间图、周刻度、居中拖动、未命名草稿编辑、落位前校验、短条悬浮与可读列表；整表删除解除引用，保留当前轮并恢复自动复种。
- [加工](gameplay/production/processing.md)：每批最多9份、锁定投入与等量产出、完整批次时间、原料保留底线、领取顺序、场地详情与投入损失。
- [独立报价与消息](gameplay/trading/market-quotes.md)：十四商品初价、供需与成本因素、双周行情、节日改期和真实公告。
- [即时买卖与出售](gameplay/trading/sales.md)：同价买卖、公共库存、完整结算与失败原因。
- [委托与自动交易](gameplay/trading/orders.md)：多因素条件、两种一次买单、资源冻结、原现金基准保留和 1% 成交费用。
- [开局与建造](gameplay/land/opening-and-building.md)：预置建筑、建造收费与扩张。
- [道路铺设与移除](gameplay/land/roads.md)：五材质、两次点击直线／L 形铺设、投影直线拆除、整段提交及连接预览；工人优先道路选路，速度不变。
- [地图与操作](gameplay/world/map-and-camera.md)：坐标、选择、镜头与画面含义，地图短按由主场景按模式唯一分派；窗口和全屏扩大时增加地图视野，保持农田大小。
- [经营速率与暂停](gameplay/world/simulation-rate.md)：发布0.5×/1×/2×、暂停保留未完成tick进度与工人动画；开发倍率最高16×及现场流程中断语义。

## 代码架构

- [当前系统组织与状态归属](architecture/implementation-system-organization.md)：已落地模块关系和修改入口。
- [共享 UI 工具](architecture/ui/ui-elements/interface-ui-elements.md)：统一主题、控件构造、金额格式与道路材质中文名称。
- [天气接口](architecture/weather/interface-weather-system.md)及[预排与循环实现](architecture/weather/implementation-weather-schedule.md)：具名不可变配置、每局组装、自然／显式模式、当前状态、事件距离与快照引用契约，以及配置字段扩展和预排规模调整的维护要求。
- [地形配置接口](architecture/terrain/interface-terrain-rules.md)：严格 JSON、不可变配置、真实开局保护与纯陆地组合；配置校验不代替地形生成和导航验收。
- [地形与路线接口](architecture/terrain/interface-terrain-map.md)及[静态地形实现](architecture/terrain/implementation-static-terrain.md)：地形读取视图与拥有者能力、保守离岸、原生导航与道路区域成本、异步准备、连续路线及资源生命周期。
- [FarmGame 接口](architecture/game-state/farm-game/interface-farm-game.md)、[推进顺序](architecture/game-state/farm-game/implementation-tick-order.md)及[等价批量实现](architecture/game-state/farm-game/implementation-batched-simulation.md)：单tick和批量共用相位，平静区间直接累计，真实事件提供稳定检查点。
- [CropCatalog 接口](architecture/farming/crop-catalog/interface-crop-catalog.md)、[Inventory 接口](architecture/inventory/inventory/interface-inventory.md)与[Wallet 接口](architecture/economy/wallet/interface-wallet.md)：作物定义、分类库存和余额的唯一拥有者。
- [LandOccupancy 接口](architecture/land/land-occupancy/interface-land-occupancy.md)、[PlacementRules 接口](architecture/land/placement-rules/interface-placement-rules.md)、[FarmingSystem 接口](architecture/farming/farming-system/interface-farming-system.md)、[播种与适季时间规则接口](architecture/farming/planting-rules/interface-planting-rules.md)、[ProcessingSystem 接口](architecture/processing/processing-system/interface-processing-system.md)和[WorkerScheduler 接口](architecture/workers/worker-scheduler/interface-worker-scheduler.md)：地块占用、放置与播种检查、生产状态和三人工人调度。
- [农田工作身份实现](architecture/farming/farming-system/implementation-work-identity-proposal.md)：本轮与未来播种许可分离，同轮浇水和成功动作保持、真正换轮失效。
- [内部换季成熟实现](architecture/farming/farming-system/implementation-season-maturity.md)：禁生边界的精确比较、本轮结束预测、共同收获终结与原入库路径。
- [耕作表接口](architecture/cultivation/interface-cultivation-plan-book.md)及[年度事件实现](architecture/cultivation/implementation-annual-events.md)：共享配置、独立草稿排程与完整保存检查、生命周期条编号、整表删除及引用解除、逐田引用与预备缓存、年度环绕、休耕缓冲及逐轮执行重验。
- [耕作表窗口接口](architecture/ui/cultivation-window/interface-cultivation-window.md)及[年度时间图实现](architecture/ui/cultivation-window/implementation-annual-timeline.md)：草稿、共享应用、整条居中拖动、跨季片段与候选完整排程预检；可读条内信息及短片段悬浮提示。
- [三人工人移动与调度实现](architecture/workers/worker-scheduler/implementation-movement-proposal.md)：已确认初始速度、工作耗时、独占认领、布局保证与稳定接口。
- [商品目录接口](architecture/market/commodity-catalog/interface-commodity-catalog.md)、[报价接口](architecture/market/market-quotes/interface-market-quotes.md)与[报价实现](architecture/market/market-quotes/implementation-quote-cycle.md)：十四商品、实际排期和真实因素消息。
- [完整交易接口](architecture/trading/trading-service/interface-trading-service.md)：一次命令封装资源检查、执行时价格与原子结算，共享委托含费冻结估算和预算反算。
- [日志接口](architecture/logging/interface-logging.md)及[统一输出实现](architecture/logging/implementation-event-output.md)：领域描述与通用输出分离，共用命令关联和一次终结；覆盖主动交易、订单等待/成交、实际行情、[建拆与底线观察](architecture/logging/implementation-building-observation.md)、生产窗口、[耕作与手动接管](architecture/logging/implementation-cultivation-observation.md)、[暂停/倍率和开发流程](architecture/logging/implementation-time-scenario-observation.md)、有界诊断、[生产与工人明细](architecture/logging/implementation-production-diagnostics.md)、真实批次/帧统计及[共用异常观察](architecture/logging/implementation-exception-observation.md)。
- [订单接口](architecture/trading/trade-order-book/interface-trade-order-book.md)：单据创建与原 ID 编辑、冻结归属、条件检查和生命周期；任意成交后重检剩余等待单。
- [委托窗口接口](architecture/ui/trade-orders-window/interface-trade-orders-window.md)：条件分组、订单管理与真实冻结/成交展示，刷新保留编辑草稿。
- [历史市场曲线接口](architecture/market/market-price-curve/interface-market-price-curve.md)及[曲线实现](architecture/market/market-price-curve/implementation-bounded-curve.md)：保留独立曲线测试，正式经营改走报价模块。
- [GameCalendar 接口](architecture/time/game-calendar/interface-game-calendar.md)和[GameTimeUnits 接口](architecture/time/game-time-units/interface-game-time-units.md)：经营日历、暂停与生产共用的精确时间比例。
- [经营到期索引接口](architecture/time/simulation-due-index/interface-simulation-due-index.md)：一年窗口内的固定秒桶、阶段内 FIFO、取消句柄和成功后移除；首批承载成熟与加工完工。
- [生产到期结算实现](architecture/game-state/farm-game/implementation-production-due.md)：农田与加工精确目标、天气分段积分、产出 FIFO、容量异常与原经营相位的衔接，以及真实 FarmGame 溢出续推回归。
- [日志专项验收](architecture/logging/implementation-validation.md)：真实provider故障、1080P满图1×/16×负载与分配矩阵、进程退出与实际Release程序集门禁；运行命令见测试说明。
- [SimulationDriver 接口](architecture/time/simulation-driver/interface-simulation-driver.md)：唯一倍率与未完成tick进度、导航等待门禁、完整秒异常提交凭据、稳定检查点、预算限额和玩家/流程改速来源。
- [参数化流程接口](architecture/development/interface-parameterized-scenario.md)与[固定流程实现](architecture/development/implementation-buy-process-sell.md)：严格配置、两种运行对象、日期及等待预算、同步结算和证据检查、只输出JSON报告。
- [配置编辑接口](architecture/development/interface-scenario-configuration-editor.md)与[配置库实现](architecture/development/implementation-scenario-configuration-library.md)：可编辑字段元数据、严格扫描与草稿、Git持久配置、另存覆盖及两种删除的文件归属。
- [开发窗口接口](architecture/ui/developer-tools-window/interface-developer-tools-window.md)：文件选择、显式重选恢复与草稿冲突保护、启动/中止、当前局与独立数据局接入、真实进度及报告保存结果。
- [MapCoordinates 接口](architecture/world/map-coordinates/interface-map-coordinates.md)：格坐标与地图本地坐标的统一换算。
- [WorldMap 接口](architecture/world/world-map/interface-world-map.md)及[分块缓存实现](architecture/world/world-map/implementation-chunk-cache.md)：清亮 v2 草地、干湿土、七作物三档、七设施四向、候选同向图、人物共同深度排序，以及正式选框/候选共用外围几何。
- [道路连接实现](architecture/world/world-map/implementation-road-appearance.md)：五材质图集、四邻 mask 与稳定变体、跨块局部刷新，以及新建／拆除预览对旧路外观的临时投影。
- [天气表现实现](architecture/world/world-map/implementation-weather-presentation.md)：真实可见区域云影、斜雨及短落地效果，暂停与倍率表现；不执行经营结算。
- [局部动效实现](architecture/world/world-map/implementation-bright-motion.md)与[环境装饰实现](architecture/world/world-map/implementation-environment-decoration.md)：建筑内部层级、逐株根点、真实产出反馈、32类纯环境的随机四向与建造清除。
- [WorkerPresentation 接口](architecture/world/worker-presentation/interface-worker-presentation.md)：三人工人快照、真实成功作业、清亮 v2 动作、脚根排序、暂停与倍率插值；[FarmGame 接口](architecture/game-state/farm-game/interface-farm-game.md)统一定义最近经营秒结果。
- [镜头输入接口](architecture/world/camera-controller/interface-camera-controller.md)：内部封装玩家倍率、窗口适配和输入换算，场景与 UI 无需协调缩放步骤。
- [主界面接口](architecture/ui/main/interface-main.md)、[可拖动窗口](architecture/ui/draggable-window/interface-draggable-window.md)、[建造目录](architecture/ui/build-catalog-window/interface-build-catalog-window.md)、[库存窗口](architecture/ui/inventory-window/interface-inventory-window.md)、[市场窗口](architecture/ui/market-window/interface-market-window.md)与[可点击 HTML 原型](architecture/ui/main/prototype-main.html)：场景命令分发、异常后的实际状态刷新、空地与水域详情、目录详情互斥、共同道路候选、建造摆放及窗口位置记忆。选种及两类详情的接口由主界面文档继续导航。
- [道路详情接口](architecture/ui/road-details-panel/interface-road-details-panel.md)：固定用途说明与移除意图，不读取作物或加工状态。
- [统一 UI 倍率接口](architecture/ui/ui-scaling/interface-ui-scaling.md)：按原始排版独立设置整体与字体倍率、动态继承及1080P固定像素策略。
- [大小地图接口](architecture/ui/map-navigation/interface-map-navigation.md)：真实地形与设施缓存、完整视野框、独立缩放与手势、同局浏览记忆；主场景负责输入优先级、后台按下／拖放取消和镜头导航。
- [原型图标](architecture/ui/ui-icons/interface-ui-icons.md)与[设施缩略图](architecture/ui/facility-preview/interface-facility-preview.md)：图标、农田与选种沿用主稿SVG；加工和道路缩略复用正式PNG，目录与详情共用取图并显示真实锚点及道路材质。
- [镜头编辑输入实现](architecture/world/camera-controller/implementation-editing-input-proposal.md)：真实编辑焦点集中阻断键盘平移，场景树处理帧回归及退出编辑后的恢复。
- [NPC 角色场景接口](architecture/characters/npc-character/interface-npc-character.md)及[独立预览操作](architecture/ui/npc-preview/interface-npc-preview.md)：角色动画、移动和切换图集。

列出全部接口文档：`rg --files docs/architecture -g 'interface-*.md'`。新增模块时按模块路径添加具名接口文档；实现细节另写 `implementation-` 前缀文件。

## 项目协作与依据

- [系统规划](project/roadmap.md)与[后续功能计划](project/deferred-features-plan.md)：当前范围、未确认事项及后续入口。
- [UI视觉基准](project/ui-visual-prototype.md)：已采用的像素田园布局、1080P和固定倍率参考。
- [构建与验收](project/build-and-validation.md)、[.NET SDK、测试与双平台并行 CI](project/continuous-integration.md)、[测试方法](project/testing.md)、[手工游玩验收](project/manual-playthrough.md)。
- [Windows 分发与性能证据实现](project/validation-artifacts-proposal.md)：CI／发布共用启动及干净 ZIP 检查，性能失败后保留本次已生成的报告和截图。
- [GitHub协作](project/contribution-workflow.md)、[PR独立审查skill](../.codex/skills/farm-exchange-review-pr/SKILL.md)、[议题模板](../.github/ISSUE_TEMPLATE/)及[模板来源](research/issue-template-sources.md)：提交PR后先询问是否审查，明确同意才委派。
- [运行资源分发与 PCK 验证](project/runtime-resources.md)：必需资源、文档与退役素材隔离、正式和开发预设及实际包正反检查。
- [素材管理](project/asset-organization.md)、[素材来源](project/asset-sources.md)、[清亮v2基准](research/bright-complete-v2.md)。
- [水域导航的行业实践](research/water-navigation-game-practices.md)：经典游戏的一手资料、成熟导航实现的接入与精度限制；#110 选型依据，尚非功能验收记录。
- [Godot 原生导航核验](research/godot-water-navigation.md)：净空、保守简化、道路区域成本与原生同步的源码和实验依据；现行调用约定见地形接口。
- [大小地图导航调研](research/minimap-navigation.md)：开源与经典游戏依据、左下角全图概览与 M 键大地图设计，以及同次游戏内倍率记忆。
- [键鼠操作登记](project/input-bindings.md)：现有及计划键位、生效上下文、输入冲突与维护入口。
- [大小地图规划](project/map-navigation-proposal.md)：已确认的全屏方向、小地图导航、图层、输入隔离、独立缩放与同局记忆；正式开发与验收进度。
- [大小地图 1080P 原型](architecture/ui/main/prototype-maps.md)：独立浏览器演示，比较居中图册、全屏地图、侧边展开与导航缩放。
- [固定时间桶评估](research/time-wheel-design-evaluation.md)：日桶、秒桶与索引堆的 FIFO 对照、内存口径与测量限制，保留旧排序原型历史。
- [设施朝向实施范围](project/facility-orientation-proposal.md)：仅建前旋转、每座q0起始、四向资源与随机装饰；道路连接规则由 [#143](https://github.com/Fotally/Farm-Exchange/issues/143) 跟踪。
- [水域实现与验收](project/water-terrain-proposal.md)：Godot 原生导航、保守简化与道路优先，保留素材核验与历史原型证据。
- 后续素材与玩法规划：[采收搬运](project/worker-harvest-transport-proposal.md)、[工人加工](project/worker-processing-proposal.md)。
- [macOS构建](project/macos-build.md)、[本地开发构建](project/development-build.md)、[双平台正式发布](project/release.md)。
- [参数化开发测试使用说明](project/parameterized-tests-usage.md)：三步操作、配置管理、长期用例、运行结果及报告；协议见[固定流程接口](architecture/development/interface-parameterized-scenario.md)。
- [运行时日志设计](research/runtime-logging.md)及[schema v1](project/runtime-log-schema-v1.md)：设计依据、模块职责与字段释义；实际采集入口及覆盖范围见[日志接口](architecture/logging/interface-logging.md)，验收步骤见[日志专项验收](architecture/logging/implementation-validation.md)。
- [日志参数与分层耦合调研](research/logging-coupling-practices.md)：业界如何减少重复传参、集中领域投影，并隔离日志底座与子系统的修改；研究保留当时候选，现行实现见日志接口。
- 研究依据：[市场曲线](research/market-price-curve.md)、[测试分类](research/test-taxonomy.md)、[基础格细分](research/grid-subdivision.md)、[窗口与地图缩放](research/window-size-and-map-zoom.md)、[建造预览案例](research/build-placement-preview.md)、[开发构建机制](research/developer-tools-builds.md)、[C#开发工具案例](research/developer-tools-csharp-cases.md)。研究记录保留当时证据，不覆盖现行专题规则。
- [EditorConfig 与提交前 C# 格式验收](static-checks/editorconfig.md)、[CI静态检查、SDK版本与格式错误定位](static-checks/ci.md)、[逐模块覆盖率门禁](static-checks/coverage.md)、[接口注释格式](static-checks/interface-comments.md)、[规则加载对应表](../.codex/rule-loading.md)。
- [历史归档](archive/index.md)：已完成方案、被替代设计和历次验收；内部路径镜像docs原分类。
