# 项目背景

- 本项目是 Godot 放置挂机游戏。地图为 384×384 个 64×32 基础格，采用等距视角；农田与加工场地各占 3×3（192×96），道路占一格。
- 已确认的核心循环：玩家建造农田，开局 3 名工人按经营秒移动到田、自动播种和浇水；作物成熟后交给对应场地加工，加工品在商店出售，收入用于建造更多建筑。
- 现有七种作物为小麦、玉米、水稻、马铃薯、向日葵、甘蔗、萝卜。农田独立选种，时长与收获量见 `docs/gameplay/production/crop-growth.md`；原料进入公共库存，可直接出售或免费加工。十四商品各有独立双周报价，供需、季节、事件与原料成本参与目标价格，节日只移动当期报价，消息在实际报价前一日公布，数值以 `docs/gameplay/trading/market-quotes.md` 为准。即时同价零费买卖完整结算，失败金币库存零修改；买入原料下次领取或随后新建场地即时领取，暂停可主动交易。每秒推进一次经营，7 游戏日＝360 秒。
- 开局地图中心赠送 3 座农田和 2 处配套加工场地；农田以 50% 概率出现 3 块同种或 2+1 两种配置，萝卜参与随机。初始金币为 50.00；生产设施每座 10.00 金币，道路每小格 1.00。任意小格可作锚点，点击任一子格操作整座；工人每秒走 3 小格、在田中心播种浇水，原标准田布局的两日预算保持。顶部显示季、年、月、日。#74 最终尺寸与配套已确认，#75 已完成独立复查和完整运行验收。
- Godot 引擎目录：`E:\Godot\Godot_v4.7.2-stable_mono_win64`。
- 用户使用中文；项目文档、设计讨论与代码修改说明使用中文。

# 代码系统结构

