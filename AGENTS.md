# 项目背景

- 本项目是 Godot 放置挂机游戏。地图为 128×128 格，采用等距视角。
- 已确认的核心循环：玩家建造农田，1 名工人按 tick 自动播种和浇水；作物成熟后交给对应场地加工，加工品在商店出售，收入用于建造更多建筑。
- 现有七种作物为小麦、玉米、水稻、马铃薯、向日葵、甘蔗、萝卜。农田独立选种；每种原料可按品种出售公共库存，或由对应场地免费加工，加工品可全部出售。各作物时长、收获量和价格倍率以 `docs/gameplay/production/crop-growth.md` 为准；原料当前按对应加工品当日售价的 50% 计价，比例可逐种调整。每秒推进一次经营，7 游戏日＝360 秒；面粉价格按有界市场曲线在 1.00～20.00 金币间逐日变化，其余加工品按当日面粉价倍率定价。
- 开局地图中心赠送 3 块农田和 2 处配套加工场地；农田以 50% 概率出现 3 块同种或 2+1 两种配置，萝卜参与随机。初始金币为 50.00；空地可直接建造，每座新建筑暂收 10.00 金币。顶部显示季、年、月、日；独立商品行情仍待 issue #25。
- Godot 引擎目录：`E:\Godot\Godot_v4.7.2-stable_mono_win64`。
- 用户使用中文；项目文档、设计讨论与代码修改说明使用中文。

# 代码系统结构

