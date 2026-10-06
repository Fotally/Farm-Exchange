# CI 静态检查范围

静态检查在现有编译、场景测试和导出流程之前执行。当前检查由 tools/Test-StaticChecks.ps1 实现，检查规则链接在 Git 中的符号链接模式及工作区目标、文档内部 Markdown 路径、文档及素材路径命名、业务脚本的命名空间与路径；C# 命名空间检查同时接受 LF 和 CRLF 行尾。Godot 生成的 `*.import` 是导入元数据，不作为素材文件检查，也不纳入 Git。失败时报告具体文件，修正原文或链接后重新运行；符号链接修复见[对应表](../../.codex/rule-loading.md)。

业务脚本命名空间检查递归覆盖模块子目录。`scripts/development/` 及其流程子目录使用 `FarmExchange.Development`，模块内 `development/` 子目录沿用所属模块命名空间，与编译排除约定一致。

dotnet format whitespace FarmExchange.sln --verify-no-changes 根据 .editorconfig 验证可格式化的 C# 空白风格；具体配置见[EditorConfig](editorconfig.md)。业务规则正确性由 Godot 场景测试负责，FPS、覆盖率、导出和启动要求仍以[构建与验收](../project/build-and-validation.md)为准。PR 模板中的变更类型与验收文字须对应真实改动，由提 PR skill 收集并供人工审查；CI 不假装理解其语义。
