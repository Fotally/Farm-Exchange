# 项目背景

- 本项目是 Godot 放置挂机游戏。地图为 384×384 个 64×32 基础格，采用等距视角；农田与加工场地各占 3×3（192×96），道路占一格。
- 已确认的核心循环：玩家建造农田，开局 3 名工人按经营秒移动到田、自动播种和浇水；作物成熟后交给对应场地加工，加工品在商店出售，收入用于建造更多建筑。
- 现有七种作物为小麦、玉米、水稻、马铃薯、向日葵、甘蔗、萝卜。农田独立选种，时长与收获量见 `docs/gameplay/production/crop-growth.md`；原料进入公共库存，可直接出售或免费加工。十四商品各有独立双周报价，供需、季节、事件与原料成本参与目标价格，节日只移动当期报价，消息在实际报价前一日公布，数值以 `docs/gameplay/trading/market-quotes.md` 为准。即时同价零费买卖完整结算，失败金币库存零修改；买入原料下次领取或随后新建场地即时领取，暂停可主动交易。每秒推进一次经营，7 游戏日＝360 秒。
- 开局地图中心赠送 3 座农田和 2 处配套加工场地；农田以 50% 概率出现 3 块同种或 2+1 两种配置，萝卜参与随机。初始金币为 50.00；生产设施每座 10.00 金币，道路每小格 1.00。任意小格可作锚点，点击任一子格操作整座；工人每秒走 3 小格、在田中心播种浇水，原标准田布局的两日预算保持。顶部显示季、年、月、日。
- Godot 引擎目录：`E:\Godot\Godot_v4.7.2-stable_mono_win64`。
- 用户使用中文；项目文档、设计讨论与代码修改说明使用中文。
- UI、原型和图形效果以后以至少 1920×1080（1080P）进行设计、默认分辨率配置与主要验收。窗口扩大时保持设定的 UI 像素倍率；整体 UI 倍率与独立字体倍率由统一接口维护。

# 代码系统结构

模块接口的 XML 注释按元素逐行排版，示例见[接口注释格式](docs/static-checks/interface-comments.md)。