- `scripts/gameplay/FarmGame.cs`：经营协调模块。按原种子顺序初始化中心随机建筑，协调固定 128×128 格的建造、拆除、每秒显式降雨与生产相位、日历、播种季节检查、逐日售价和交易；换季时在成熟结算之后请求农田清理禁生作物。提供只读放置预检、执行时重验与实际扣费结果、地块及两类详情语义快照，旧建造命令转发新路径。测试专用夹具生成 16,384 个现有实体，并检查占用与生产状态一致。
- `scripts/farming/CropCatalog.cs`：七种作物定义的唯一入口，包含适宜季节，提供只读作物表、按种类查询与种类有效性检查。
- `scripts/farming/PlantingRules.cs`：播种季节与预计成熟的只读判断模块；按相邻适宜季节计算可用时间，干田预留最少 1 秒供水，统一返回不适季或时间不足原因。
- `scripts/land/LandOccupancy.cs`：固定地图每格主要占用类别的唯一拥有者；不持有作物或加工进度。
- `scripts/land/PlacementRules.cs`：只读放置规则模块；统一验证建筑描述、格范围、占用与余额，为预检和实际执行返回稳定原因。现有合法占用只有农田和加工场地。
- `scripts/farming/FarmingSystem.cs`：每块农田所选作物、水分、播种与生长进度的唯一拥有者；播种前调用统一季节检查，工人浇水和显式降雨共用供水操作，改种留水、收获与拆除清水；进入禁生季节时完整清理待水或生长中的本轮作物与水分，保留农田和选种。
- `scripts/processing/ProcessingSystem.cs`：加工场地匹配作物与批次进度的唯一拥有者，负责按旧顺序从公共库存领取匹配原料。
- `scripts/workers/WorkerScheduler.cs`：单工人旧轮转游标的唯一拥有者，传递当前日历快照，每 tick 至多执行一次符合播种或浇水规则的农田工作；尚无经营移动时间。
- `scripts/inventory/Inventory.cs`：每局原料、加工品分类库存与逐作物原料保留底线的唯一拥有者，统一查询领取可用性并完整扣除超过底线的一份原料；手动出售仍可清空公共库存。
- `scripts/economy/Wallet.cs`：每局金币余额唯一拥有者，验证初始余额、扣款及入账的数值范围。
- `scripts/characters/NpcCharacter.cs` 与 `scenes/npc_character.tscn`：可复用的 NPC 动画角色。读取 64×64、每方向 6 帧的角色图集；按外部给定方向移动、切换朝向并在停步时保留首帧，不决定经营任务。
- `scripts/market/MarketPriceCurve.cs`：市场曲线模块。按市场种子与天数直接计算 1.00～20.00 金币之间的面粉价格，以多周期正弦和小权重平滑噪声形成走势。
- `scripts/time/GameCalendar.cs` 与 `GameTimeUnits.cs`：前者唯一维护累计 `uint32` 模拟秒与暂停，按 7 日＝360 秒换算年月日与季节；后者统一生产和日历的整数比例及剩余秒数换算。`FarmGame` 持有日历并按步进推进。
- `scripts/world/MapCoordinates.cs`：固定等距地图的格坐标与地图本地坐标换算入口，使用 `FarmGame.MapSize` 定义的同一地图范围；不读取节点或经营状态。
- `scripts/world/WorldMap.cs`：地图表现模块。按 8×8 格缓存带顶点颜色的地图块网格，镜头移动只更新块可见性，经营变化后同步完整世界快照并重建外观变化的块；独立绘制选中框和地图边缘，将屏幕输入转为地图本地格坐标，并提供有效格的全局中心与镜头限制。不维护经营规则。
- `scripts/world/CameraController.cs`：输入与镜头模块。区分左键点击、左键拖动，左键释放时结束拖动，并在释放事件被界面拦截时逐帧校正状态；处理缩放和键盘移动，通过 `WorldMap` 的接口选择格子及限制镜头。
- `scripts/ui/Main.cs` 与 `scenes/main.tscn`：场景协调入口。持有摆放和所选格，分发窗口意图及计时器命令，显示季、年、月、日与暂停按钮，经营变化后统一刷新；只在地块变化时同步地图。
- `scripts/ui/DraggableWindow.cs`、`BuildCatalogWindow.cs`、`CropSelectionWindow.cs`、`InventoryWindow.cs`、`MarketWindow.cs`、`FarmDetailsPanel.cs`、`ProcessorDetailsPanel.cs` 与 `UiElements.cs`：分别维护窗口拖动与范围、建造目录、固定的选种/库存/市场控件、两类详情展示及共用视觉元素。库存窗口逐作物编辑原料保留底线并在刷新时保留未提交输入及焦点；加工详情区分缺料、受底线限制、待领取和加工中。窗口发出玩家意图，不持有经营状态。
- `scripts/ui/NpcPreview.cs` 与 `scenes/npc_preview.tscn`：独立角色预览，接收 WASD/方向键移动、Q/E 切换 20 位角色并显示名称与跟随镜头；不接入主经营场景。
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
6. 以本 issue 或事先划定的小模块为审查单元：主 Agent 先完成该单元全部计划内代码和对应中文文档，再集中调用 `.codex/agents/code-checker.toml` 定义的只读 `code_checker` 子 Agent；开发到一半时不穿插审查。调用时提供 issue 号、已完成单元、改动基准和变更文件，审查该单元完整改动。审查代码规范、可证实的缺陷、回归风险、必要测试及与已确认规则的一致性；不评价业务功能或玩法设计是否合理。子 Agent 按文件和行号报告待修问题；主 Agent 修复并同步文档后再次调用其审查，直到没有待修问题。后续验收若引起代码改动，也须重新审查。
7. 功能开发与验证均在 `dev` 分支进行。代码、文档和审查完成后，Agent 必须使用项目指定引擎依次完成编译、自动化场景测试、Windows Release 中间导出，并运行 `build/windows/FarmExchange.exe` 验证导出产物；发现失败则继续修复并重新验证，直至全部通过，形成“issue → 开发 → 文档 → 审查与修复复查 → 编译 → 测试 → 导出 → 运行导出程序”的闭环。
8. 闭环验证通过后，使用 `.codex/skills/farm-exchange-submit-pr/SKILL.md` 提交并推送 `dev`，创建或更新关联 issue 的 `dev` → `main` Pull Request，等待人工合并。Agent 不直接提交或推送 `main`，也不自行合并 Pull Request。
9. 行覆盖率以 `scripts/` 的每个一级目录为模块分别验收：每个模块及业务脚本总体均须达到 80%。根据 Cobertura 报告逐模块核对并记录结果；现有测试脚本仅自动检查总体，模块验收需另行核对。

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