- `scripts/gameplay/FarmGame.cs`：经营协调模块。按原种子顺序初始化中心随机多格设施，协调 384×384 基础格的完整建造拆除；任一子格解析同一锚点实例，按稳定实例顺序推进生产、降雨与领取。封装年度表管理、批量应用及两种手动接管，成熟与换季促熟共用收获入库路径；工人动作后记录实际计划播种，换季清理后同步日期事件。换日推进报价，完整交易委托交易模块；提供执行重验、实际扣费及地块/实例/详情快照。满地图夹具生成 16,384 生产实例、占用 147,456 子格并检查状态一致。
- `scripts/farming/CropCatalog.cs`：七种作物定义的唯一入口，包含适宜季节，提供只读作物表、按种类查询与种类有效性检查。
- `scripts/farming/PlantingRules.cs`：播种季节与预计成熟的只读判断模块；按相邻适宜季节计算可用时间，干田预留最少 1 秒供水。当前禁生季节拒绝播种，预计时间不足仅提示风险，手动与计划共用同一判断。
- `scripts/land/LandOccupancy.cs`：实例锚点、类别与基础格到实例映射的唯一拥有者。完整检查、登记及移除全部占地，返回稳定锚点顺序的只读实例快照缓存；不持有金币、作物或加工进度。
- `scripts/land/BuildingFootprint.cs` 与 `BuildingSpaceSnapshot.cs`：固定形状偏移与工作中心的唯一定义，以及只读实例空间快照。农田/加工 3×3、道路 1×1；土地、工人和地图共享几何，不新增外部任意形状或永久 ID。
- `scripts/land/PlacementRules.cs`：只读放置规则模块；统一验证建筑描述、格范围、占用与余额，为预检和实际执行返回稳定原因。道路与生产建筑互斥，`FarmGame.GetBuildingCostCents` 提供唯一类型费用，由经营入口重验后扣款；道路不创建生产状态。
- `scripts/farming/FarmingSystem.cs`：每块农田所选作物、水分、播种与精确生长进度的唯一拥有者；统一季节检查，工人浇水与显式降雨共用供水操作，改种留水、收获与拆除清水。播种启停支持计划休耕，已播种仍可供水；重启同种也失效旧任务。禁生换季先按内部严格阈值促成临近成熟并走原收获入库流程，再清理其余待水或生长中的本轮作物与水分，保留农田和选种；修改该边界时读取 `docs/architecture/farming/farming-system/implementation-season-maturity.md`。
- `scripts/cultivation/CultivationPlanBook.cs`、`CultivationPlanRequest.cs` 与 `CultivationPlanSnapshot.cs`：共享年度表、农田引用、逐条年度执行凭据、预备安排与日期事件的唯一拥有者。精确年度时间环绕，四季条每年重复，空白休耕、不同种至少间隔一天；编辑或重应用后仍保持每条每年一轮，错过不补种。两种表级模式封装切换与保留，应用编辑保留当前轮，手动指令解除本田引用；不拥有生长状态、库存或工人任务。
- `scripts/processing/ProcessingSystem.cs`：加工场地匹配作物与批次进度的唯一拥有者，负责按旧顺序从公共库存领取匹配原料。
- `scripts/workers/WorkerScheduler.cs` 与 `WorkerSnapshot.cs`：工人位置、任务、按实例独占认领与稳定锚点轮转游标的唯一拥有者。三人每经营秒推进 3 小格，到农田定义的工作中心播种浇水，执行前重验版本凭据；外部只推进一秒或读快照，选择算法保持私有以便替换复杂调度。
- `scripts/inventory/CommodityId.cs` 与 `Inventory.cs`：前者统一十四商品标识与合法性；后者唯一拥有两类公共库存、逐作物底线和冻结数量。总查询含冻结，可用量扣除一次卖单冻结，加工仅从可用原料领取超过底线的一份；买入与自产共用存储，主动出售不受加工底线限制但不能用冻结量。
- `scripts/trading/TradingService.cs` 与 `TradeResult.cs`：完整即时与委托结算模块，先检查数量、执行时报价、可用及本单冻结资源、保留线和容量，再一次提交；即时零费，委托按实际成交额收 1% 向上取分费用，结果分别给出货值与费用。宽整数预检，失败资源零修改；调用方不拼装扣款和入库。
- `scripts/trading/TradeOrderBook.cs`、`TradeOrderRequest.cs` 与 `TradeOrderSnapshot.cs`：订单模块唯一持有单据配置、创建顺序、原现金基准、生命周期和各单资源归属。组内全部/组间任一条件，季节仅读日历；一次限价数量或固定预算买单冻结，卖单冻结数量，持续策略不冻结。一次目标差额建单锁定、持续动态算，行情更新后每单检查一次；同 ID 编辑重验、撤销释放，查询返回独立只读快照。
- `scripts/economy/Wallet.cs`：每局金币总余额与冻结金额的唯一拥有者，提供可用金额；建造与即时交易不能动用冻结，委托消费本单额度并释放差额，所有数值保持原整数容量。
- `scripts/characters/NpcCharacter.cs` 与 `scenes/npc_character.tscn`：可复用的 NPC 动画角色。读取 64×64、每方向 6 帧的角色图集；预览模式按外部方向移动，主地图通过 `ShowAt` 只展示经营快照给定的位置、朝向和暂停，不执行自主物理移动或决定经营任务。
- `scripts/market/CommodityCatalog.cs` 与 `CommodityDefinition.cs`：唯一维护十四商品名称和初价，稳定排列，使用库存模块的统一商品标识。
- `scripts/market/MarketQuotes.cs` 与 `MarketSnapshot.cs`：唯一持有正式报价、固定双周排期、事件与公告，封装供需、季节、成本、整数分限幅及节日改期；只接收日历推进并返回独立只读快照，玩家交易量不影响报价。`MarketPriceCurve.cs` 保留为历史独立曲线及测试，不再参与经营。
- `scripts/time/GameCalendar.cs`、`GameDate.cs` 与 `GameTimeUnits.cs`：日历唯一维护累计 `uint32` 模拟秒与暂停，纯日期查询共用相同年月日与季节换算，为未来实际报价日生成不可变日期；比例模块统一生产和日历的整数比例及剩余秒数换算。`FarmGame` 持有日历并按步进推进。
- `scripts/world/MapCoordinates.cs`：固定等距地图的格坐标与地图本地坐标换算入口，使用 `FarmGame.MapSize` 定义的同一地图范围；不读取节点或经营状态。
- `scripts/world/WorldMap.cs`：地图表现模块。每空间实例读取一次生产快照，填入完整占地；按 8×8 基础格缓存网格，一个实例只在工作中心画一次标记。镜头只更新块可见性，外观变化时重建对应块；按固定偏移绘制完整设施外围选框，输入转为本地基础格并限制镜头。不维护经营规则。
- `scripts/world/WorkerPresentation.cs`：读取工人编号、经营格位置、目标与活动快照，复用角色场景显示三人并按帧插值；位置换算复用地图接口，暂停保持画面，视觉帧率与动画不推进经营。
- `scripts/world/CameraController.cs`：输入与镜头模块。唯一保存玩家倍率，内部抵消视口拉伸；窗口和全屏扩大时增加地图视野，保持农田与人物像素大小、镜头中心和玩家倍率。订阅尺寸变化并在退出时取消；缩放限制与输入换算保持内部，UI 沿用原适配。区分左键点击与拖动，释放即结束拖动，释放被界面拦截时逐帧校正；通过 `WorldMap` 选择格子及限制镜头。
- `scripts/ui/Main.cs` 与 `scenes/main.tscn`：场景协调入口。持有摆放和所选格，分发窗口意图及计时器命令，显示真实工人数、季、年、月、日与暂停按钮，经营变化后统一刷新，并组装读取工人快照的表现；只在地块变化时同步地图。
- `scripts/ui/DraggableWindow.cs`、`BuildCatalogWindow.cs`、`CropSelectionWindow.cs`、`InventoryWindow.cs`、`MarketWindow.cs`、`FarmDetailsPanel.cs`、`ProcessorDetailsPanel.cs`、`RoadDetailsPanel.cs` 与 `UiElements.cs`：分别维护窗口拖动与范围、建造目录、固定的选种/库存/市场控件、三类详情及共用视觉元素。目录包含道路卡片并统一查询费用；道路连续铺设到 Esc/取消为止，详情仅发出拆除意图。库存窗口在刷新时保留草稿与焦点；加工详情实时区分领取等待原因。窗口不持有经营状态。
- `scripts/ui/NpcPreview.cs` 与 `scenes/npc_preview.tscn`：独立角色预览，接收 WASD/方向键移动、Q/E 切换 20 位角色并显示名称与跟随镜头；不接入主经营场景。
- `scripts/ui/TradeOrdersWindow.cs`：从市场打开的委托与策略窗口，编辑商品、两种买单预算、数量、现金保留及条件组；列表读取真实状态、冻结和最近成交费用，编辑保留原 ID，每秒刷新不重建草稿控件；窗口不计算费用或修改经营资源。
- `scripts/ui/CultivationWindow.cs`、`CultivationTimeline.cs` 与 `CultivationCropCard.cs`：共享年度表管理、四季时间图与作物拖动来源。整条预览、落位跨季拆分及冬春环绕，任一片段操作原条；编辑草稿与批量应用只提交经营意图，两种手动指令展示接管影响。窗口显示计划日期、农田引用与实际生产进度，动画不推进经营。
- `tests/unit/`：经营流程、土地占用、农田与加工状态边界、交易、作物定义、库存、钱包、市场、独立日历及地图坐标的单元测试；`tests/integration/`：镜头输入、地图选择与 NPC 动画预览的集成测试；`tests/e2e/`：主场景经营流程的端到端测试；`tests/performance/`：必跑的满地图 50 tick 负载测试（含角落实体推进检查）与按需的有窗口 FPS 性能测试。图形测试要求平均至少 60 FPS、P95 帧间隔不超过 16.67 ms，并保存前后截图。根目录 `TestSuite` 汇总 headless 检查；导出程序启动是构建冒烟测试。
- `tools/Run-Tests.ps1` 与 `coverage.settings`：编译 Debug、导入 Godot 图片资源、运行必需的 headless 测试套件、生成 Cobertura 报告，并自动检查业务脚本总体行覆盖率不低于 80%；`-Performance` 追加图形性能测试和 JSON 报告。
- `tools/Repair-RuleLinks.ps1` 与 `tools/Test-StaticChecks.ps1`：按 `.codex/rule-links.json` 修复及检查目录指令符号链接，并检查文档路径、内部链接和脚本命名空间。
- `.github/workflows/ci.yml`：`dev` 推送时自动运行 headless 测试；手动触发可选图形 FPS 性能测试；`main` 推送时在测试通过后额外完成 Windows Release 导出，通过进程退出码验收导出程序启动并上传构建产物。
- `.github/workflows/macos.yml`：在 macOS runner 上编译、导出 Universal 2 ZIP、检查双架构程序集并启动应用，上传提交级构建产物。
- `export_presets.cfg`：定义 Windows x86_64 与 macOS Universal 2 验收构建。

