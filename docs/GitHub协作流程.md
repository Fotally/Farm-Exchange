# GitHub 协作流程

## 仓库与分支

- GitHub 仓库：`Fotally/Farm-Exchange`。
- `main` 是保护分支，只接收从 `dev` 发起并由人工合并的 Pull Request。
- `dev` 是 Agent 实施修改、提交验证结果和发起 Pull Request 的开发分支。

## Issue 驱动流程

项目根目录 `AGENTS.md` 的工作约定是修改流程的唯一规则来源。每次修改按以下顺序进行：

1. 在 GitHub 中找到或创建一项 issue，写明本次修改的范围和验收标准。
2. Agent 在 `dev` 分支只实施该 issue 记录的内容；范围需要变化时，先与用户沟通，并把确认后的范围更新到 issue。
3. 完成实现、中文文档同步和适当验证后，将提交推送到远端 `dev`。
4. 创建从 `dev` 到 `main` 的 Pull Request，在说明中关联对应 issue，并列出验证结果。
5. Agent 停止在待合并状态，由人工审查和合并 Pull Request。

## Main 分支保护

`main` 要求通过 Pull Request 合并，并禁止直接推送、强制推送和删除。仓库暂未配置持续集成检查，因此当前不设置必需状态检查；添加稳定的持续集成任务后，应通过新的 issue 决定是否把对应检查设为合并条件。