- `scripts/terrain/TerrainRules.cs`：地形 Module 的严格不可变配置，完整检查版本、必填与重复字段、参数组合和调用方提供的真实开局占地／工人起点；每次加载重新读取 JSON，默认含水与显式纯陆地共用契约。修改字段时读取 `docs/architecture/terrain/interface-terrain-rules.md`。
- `scripts/terrain/TerrainMap.cs`、`TerrainReadView.cs` 与内部生成实现：经营局私有持有完整地形，只向消费者提供稳定只读视图；读取视图不提供导航维护、资源释放或向拥有者转换。地形唯一拥有静态水顶点、权威岸线与本局原生导航资源；独立种子、有界生成并按最终岸线检查硬约束。圆脚底加1/256格数值余量，原生RDP以1/32格保守简化并额外离岸1/8格，单次Godot Round合并外扩且创建时认证覆盖，经原生 A*／Funnel 返回完整路线；连续导航按道路占地分区，普通陆地成本2、道路1，建拆只重分组原面并等待正常同步；真实起终点先检查圆净空，再以原生 Clipper 圆内方框分类，逐段扫圆不依赖共享顶点会误报的射线谓词。原生三角形只循环轮换最短边起始索引，保持几何与朝向，减少投影计算的浮点抵消。异步创建依当前地图 `MapChanged` 和全部区域状态确认正常同步，取消或结束释放资源。`LandPosition`／`LandPath` 封装有限精度连续位置、累计距离和采样，静态有理几何不进入移动状态。建造读取完整陆地，表现读取同一地形；接口见 `docs/architecture/terrain/interface-terrain-map.md`。
- `scripts/gameplay/FarmGame.cs`：经营协调模块。含水局通过 CreateAsync 等待地形与原生导航完整就绪，再初始化设施和工人；显式纯陆地复用同一创建流程。日志观察可先单独结束，完整局释放地形后不再推进。成功道路建拆由本局版本及唯一准备任务合并，等待最新原生快照后才恢复经营，不重置已领取行程；失败命令不改准备状态；就绪查询在局已释放时抛出异常。按开局日历季节从 `CropCatalog` 筛选适季作物，保持原种子抽取顺序并初始化中心随机多格设施，协调 384×384 基础格的完整建造拆除；任一子格解析同一锚点实例，保留降雨与稳定锚点领取，成熟和加工完工按共享到期队列 FIFO 逐设施提交；局创建时选择经过验证的不可变天气配置并传入执行器，正式与测试配置共用排程实现，空间及活动天气快照转发保留引用稳定性约定。封装年度表管理与整表删除：解除引用、保留当前作物进度并恢复自动复种；提供独立草稿排程检查与完整保存验证、批量应用及两种手动接管，成熟与换季促熟共用收获入库路径；工人动作后记录实际计划播种，换季清理后同步日期事件。换日推进报价，完整交易委托交易模块；提供执行重验、实际扣费及地块/实例/详情快照；道路整段新建共用预检并一次重验扣费登记，范围拆除仅移除实际道路且不退款。满地图夹具生成 16,384 生产实例、占用 147,456 子格并检查状态一致。
- `scripts/gameplay/SimulationAdvanceResult.cs`：完整经营检查点、实际推进与宽整数产出汇总。`FarmGame.AdvanceTicks` 请求无外部输入区间，生产到期索引与其他状态模块提供最近事件；平静区间推进绝对时钟，不逐设施扣减生产时间，事件复用完整经营相位；单秒与批量共用天气权威状态，天气开始、结束和雨田供水接入真实事件，平静区间累计同一精确天气时间。
- `scripts/gameplay/ProductionResult.cs`：最近完整经营秒的播种、浇水、收获及加工成功记录。`FarmGame` 唯一持有非持久只读结果，任务成功路径与正式入库路径写入；平静尾段清空，消费方去重并重验有效性，不能由待执行任务或动画推断成功。
- `scripts/farming/CropCatalog.cs`：七种作物定义的唯一入口，包含适宜季节，提供只读作物表、按种类查询与种类有效性检查。
- `scripts/farming/PlantingRules.cs`：播种与连续适季时间的只读规则模块；手动预算、年度整数日风险与真实 tick 结束预测共用精确适季边界，干田预留最少 1 秒供水。当前禁生季节拒绝播种，预计时间不足仅提示风险，手动与计划共用同一判断。
- `scripts/land/LandOccupancy.cs`：实例锚点、类别、登记时朝向、单格道路材质与基础格到实例映射的唯一拥有者。完整检查、登记及移除全部占地，返回稳定锚点顺序的不可变实例快照缓存，无空间变更时复用引用，成功建拆失效后发布新快照且旧快照不被修改；不持有金币、作物或加工进度。
- `scripts/land/BuildingFootprint.cs` 与 `BuildingSpaceSnapshot.cs`：固定形状偏移与工作中心的唯一定义，以及只读实例空间快照。农田/加工 3×3、道路 1×1；`BuildingOrientation.cs` 定义 q0～q3 世界朝向，空间快照只读提供且建成后不修改；土地、工人和地图共享几何，不新增外部任意形状或永久 ID。
- `scripts/land/PlacementRules.cs`：只读放置规则模块；统一验证完整陆地、建筑描述、世界朝向、格范围、占用与余额，为预检和实际执行返回稳定原因。道路与生产建筑互斥，`FarmGame.GetBuildingCostCents` 提供唯一类型费用，由经营入口重验后扣款；道路不创建生产状态；`RoadMaterial.cs` 定义五材质，合法性在现有土地规则集中判断并供经营、土地与表现共用，各入口保留原失败语义；批量道路检查与结果保留宽整数费用，整段失败资源零修改。
- `scripts/farming/FarmingSystem.cs`：每块农田所选作物、水分、播种、精确绝对成熟目标、到期句柄、本轮随机基础量与天气分段积分的唯一拥有者；实际开始生长时独立抽取6～9倍浮点倍率，每轮只抽一次，按权威天气实际变化及收获切段累计精确时间，正常与促熟共用产量和清轮前库存容量检查；统一季节检查，工人浇水与显式降雨共用供水操作，改种留水、收获与拆除清水。本轮身份与播种许可版本分别维护；启停仅失效旧待执行播种，同轮浇水认领和已成功动作保持，真正重启、拆建、收获换轮及清理仍失效。播种启停支持计划休耕，已播种仍可供水和成熟；只读预测按实际步长及首次禁生边界给出本轮结束，不把清除预测为收获；重启同种也失效旧任务。禁生换季先按内部严格阈值促成临近成熟并走原收获入库流程，再清理其余待水或生长中的本轮作物与水分，保留农田和选种；`CropClearResult.cs` 只返回本次逐作物实际清理轮数，不持有第二份农田状态。修改换季边界时读取 `docs/architecture/farming/farming-system/implementation-season-maturity.md`；修改工作凭据或成功动作校验时读取 `docs/architecture/farming/farming-system/implementation-work-identity-proposal.md`。
- `scripts/cultivation/CultivationPlanBook.cs`、`CultivationPlanRequest.cs` 与 `CultivationPlanSnapshot.cs`：共享年度表、农田引用、逐条年度执行凭据、预备安排与日期事件的唯一拥有者。每表生命周期保留下一可用条编号，删除及保存空表不回退，快照向草稿提供 `NextEntryId`；原条编辑仍保留编号与凭据。精确年度时间环绕，四季条每年重复，空白休耕、不同种至少间隔一天；独立条目排程校验拒绝禁生季起点，适季开始但跨入禁生季仍返回风险，完整保存另检查名称与模式。编辑或重应用后仍保持每条每年一轮，错过不补种。两种表级模式封装切换与保留，空白和同种后续条保留当前轮且停止复种，每条独立播种；实际结束按当前日期重新定位，应用编辑保留当前轮，手动指令解除本田引用；整表删除移除全部该表引用且不回退表编号；不拥有生长状态、库存或工人任务。
- `scripts/processing/ProcessingSystem.cs`：加工场地匹配作物、批次锁定投入量与进度的唯一拥有者，每批最多领取9份并等量产出，少量批次仍使用完整周期；持有精确绝对完工目标与共享产出队列句柄，读取快照时计算剩余量，完工前检查成品容量。修改批次、领取或完工时读取 `docs/architecture/processing/processing-system/interface-processing-system.md`。
- `scripts/workers/WorkerScheduler.cs` 与 `WorkerSnapshot.cs`：工人位置、任务、按实例独占认领与稳定锚点轮转游标的唯一拥有者。三人每经营秒推进 3 小格，到农田定义的工作中心播种浇水，执行前重验版本凭据；按单tick或平静区间推进，最近事件距离、行程起点与累计移动公式封装在工人模块；先取得完整路线再认领任务，整数累计行程按同一路线采样，取消保留连续位置；移动记录保留原始工作凭据供重验。选择算法保持私有。
- `scripts/inventory/CommodityId.cs` 与 `Inventory.cs`：前者统一十四商品标识与合法性；后者唯一拥有两类公共库存、逐作物底线和冻结数量。总查询含冻结，可用量扣除一次卖单冻结，加工按上限原子领取可用原料中超过底线的数量；买入与自产共用存储，主动出售不受加工底线限制但不能用冻结量。
- `scripts/trading/TradingService.cs`、`TradeResult.cs` 与 `ProductSaleResult.cs`：完整即时与委托结算模块，唯一维护含费成本、限价冻结估算和固定预算最大数量；先检查数量、执行时报价、可用及本单冻结资源、保留线和容量，再一次提交；即时零费，委托按实际成交额收 1% 向上取分费用，结果分别给出货值与费用。单商品结果保留实际读取的可选单价，全部加工品出售由原结算过程给出七商品明细；宽整数预检，失败资源零修改，调用方不拼装扣款和入库。
- `scripts/logging/`：独立日志模块。`RuntimeLog` 组装会话与局，`GameLog` 维护局上下文及具名领域入口，`CommandObservation` 共用指令关联与一次终结；领域 Adapter 拥有事件描述、真实资源投影和订单等待去重/有界合并，`LogOutput` 仅处理通用身份、序号、过滤、串行分流及健康，转义及截断元数据合并由 formatter 维护、文件轮转交给 Serilog。交易、订单和行情观察已接入；`GameplayLog` 观察建拆/底线，`ProductionLog` 统一实际生产增量、资源边界和60秒目标窗口，完整推进结束才封窗、正常局结束补尾。公开手动绑定不冒充自动生产采集，缺口与夹具变更降低覆盖；业务继续拥有资金、库存、执行和判断。修改记录入口、生命周期、分流或字段时读取 `docs/architecture/logging/interface-logging.md` 与 `docs/project/runtime-log-schema-v1.md`；`CultivationLog`、`TimeLog`、`ScenarioLog` 分别观察真实耕作结果、已接受时间意图和流程生命周期，`DiagnosticCapture` 只维护显式事件/对象范围与时间/条数预算，生产/工人/规则/行情/订单/表现 Adapter 提供真实明细且 Release 前置关闭；`PerformanceLog` 累计真实批次，`FramePerformanceLog` 由 RuntimeLog 单独累计进程帧，保持平静推进。`SimulationLog` 与 `TimeLog` 的短生命周期观察覆盖真实推进与驱动异常，共用异常投影及本次传播关联，保留命令终结和原异常传播；修改异常记录或阶段时读取 `docs/architecture/logging/implementation-exception-observation.md`。
- `scripts/trading/TradeOrderBook.cs`、`TradeOrderRequest.cs`、`TradeOrderSnapshot.cs` 与 `TradeOrderEvaluation.cs`：订单模块唯一持有单据配置、创建顺序、原现金基准、生命周期和各单资源归属。组内全部/组间任一条件，季节仅读日历；含费冻结估算与预算最大数量委托交易模块；一次限价数量或固定预算买单冻结，卖单冻结数量，持续策略不冻结。一次目标差额建单锁定、持续动态算，行情更新后每单检查一次；同 ID 编辑重验、撤销释放，查询返回独立只读快照。实际判断分支提供稳定阻塞事实，日志不从中文原因逆推或补做判断；实际成交在冻结与状态更新后记录一次。任意一次或持续成交且仍有等待单时要求下一经营秒重检；依赖不变且全部等待时保留批量平静推进。
- `scripts/economy/Wallet.cs`：每局金币总余额与冻结金额的唯一拥有者，提供可用金额；建造与即时交易不能动用冻结，委托消费本单额度并释放差额，所有数值保持原整数容量。
- `scripts/characters/NpcCharacter.cs` 与 `scenes/npc_character.tscn`：可复用的清亮 v2 NPC 动画角色。加载20名角色的待机、走、跑、播种和浇水 SpriteFrames，保留64×64、脚根(32,60)、四向及逐帧时长；合成帧包含工具，单次作业不重复叠加特效。主地图只展示真实位置和成功结果，动画不执行经营任务。
- `scripts/market/CommodityCatalog.cs` 与 `CommodityDefinition.cs`：唯一维护十四商品名称和初价，稳定排列，使用库存模块的统一商品标识。
- `scripts/market/MarketQuotes.cs` 与 `MarketSnapshot.cs`：唯一持有正式报价、固定双周排期、事件与公告，封装供需、季节、成本、整数分限幅及节日改期；接收日历推进并返回独立只读快照，玩家交易量不影响报价。可选本局 `MarketLog` 在实际公告/报价提交处观察已锁定因素，不增加随机抽取或价格计算；构造历史只建立基线。`MarketPriceCurve.cs` 保留为历史独立曲线及测试，不再参与经营。
- `scripts/weather/WeatherSystem.cs`、`WeatherRules.cs` 与 `WeatherRegionSnapshot.cs`：天气 Module 唯一拥有加载时生成的10年循环预排与当前活动矩形，自然及开发／测试显式模式在局创建时确定；默认晴天、区域互斥且最多3片，整田九格按雨＞云＞晴判定。`WeatherRules` 统一解析验证具名 JSON 并返回不可变配置，正式资源只加载一次，由 `FarmGame` 在建局时选择并传入执行器，合法配置变体可同进程隔离运行，算法不写死权重；当前状态、事件距离与不可变快照供经营和表现共用，活动组合不变时复用快照引用，变化时发布新快照；内部按固定农田锚点缓存九格聚合结果，活动天气实际变化时失效，不持有设施或农田状态。修改预排、区域设置或循环接缝时读取 `docs/architecture/weather/interface-weather-system.md`。
- `scripts/time/GameCalendar.cs`、`GameDate.cs` 与 `GameTimeUnits.cs`：日历唯一维护累计 `uint32` 模拟秒与暂停，固定月、季及年度长度供作物、年度表与时间图共用；纯日期查询共用相同年月日与季节换算，为未来实际报价日生成不可变日期；比例模块统一生产和日历的整数比例及剩余秒数换算。`FarmGame` 持有日历，按单tick或平静区间推进并在事件边界完整结算。
- `scripts/time/SimulationDueIndex.cs`：可复用的经营到期索引，仅持有绝对秒与整数载荷；固定 32,768 秒桶、两级非空位图及可复用索引双链维护一年窗口内 FIFO，容量独立配置。首次读取到期项封闭本秒登记，业务成功后取消句柄摘除，异常保留当前及后项；取消与清空使旧句柄失效。修改窗口、封口或句柄契约时读取 `docs/architecture/time/simulation-due-index/interface-simulation-due-index.md`。
- `scripts/time/SimulationDriver.cs` 与 `SimulationRateSource.cs`：唯一拥有每局倍率与未完成tick进度，并提供最近请求的实际完整秒凭据；完整检查点在宿主回调前提交进度，异常后不重复消费旧小数，现实帧时间转为同一经营批量请求；暂停及导航等待不累计，改速保留进度；驱动在接纳帧时间与完整检查点后统一检查导航就绪，正常等待保留已完成秒与等待前小数且恢复不追赶，已释放局错误继续传播。提供玩家/流程来源通知。发布仅0.5/1/2，开发额外提供 1～16 的整数倍率；场景只组装唯一当前局驱动；可选时间观察在已接受倍率赋值后、原通知前记录，同值选择仍保留来源。
- `scripts/world/MapCoordinates.cs`：固定等距地图的格坐标与地图本地坐标换算入口，使用 `FarmGame.MapSize` 定义的同一地图范围；不读取节点或经营状态。
- `scripts/world/WorldMap.cs`：地图表现模块。按 8×8 基础格缓存本局草／土／水三元素材与五材质道路，水纹空间相位独立于三变体格序，按真实水 mask 裁剪并读取统一暂停和倍率；内部 `RoadAppearance` 共用四邻 mask 与稳定变体，正式及新建／拆除预览局部刷新受影响旧路和跨块邻格；每实例按真实快照选择干湿土、播种层、七作物三档或七加工建筑，土层固定低层，设施与工人按工作中心和脚根共同深度排序。资源映射、pivot、外观档及可见性留在模块内部，图片跨块不裁切；镜头移动不重建经营状态；以土地的稳定只读占地快照识别空间变更，占地不变时跳过道路与删除集合同步，显隐只在真实可见性变化时写节点，仍逐次刷新湿土、作物档位、加工与动效。候选与正式实例按同一 q0～q3 选择设施、农田底层与作物图；仅 q0 使用当前包已提供的局部动效，冲突与整实例外围选框独立覆盖，正式与候选复用外围几何且各自保留颜色线宽；输入转换和占地仍复用正式几何，不维护经营规则。
- `scripts/world/WeatherPresentation.cs`：读取当前真实天气区域，程序绘制可见区域的云影、斜雨线及短落地效果，暂停停止并按倍率控制密度；视觉随机独立，UI保持原色，不执行供水或产量结算。
- `scripts/world/FacilityMotion.cs` 与 `rooted_wind.gdshader`：世界内部局部动效，封装磨坊塔身/叶片/轮毂、制糖坊蒸汽、三作物逐株固定根风摆及真实产出短效果。地图统一输入可见性、暂停、倍率与成功结果；建筑局部分层不改变全场深度顺序，植株按根点与人物共同排序，不执行经营结算。
- `scripts/world/EnvironmentDecorations.cs`：世界内部纯环境状态与可见节点缓存，读取本局完整陆地过滤水域，固定独立种子生成27类自然散布与5类受控陈设，按稳定哈希随机选择 q0～q3 图，保留原落点与种类；按实际完整占地永久清除落点，预览和失败不清除、拆除不复生。原画布控制跨格可见性，根点参与共同深度排序，不拥有正式占地或资源。
- `scripts/world/PlacementPreviewGeometry.cs`：内部占地几何，正式选框与候选预览共用外围计算；读取同一占地偏移生成候选格与逐格阻塞，只查询地图范围与真实占用，不复制正式放置规则或保存经营状态。
- `scripts/world/WorkerPresentation.cs`：读取三名工人快照与真实成功结果，复用角色场景和地图共同深度排序；沿本经营秒的真实折线路线按公共倍率插值并调整动画，高倍率显示最新真实位置而不积压动作。最近经营秒去重并重验原作业凭据，改种、拆除或换季使旧动作失效；暂停对齐位置并保留动画帧，动画不推进经营。
- `scripts/world/CameraController.cs`：输入与镜头模块。唯一保存地图玩家倍率，窗口和全屏扩大时增加地图视野，保持农田与人物像素大小、镜头中心和玩家倍率。订阅尺寸变化并在退出时取消；实际可编辑文本焦点下屏蔽键盘平移，释放焦点后恢复；按钮和只读焦点保持原行为，鼠标释放先校正。公开输入启停会清理旧拖动，地图本地定位共用原镜头边界；缩放限制与输入换算保持内部，UI 倍率由 `UiScaling` 独立维护。区分左键点击与拖动，释放即结束拖动，界面收到释放时清除按下凭据；公开拖动及鼠标在窗口内状态供摆放隐藏预览，通过 `WorldMap` 限制镜头；只发布已接受的地图短按，由 Main 按当前模式唯一分派，镜头不再直接选格；真实地图移动供道路段工具使用，镜头位移不决定道路轨迹。
- `scripts/ui/Main.cs` 与 `scenes/main.tscn`：场景协调入口。ReadyTask 完整异步准备地图，准备期间显示提示并禁止经营和镜头输入；退树取消准备并释放局。道路更新期间保持窗口交互且不消费驱动时间，等待统一准备任务后恢复，保留玩家暂停与倍率。组装像素田园浮动布局：顶部日期、点击循环倍率与暂停、右上金币与工人、左侧真实经营近况、右侧设施详情、底部中央经营入口；近况与 DEBUG 开发入口由 `ActivityLayout` 纵向容器按真实高度重排，字号与道路提示增长不遮挡。倍率位于日期竖线右、暂停左，1×→2×→0.5×循环，高开发倍率点击回1×，均提交玩家意图且不解除暂停。唯一分派已接受短按的普通选格、生产建造或道路意图，WorldMap 只落实已决定的选框，不再依赖选择事件补偿；唯一持有摆放类型和候选锚点，所有类型逐次建造后保持摆放，打开大地图取消候选与未完成道路段；农田/加工在建前通过 R/按钮旋转，每座候选起始 q0、成功后下一座重置、失败保留，不提供建成后旋转；道路使用内部 `RoadSegmentGesture` 唯一维护工具模式、当前段及共同候选/提交准备，新建与拆路未锁定起点时均显示跟随鼠标的单格预览，拆路只标记实际道路并预览邻接断开，五材质新建与投影直线拆除均两次短按；道路右键／Esc 先清段再退出，取消按钮直接退出；每帧在镜头更新后定位，界面遮挡或拖动时隐藏预览，标准费用与可用资金反馈分开显示。分发窗口意图并通过唯一时间驱动推进经营，经营变化及检查点异常后的已完成秒统一刷新；详情可见性统一检查目录与年度表状态，保留原选择；未占用地块直接读取本局完整陆地判定，含水岸格与水面显示水域不可建造；组装读取工人快照的表现；只在地块变化时同步地图。
- `scripts/ui/development/DeveloperToolsWindow.cs`、`ScenarioCatalogStep.cs`、`ScenarioEditorStep.cs`、`ScenarioResultStep.cs` 与 `ScenarioConfigurationForm.cs`：C三步协调、换行分类按钮与双列目录、配置编辑和真实运行结果；视觉按已确认C原型逐项比对步骤选中态、卡片与间距。同一表单按描述生成字段与并排分组，窗口唯一维护选择与步骤，草稿及文件规则交给配置库。两种垃圾桶经确认后转交删除意图；脏草稿重选需确认放弃，取消保留选择和输入，失效文件清空选择并禁用运行；准备及运行中锁定编辑，独立局异步准备可取消；当前局借主驱动、独立局只推进数据，报告引用已保存原文件。修改窗口操作时读取[开发窗口接口](docs/architecture/ui/developer-tools-window/interface-developer-tools-window.md)。
- `scripts/ui/DraggableWindow.cs`、`BuildCatalogWindow.cs`、`CropSelectionWindow.cs`、`InventoryWindow.cs`、`MarketWindow.cs`、`FarmDetailsPanel.cs`、`ProcessorDetailsPanel.cs`、`RoadDetailsPanel.cs` 与 `UiElements.cs`：分别维护窗口拖动与可用区域、建造目录、固定的选种/库存/市场控件、三类详情及统一木框纸面主题。窗口避让顶部状态与底部入口，长内容可滚动；主题集中维护按钮、输入、勾选及列表各态字色；`UiElements` 共用道路材质中文名称，目录与详情不为文字查询依赖场景入口。目录包含农田、七加工、五材质道路和拆路工具，分类和名称搜索共用固定控件并统一查询费用；道路成功后保留工具与材质、下一段重新选起点，详情继续发出单格拆除意图。库存显示总量、冻结、可用量并保留底线草稿与焦点；设施详情从快照显示实际进度、作物基准产量、当前整田天气、报价及损失说明；加工详情读取当前锁定批量和等量产出，空闲为零；选种同样使用基准产量口径。窗口不持有经营状态。
- `scripts/ui/NpcPreview.cs` 与 `scenes/npc_preview.tscn`：独立角色预览，接收 WASD/方向键移动、Q/E 切换20位角色、数字1～5核验五种已接入动作，显示名称与跟随镜头；不接入主经营场景。
- `scripts/development/configuration/`：可编辑字段类型与中文元数据、反射描述、严格JSON校验、配置库及独立草稿。配置库唯一负责文件来源、修订、另存/覆盖、单配置删除及流程目录持久移除；按稳定文件身份封装显式重选，返回最新目录、草稿或错误，旧草稿保留原字节冲突保护；内置示例只读，项目内用户配置长期纳入Git。修改字段或保存删除约定时读取[配置编辑接口](docs/architecture/development/interface-scenario-configuration-editor.md)。
- `scripts/development/scenarios/`：严格持久配置、共用买入→加工→一次卖出流程和JSON报告。流程只使用真实经营命令与稳定检查点，不拥有生产或交易状态；独立局准备受控数据，现场明确证据不足。报告引用原文件及加载时SHA-256，不保存配置副本；首个业务命令前绑定实际报告目录RunId，独立局与主局共享日志会话但拥有不同局身份，真实终结与报告保存分别记录。
- `scripts/ui/UiScaling.cs`：统一 UI 倍率模块，唯一记录控件原始排版与整体/字体倍率。两个公开设置接口支持独立子树与父子倍率组合，重复设置不累计，动态控件继承；字体变化触发真实重排，自绘时间图共用字号换算。默认1080P且关闭画布拉伸，扩大窗口不改设定倍率；不维护地图或经营状态。
- `scripts/ui/MapNavigation.cs` 与内部绘图、画布实现：大小地图 Module 只持有本局浏览、缩放、手势与显示缓存，读取真实地形和稳定设施空间快照；底图与设施变更更新，视野框按实际完整视口单独刷新。左下小地图固定全图，大地图独立缩放及左右拖动；同局保留浏览状态，新局重置。主场景协调开关、焦点、确认框、建造取消及镜头输入隔离；开图终止后台待启动和已启动的 GUI 拖放，经营暂停状态保持。修改地图交互或缓存时读取 `docs/architecture/ui/map-navigation/interface-map-navigation.md`。
- `scripts/ui/UiIcons.cs`、`FacilityPreview.cs` 与内部 `FacilityThumbnail.cs`：线条图标、农田和选种沿用原型 SVG；七加工 q0 与五材质三格道路缩略复用正式 PNG，目录和详情共用取图，拆路增加工具角标；只读快照提供真实占地、锚点和道路材质。图标跟随整体倍率，字体倍率仅调整文字；素材来源见 `assets/ui/source_notes.md`，不替换世界地图与人物素材。
- `scripts/ui/TradeOrdersWindow.cs`：从市场打开的委托与策略窗口，编辑商品、两种买单预算、数量、现金保留及条件组；列表读取真实状态、冻结和最近成交费用，编辑保留原 ID，每秒刷新不重建草稿控件；窗口不计算费用或修改经营资源。
- `scripts/ui/CultivationWindow.cs`、`CultivationTimeline.cs` 与 `CultivationCropCard.cs`：共享年度表管理、四季时间图与作物拖动来源。鼠标居中抓取整条，落位跨季拆分及冬春环绕，任一片段操作原条；纸色方形时间带和作物条保留月份并显示周刻度，条内居中信息放不下时悬停显示整轮周期及起止日期。窗口唯一构造替换后的候选草稿，读取表的下一可用编号并仅在新增成功后推进草稿编号；拖放预检及实际落位复用独立条目排程检查，未命名也可编辑，完整保存仍要求名称。拒绝时原草稿与正式表保持并说明原因；农田勾选的普通、悬停、按下及焦点字色保持深色可读。选中已保存表可整表删除，成功清空编辑器；编辑草稿、整表删除与批量应用只提交经营意图，两种手动指令展示接管影响。窗口显示计划日期、农田引用与实际生产进度，动画不推进经营。
- `tests/unit/`：经营流程、土地占用、农田与加工状态边界、交易、作物定义、库存、钱包、市场、独立日历、地区天气配置及地图坐标的单元测试；生产容量回归通过真实 FarmGame 检查溢出后的同秒续推、完整检查点和产出不重复；天气配置变体按JSON字段编辑并覆盖排版差异，配置字段扩展与预排规模调整的维护要求见天气接口和排程专题；`tests/integration/`：镜头输入、地图选择与 NPC 动画预览的集成测试；`tests/e2e/`：主场景经营流程的端到端测试；`tests/performance/`：必跑的满地图 50 tick 负载测试（含角落实体推进检查）与本地有窗口 FPS 性能测试；涉及地图、渲染或 UI 的修改必跑，其他修改按需执行。图形测试要求平均至少 60 FPS、P95 帧间隔不超过 16.67 ms，并保存前后截图。根目录 `TestSuite` 汇总 headless 检查；导出程序启动是构建冒烟测试。
- `tools/Run-Tests.ps1` 与 `coverage.settings`：编译 Debug、导入 Godot 图片资源、运行必需的 headless 测试套件、生成 Cobertura 报告，由 `tools/Test-Coverage.ps1` 按文件与行号去重，动态检查业务脚本总体及每个一级模块行覆盖率不低于 80%；`tools/Test-CoverageGate.ps1` 验证门禁夹具；`-Performance` 追加图形性能测试和 JSON 报告。
- `tools/Invoke-WindowsDistribution.ps1`：CI 与发布共用的完整分发命令，真实启动并保存退出事实，先保全再移出 runtime/debug 日志、保留同版本 schema 与全部运行依赖，发布 ZIP 复验通过后才交付。`Test-RuntimePack.ps1` 读取实际 PCK 目录及导入目标，统一验证必需运行资源与文档/退役素材隔离；Windows 分发与 macOS CI/发布共用该检查，独立日志 schema 保留在包外。`Run-PerformanceTests.ps1` 与 `Save-PerformanceEvidence.ps1` 分别维护测量失败和实际文件清单；`Test-ValidationArtifacts.ps1` 以真实进程及文件夹具约束 Windows 构建、发布及本地性能证据。修改分发内容或失败证据路径时读取 `docs/project/validation-artifacts-proposal.md`。
- `tools/Run-LoggingPerformance.ps1`、`Test-LoggingLifecycle.ps1` 与 `Test-LoggingRelease.ps1`：日志专项1080P、1×/16×矩阵，独立进程退出及实际Release程序集前置门禁，门禁探针同样以 `net10.0` 为目标；独立控制台通过现有配置接口显式加载正式天气 JSON，记录配置来源与哈希。测试场景与探针只生成本地证据，不更改正式采集策略。执行及故障边界见 `docs/architecture/logging/implementation-validation.md`。
- `tools/Repair-RuleLinks.ps1` 与 `tools/Test-StaticChecks.ps1`：按 `.codex/rule-links.json` 修复及检查目录指令符号链接，并检查文档路径、内部链接和脚本命名空间。提交前另按 [EditorConfig 格式验收](docs/static-checks/editorconfig.md)运行完整 C# 格式检查。
- `global.json` 与 `FarmExchange.csproj`：前者唯一固定开发与双平台 CI 的 SDK 10.0.401，禁止前滚和预览版；后者将项目目标框架设为 `net10.0`。工作流按 `global.json` 选用 .NET 10 SDK，另保留 `8.0.x` 安装供覆盖率工具运行，版本维护与本地检查见 `docs/project/continuous-integration.md`。
- `.github/workflows/ci.yml`：独立测试 CI，保留静态检查、headless 测试及总体和逐模块 80% 覆盖率；不运行有窗口 FPS，不导出 Windows。与两平台构建在 dev/main 推送时并行运行，并保留手动入口。
- `.github/workflows/windows.yml`：独立 Windows Release 编译、资源导入、导出及启动验收，dev/main 推送和手动触发；共用分发命令保全日志并上传干净提交级构建与独立证据。
- `.github/workflows/macos.yml`：在 macOS runner 上编译、导出 Universal 2 ZIP、检查双架构程序集并启动应用，上传提交级构建产物。
- `.github/workflows/release.yml`：独立手动发布游戏版本；操作者输入版本号，要求触发时 main 同提交的测试、Windows 构建、macOS 构建三条 CI 全部成功，再复用两平台产物。精确检出发布提交的工具和 schema；Windows 下载后共用分发命令再次验收启动、保全日志并检查最终干净 ZIP，macOS 检查 Universal 2 ZIP 后保持原包字节；两平台准备成功后创建标签与单个 GitHub Release，上传两份版本 ZIP。操作与失败处理见 `docs/project/release.md`。
- `export_presets.cfg`：定义 Windows x86_64 与 macOS Universal 2 正式验收构建，以及保留开发节点脚本资源的本地 Windows Dev / macOS Dev 预设；开发包不进入 CI 或正式发布。
- `.codex/skills/farm-exchange-review-pr/SKILL.md`：提交PR后的可选择独立审查入口，先询问是否审查，明确同意后委派新的指定模型子Agent；绑定准确PR版本，检查规则落实、深模块设计、维护成本和验收证据，保持只读。
- `.github/ISSUE_TEMPLATE/`：六类中文议题正文模板的唯一维护位置，供 GitHub 网页与 `.codex/skills/farm-exchange-submit-issue/SKILL.md` 共用；skill 负责查重、按模板填写与提交核对。模板在人工合入默认分支后供网页使用，来源说明见 `docs/research/issue-template-sources.md`。


