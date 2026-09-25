---
paths:
  - "tools/**"
---

# 工具脚本规则

- PowerShell 脚本文件采用动词-名词的 PascalCase，例如 Run-Tests.ps1、Test-StaticChecks.ps1；变量表达用途，不复用常见系统变量名。
- 工具负责可重复的机械步骤，失败时给出具体路径和原因。实际验收命令及触发条件写入 docs/project/build-and-validation.md 或 docs/static-checks/ci.md。