# 工作约定

1. 每次代码修改完成后，在同一任务中及时更新 `docs/` 对应的中文文档与本文件的系统结构；代码、规则、操作说明和验收步骤一致后，才视为修改完成。
2. 实施前核对 `docs/project/roadmap.md` 中的已确认范围。具体数值和操作规则未确认前仅做不依赖它们的工作；需要变更已沟通的实施方案时，先与用户沟通。
3. 保持模块接口简洁；只为实际出现的需求建立模块与接缝，不为假想情况增加兜底或抽象层。
4. 使用项目指定的 Godot 引擎验证场景与脚本。引擎可执行文件位于上述目录。
5. GitHub issue 是所有修改的唯一入口：收到功能请求后，Agent 自动查找对应 issue；没有匹配项时自动创建写明范围和验收标准的 issue。关联 issue 后才开始开发，并且只完成该 issue 记录的内容。用户已持续授权只读查看 GitHub issue 和 PR，Agent 直接查询，不再请求逐次许可；`.codex/rules/github-read.rules` 放行对应的 `gh` 只读命令。
6. 以本 issue 或事先划定的小模块为审查单元：先完成该单元全部代码和中文文档，再按 `.codex/agents/code-checker.toml` 新建只读 `code_checker` 子 Agent 集中审查；开发到一半不穿插审查。每轮初审、修复复查及验收修改后的复审均新开独立 Agent，不复用上一轮审查员；提供 issue、已完成单元、基准和完整变更文件，由新审查员独立读取。检查规范、可证实缺陷、回归风险、必要测试和已确认规则一致性，不评价玩法合理性。问题须附文件、行号和证据，主 Agent 修复并同步文档后再新开审查，直到没有待修问题。
7. 功能开发与验证均在 `dev` 分支进行。代码、文档和审查完成后，Agent 必须使用项目指定引擎依次完成编译、自动化场景测试、Windows Release 中间导出，并运行 `build/windows/FarmExchange.exe` 验证导出产物；发现失败则继续修复并重新验证，直至全部通过，形成“issue → 开发 → 文档 → 审查与修复复查 → 编译 → 测试 → 导出 → 运行导出程序”的闭环。
8. 闭环验证通过后，使用 `.codex/skills/farm-exchange-submit-pr/SKILL.md` 提交并推送 `dev`，创建或更新关联 issue 的 `dev` → `main` Pull Request，等待人工合并。Agent 不直接提交或推送 `main`，也不自行合并 Pull Request。
9. 行覆盖率以 `scripts/` 的每个一级目录为模块分别验收：每个模块及业务脚本总体均须达到 80%。根据 Cobertura 报告逐模块核对并记录结果；现有测试脚本仅自动检查总体，模块验收需另行核对。
10. 同时最多 3 名开发子 Agent，分配前明确文件所有权并建立相互交流。新任务与该开发 Agent 的历史任务不相关时新建 Agent，避免污染上下文；同一任务或模块的修复、验收反馈可接续。审查 Agent 仍按第 6 条每轮新建。