# 工作约定

1. 每次代码修改完成后，在同一任务中及时更新 `docs/` 对应的中文文档与本文件的系统结构；代码、规则、操作说明和验收步骤一致后，才视为修改完成。功能合并后按 `docs/AGENTS.md` 的持续整理约定检查并归档历史方案，现行规则和待办先保留在对应专题。
2. 实施前核对 `docs/project/roadmap.md` 中的已确认范围。具体数值和操作规则未确认前仅做不依赖它们的工作；需要变更已沟通的实施方案时，先与用户沟通。
3. 保持模块接口简洁；只为实际出现的需求建立模块与接缝，不为假想情况增加兜底或抽象层。
4. 使用项目指定的 Godot 引擎验证场景与脚本。引擎可执行文件位于上述目录。
5. GitHub issue 是所有修改的唯一入口：收到功能请求后，Agent 自动查找对应 issue；没有匹配项时自动创建写明范围和验收标准的 issue。创建或更新 issue 时使用 [farm-exchange-submit-issue](.codex/skills/farm-exchange-submit-issue/SKILL.md)，按任务类型选择模板，正文只记录工程内容。关联 issue 后才开始开发，并且只完成该 issue 记录的内容。用户已持续授权只读查看 GitHub issue 和 PR，Agent 直接查询，不再请求逐次许可；`.codex/rules/github-read.rules` 放行对应的 `gh` 只读命令。
6. 以本 issue 或事先划定的小模块为审查单元：先完成该单元全部代码和中文文档，再按 `.codex/agents/code-checker.toml` 新建只读 `code_checker` 子 Agent 集中审查；开发到一半不穿插审查。每轮初审、修复复查及验收修改后的复审均新开独立 Agent，不复用上一轮审查员；提供 issue、已完成单元、基准和完整变更文件，由新审查员独立读取。检查规范、可证实缺陷、回归风险、必要测试和已确认规则一致性，不评价玩法合理性。问题须附文件、行号和证据，主 Agent 修复并同步文档后再新开审查，直到没有待修问题。
7. 功能开发与验证均在 `dev` 分支进行。代码、文档和审查完成后，Agent 必须使用项目指定引擎依次完成编译、自动化场景测试、Windows Release 中间导出，并运行 `build/windows/FarmExchange.exe` 验证导出产物；发现失败则继续修复并重新验证，直至全部通过，形成“issue → 开发 → 文档 → 审查与修复复查 → 编译 → 测试 → 导出 → 运行导出程序”的闭环。本地验收直接使用当前 `dev` 工作区，Windows Release 统一覆盖导出到 `build/windows/FarmExchange.exe`；不为验收或导出失败回滚另建项目、源码树或发布包副本，回滚依据 Git 提交和 GitHub 已发布产物。测试报告、覆盖率、截图和必要测试工具工件仍保存到既有产物目录。
8. 闭环验证通过后，使用 `.codex/skills/farm-exchange-submit-pr/SKILL.md` 提交并推送 `dev`，创建或更新关联 issue 的 `dev` → `main` Pull Request，等待人工合并。Agent 不直接提交或推送 `main`，也不自行合并 Pull Request。 PR创建或更新后，使用 [farm-exchange-review-pr](.codex/skills/farm-exchange-review-pr/SKILL.md) 先询问是否进行提交后的独立审查，明确同意后才委派；未回复或拒绝时保留已交付PR状态，已明确要求审查同一PR时不重复询问。此项保留第6条的提交前审查流程。
9. 行覆盖率以 `scripts/` 的每个一级目录为模块分别验收：每个模块及业务脚本总体均须达到 80%。统一测试入口根据 Cobertura 报告自动按文件与行号去重，并检查总体及每个一级模块；验收记录自动输出的有效行、已覆盖行与百分比，缺失报告、无有效业务行或缺失模块均失败。
10. 同时最多 3 名开发子 Agent，分配前明确文件所有权并建立相互交流。新任务与该开发 Agent 的历史任务不相关时新建 Agent，避免污染上下文；同一任务或模块的修复、验收反馈可接续。审查 Agent 仍按第 6 条每轮新建。

