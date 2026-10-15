# EditorConfig 检查范围

根目录 .editorconfig 只声明可静态判断的文本和 C# 命名风格：UTF-8、缩进、文件末尾换行与可表达的 PascalCase、camelCase 规则。C# 文件采用四空格缩进；Markdown 表格与中文正文不设置固定行宽。

EditorConfig 是规则来源，检查由编辑器或 CI 中的 dotnet format 执行。命名规则的 IDE 严重级别不自动等同于编译失败；若将某项设为构建门槛，须同时配置并验证相应诊断。现有私有静态只读字段沿用 PascalCase，避免未经讨论的大范围成员改名。

提交前运行 `dotnet format whitespace FarmExchange.sln --verify-no-changes`，同时检查业务脚本与测试辅助代码的属性模式缩进、对象初始化换行等 C# 排版。`git diff --check` 只检查差异中的空白错误，不能替代格式器；定位及定向修复步骤见 [CI 静态检查](ci.md)。

“一个模块是否该有 C# interface 类型”“文档归属是否正确”“跨模块是否依赖了实现细节”需要审查实际调用关系，不交给格式规则判断。规则原文及目录加载方式见[链接对应表](../../.codex/rule-loading.md)。
