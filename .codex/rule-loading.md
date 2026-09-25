# 规则加载与链接对应表

规范原文位于 `.claude/rules/*.md`，Claude Code 使用原文开头的 `paths` 限定加载范围。Codex 按目录读取指向同一原文的 `AGENTS.md` 符号链接。Codex 的 `.codex/rules/*.rules` 用于命令审批，不承载这些 Markdown 规范；本仓库无需该目录。根 `CLAUDE.md` 只导入根 `AGENTS.md`，避免 Claude Code 重复加载目录规则。

实际映射以 [rule-links.json](rule-links.json) 为准：

| Claude Code 规则原文 | Codex 目录入口 |
| --- | --- |
| .claude/rules/docs.md | docs/AGENTS.md |
| .claude/rules/scripts.md | scripts/AGENTS.md |
| .claude/rules/tests.md | tests/AGENTS.md |
| .claude/rules/scenes.md | scenes/AGENTS.md |
| .claude/rules/assets.md | assets/AGENTS.md |
| .claude/rules/github.md | .github/AGENTS.md |
| .claude/rules/tools.md | tools/AGENTS.md |

Windows 克隆仓库时若 Git 的 core.symlinks=false，链接可能被检出为只包含目标相对路径的一行普通文本。进入仓库后先在 Windows 启用开发者模式或具备创建文件符号链接的权限，再运行：

```powershell
git config core.symlinks true
./tools/Repair-RuleLinks.ps1
./tools/Test-StaticChecks.ps1
```

修复脚本只按 JSON 映射重连预期链接；遇到其他已有文件会失败，不覆盖未知内容。CI 在检出后执行同一修复和检查。启动新 Codex 会话以加载目录 AGENTS.md；在 Claude Code 中使用 /context 检查项目规则。根目录启动 Codex 时，根 AGENTS.md 要求在修改目标目录前读取对应的目录 AGENTS.md。

官方机制：[Codex AGENTS.md](https://learn.chatgpt.com/docs/agent-configuration/agents-md)、[Codex 命令规则](https://learn.chatgpt.com/docs/agent-configuration/rules)、[Claude Code 规则与 Windows 链接](https://code.claude.com/docs/en/memory)。