# 目录规范加载

文档、游戏代码、测试、场景、素材、工具和 GitHub 文件各有目录 AGENTS.md，原文位于 `.claude/rules/*.md`，链接对应表见 `.codex/rule-loading.md`。从仓库根目录工作时，在修改目标目录前读取该目录的 AGENTS.md；链接失效时先按对应表修复。Codex 的 `.codex/rules/*.rules` 只用于命令审批。

# 版本发布

1. 正式版本只从已人工合并的 `main` 发布。版本号使用 `v主版本.次版本.修订版本`：不兼容既有存档或核心玩法规则时递增主版本；兼容地新增玩法、系统或内容时递增次版本；兼容地修复缺陷、优化性能或调整表现、文档和构建时递增修订版本。首个稳定公开版本为 `v1.0.0`，此前使用 `v0.次版本.修订版本`。
2. `build/windows/` 是可覆盖的 Windows x86_64 中间导出目录，不纳入 Git。正式发布包写入 `build/releases/v主版本.次版本.修订版本/`，文件名分别为 `FarmExchange-v主版本.次版本.修订版本-windows-x86_64.zip` 和 `FarmExchange-v主版本.次版本.修订版本-macos-universal.zip`。
3. 发布前必须在 `main` 对目标提交完成 Release 编译、自动化场景测试、导出程序启动验收；独立发布工作流要求同提交的测试、Windows 构建、macOS 构建三条 CI 全部成功，复用两平台产物；Windows 下载后再验收启动并打包，macOS 保留已在 CI 验收的原始 ZIP，无需本地上传。版本号由操作者指定，两平台准备成功后创建同版本 Git 标签与单个 GitHub Release，上传两份 ZIP。发布完成以标签、GitHub Release、两平台压缩包版本号一致为准。

