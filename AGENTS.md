# 项目背景

- 本项目是 Godot 放置挂机游戏。地图为 128×128 格，采用等距视角。
- 已确认的核心循环：玩家建造农田，1 名工人按 tick 自动播种和浇水；作物成熟后交给对应场地加工，加工品在商店出售，收入用于解锁更多土地。
- 现有六种作物为小麦、玉米、水稻、马铃薯、向日葵、甘蔗。农田独立选种；每种原料由对应场地免费加工，只有加工品能出售。各作物时长和价格倍率以 `docs/gameplay/production/crop-growth.md` 为准。每 10 tick 经过 1 天，面粉价格按有界市场曲线在 1.00～20.00 金币间变化，其余加工品按当日面粉价倍率定价。
- 开局地图中心额外赠送 3 块农田、2 处配套加工场地及对应的 5 块已解锁土地；农田以 50% 概率出现 3 块同种或 2+1 两种配置。初始金币为 50.00，另有 2 次免费选地机会。
- Godot 引擎目录：`E:\Godot\Godot_v4.7.2-stable_mono_win64`。
- 用户使用中文；项目文档、设计讨论与代码修改说明使用中文。

# 代码系统结构

- `scripts/gameplay/FarmGame.cs`：游戏状态模块。初始化中心随机农田与配套加工场地，持有固定 128×128 土地、每块农田所选作物、工人轮流工作、tick 与天数、分类库存、交易和购地规则；界面通过格子快照、作物定义与经营命令使用它。测试专用的满地图填充方法生成 16,384 个现有实体。
- `scripts/market/MarketPriceCurve.cs`：市场曲线模块。按市场种子与天数直接计算 1.00～20.00 金币之间的面粉价格，以多周期正弦和小权重平滑噪声形成走势。
- `scripts/world/WorldMap.cs`：地图表现模块。按 8×8 格缓存带顶点颜色的地图块网格，镜头移动只更新块可见性，经营变化后同步完整世界快照并重建外观变化的块；独立绘制选中框和地图边缘，换算与选择格坐标，限制镜头。不维护经营规则。
- `scripts/world/CameraController.cs`：输入与镜头模块。区分左键点击、左键拖动，左键释放时结束拖动，并在释放事件被界面拦截时逐帧校正状态；处理缩放和键盘移动，通过 `WorldMap` 的接口选择格子及限制镜头。
- `scripts/ui/Main.cs` 与 `scenes/main.tscn`：场景协调及界面。提供农田作物和加工场地选择，把按钮命令和一秒计时器交给 `FarmGame`，再刷新地图、六类库存和售价。
- `tests/unit/`：农田、加工、交易、市场及地图坐标的单元测试；`tests/integration/`：镜头输入与地图选择的集成测试；`tests/e2e/`：主场景经营流程的端到端测试；`tests/performance/`：必跑的满地图 50 tick 负载测试（含角落实体推进检查）与按需的有窗口 FPS 性能测试。图形测试要求平均至少 60 FPS、P95 帧间隔不超过 16.67 ms，并保存前后截图。根目录 `TestSuite` 汇总 headless 检查；导出程序启动是构建冒烟测试。
- `tools/Run-Tests.ps1` 与 `coverage.settings`：编译 Debug、运行必需的 headless 测试套件、生成 Cobertura 报告，并要求业务脚本行覆盖率不低于 80%；`-Performance` 追加图形性能测试和 JSON 报告。
- `tools/Repair-RuleLinks.ps1` 与 `tools/Test-StaticChecks.ps1`：按 `.codex/rule-links.json` 修复及检查目录指令符号链接，并检查文档路径、内部链接和脚本命名空间。
- `.github/workflows/ci.yml`：`dev` 推送时自动运行 headless 测试；手动触发可选图形 FPS 性能测试；`main` 推送时在测试通过后额外完成 Windows Release 导出，通过进程退出码验收导出程序启动并上传构建产物。
- `.github/workflows/macos.yml`：在 macOS runner 上编译、导出 Universal 2 ZIP、检查双架构程序集并启动应用，上传提交级构建产物。
- `export_presets.cfg`：定义 Windows x86_64 与 macOS Universal 2 验收构建。

# 工作约定

1. 每次代码修改完成后，在同一任务中及时更新 `docs/` 对应的中文文档与本文件的系统结构；代码、规则、操作说明和验收步骤一致后，才视为修改完成。
2. 实施前核对 `docs/project/roadmap.md` 中的已确认范围。具体数值和操作规则未确认前仅做不依赖它们的工作；需要变更已沟通的实施方案时，先与用户沟通。
3. 保持模块接口简洁；只为实际出现的需求建立模块与接缝，不为假想情况增加兜底或抽象层。
4. 使用项目指定的 Godot 引擎验证场景与脚本。引擎可执行文件位于上述目录。
5. GitHub issue 是所有修改的唯一入口：收到功能请求后，Agent 自动查找对应 issue；没有匹配项时自动创建写明范围和验收标准的 issue。关联 issue 后才开始开发，并且只完成该 issue 记录的内容。
6. 功能开发与验证均在 `dev` 分支进行。代码和文档完成后，Agent 必须使用项目指定引擎依次完成编译、自动化场景测试、Windows Release 中间导出，并运行 `build/windows/FarmExchange.exe` 验证导出产物；发现失败则继续修复并重新验证，直至全部通过，形成“issue → 开发 → 文档 → 编译 → 测试 → 导出 → 运行导出程序”的闭环。
7. 闭环验证通过后，使用 `.codex/skills/farm-exchange-submit-pr/SKILL.md` 提交并推送 `dev`，创建或更新关联 issue 的 `dev` → `main` Pull Request，等待人工合并。Agent 不直接提交或推送 `main`，也不自行合并 Pull Request。

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
