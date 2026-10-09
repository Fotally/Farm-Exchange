# CI 静态检查范围

规则正文引用由 `tools/Test-RuleReferences.ps1` 统一检查，并接入 `tools/Test-StaticChecks.ps1`：映射内原文与每个目录入口分别解析普通 Markdown 内部链接；显式“仓库根路径”代码引用按仓库根目录解析。错误包含入口、引用和解析后的目标；外网可用性与正文语义不在范围内。运行 `tools/Test-RuleReferenceChecks.ps1` 验证真实符号链接目标正确但正文仅在原文位置有效的负例，以及根路径引用、普通相对路径和缺失入口。

覆盖率由 `tools/Run-Tests.ps1` 在生成报告后调用统一门禁，检查总体及各脚本一级模块均达到 80%；归属、去重与负面夹具见[覆盖率门禁](coverage.md)。

静态检查在现有编译、场景测试和导出流程之前执行。当前检查由 tools/Test-StaticChecks.ps1 实现，检查规则链接在 Git 中的符号链接模式及工作区目标、文档内部 Markdown 路径、文档及素材路径命名、业务脚本的命名空间与路径；C# 命名空间检查同时接受 LF 和 CRLF 行尾。Godot 生成的 `*.import` 是导入元数据，不作为素材文件检查，也不纳入 Git。失败时报告具体文件，修正原文或链接后重新运行；符号链接修复见[对应表](../../.codex/rule-loading.md)。

业务脚本命名空间检查递归覆盖模块子目录。`scripts/development/` 及其流程子目录使用 `FarmExchange.Development`，模块内 `development/` 子目录沿用所属模块命名空间，与编译排除约定一致。

dotnet format whitespace FarmExchange.sln --verify-no-changes 根据 .editorconfig 验证可格式化的 C# 空白风格；具体配置见[EditorConfig](editorconfig.md)。业务规则正确性由 Godot 场景测试负责，FPS、覆盖率、导出和启动要求仍以[构建与验收](../project/build-and-validation.md)为准。PR 模板中的变更类型与验收文字须对应真实改动，由提 PR skill 收集并供人工审查；CI 不假装理解其语义。

多行字典初始化中的字段按格式器要求逐项换行。定位单文件格式错误时，运行 `dotnet format whitespace FarmExchange.sln --verify-no-changes --include scripts/logging/TradeOrderLog.cs`；修复可对同一文件去掉 `--verify-no-changes` 执行，再运行不带 `--include` 的完整检查。格式检查通过不替代编译和测试。若 CI 在静态检查阶段退出，随后报告覆盖率文件缺失，应先修复最早失败步骤，再完整重跑以生成报告。

CI 在格式检查前输出 `dotnet --version`。项目目标框架 `net8.0` 与实际执行格式器的 SDK 版本不同；`actions/setup-dotnet` 安装 `8.0.x` 也不等于固定使用该版本，没有 `global.json` 时还需核对 runner 已安装的 SDK。定位本地通过而 CI 失败的问题时，先在仓库目录运行 `dotnet --version` 和 `dotnet --list-sdks`，再用 CI 实际版本对同一提交执行完整格式检查。

#130 的 `Main.InitializeGame` 曾在 `#endif` 两侧连接 `else if`：SDK 8.0.425、9.0.304 检查通过，10.0.401 可复现 CI 的16条空白错误。修复使用显式语句块表达工厂与公共平台选择，保留原编译条件和分支语义；涉及条件编译的排版调整须同时检查本地与 CI 使用的 SDK，不通过放宽门禁解决差异。