# 目录规范加载

文档、游戏代码、测试、场景、素材、工具和 GitHub 文件各有目录 AGENTS.md，原文位于 `.claude/rules/*.md`，链接对应表见 `.codex/rule-loading.md`。从仓库根目录工作时，在修改目标目录前读取该目录的 AGENTS.md；链接失效时先按对应表修复。Codex 的 `.codex/rules/*.rules` 只用于命令审批。

# 版本发布

1. 正式版本只从已人工合并的 `main` 发布。版本号使用 `v主版本.次版本.修订版本`：不兼容既有存档或核心玩法规则时递增主版本；兼容地新增玩法、系统或内容时递增次版本；兼容地修复缺陷、优化性能或调整表现、文档和构建时递增修订版本。首个稳定公开版本为 `v1.0.0`，此前使用 `v0.次版本.修订版本`。
2. `build/windows/` 是可覆盖的 Windows x86_64 中间导出目录，不纳入 Git。正式发布包写入 `build/releases/v主版本.次版本.修订版本/FarmExchange-v主版本.次版本.修订版本-windows-x86_64.zip`。
3. 发布前必须在 `main` 对目标提交完成 Release 编译、自动化场景测试、导出程序启动验收；通过后创建同版本 Git 标签与 GitHub Release，并只上传对应的 Windows zip。发布完成以标签、GitHub Release、压缩包版本号三者一致为准。

# 文档索引

- `docs/README.md`：玩法、架构、项目协作、调研与静态检查的中文导航。
- `docs/gameplay/`：玩家可观察的作物、加工、交易、土地和地图操作规则。
- `docs/architecture/`：按模块整理的具名接口与对应实现说明。
- `docs/project/roadmap.md`：已确认背景、待确认事项和交付阶段。
- `docs/project/build-and-validation.md`：Windows 与 macOS 构建、测试、导出与启动验收。
- `docs/project/contribution-workflow.md`：issue、dev、main 与人工合并流程。
- `docs/research/`：市场曲线与测试分类的调研依据。
- `docs/static-checks/`：EditorConfig 与 CI 的机械检查范围。