# 文档索引

- 新增、迁移、替换素材或调整导入设置时读取 `docs/project/asset-organization.md`；当前完整清亮 v2 素材入口与使用约定见 `docs/research/bright-complete-v2.md`，v1 分析归入 `docs/archive/research/` 保留历史。素材规则经 `assets/AGENTS.md` 加载，原始包归档不代表已接入或获准公开分发。

- 修改 UI 视觉或布局时读取 `docs/project/ui-visual-prototype.md`：采用田园布局与像素木作主题，保留当前地图与人物素材；HTML 为设计示例，Godot 窗口接入真实经营状态。

- `docs/README.md`：玩法、架构、项目协作、调研与静态检查的中文导航。
- `docs/gameplay/`：玩家可观察的作物、加工、交易、土地和地图操作规则。
- `docs/architecture/`：按模块整理的具名接口与对应实现说明。
- `docs/project/roadmap.md`：当前已确认范围、待确认事项和后续阶段。
- `docs/archive/index.md`：历史材料统一入口，内部镜像 `docs/` 原分类；已完成方案与阶段验收不作为现行规则。
- `docs/project/build-and-validation.md`：Windows 与 macOS 构建、测试、导出与启动验收。
- `docs/project/contribution-workflow.md`：issue、dev、main 与人工合并流程。
- `docs/research/`：市场曲线与测试分类的调研依据。
- `docs/static-checks/`：EditorConfig 与 CI 的机械检查范围。
