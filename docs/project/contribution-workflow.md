# GitHub 协作流程

## 仓库与分支

- GitHub 仓库：`Fotally/Farm-Exchange`。
- `main` 是保护分支，只接收从 `dev` 发起并由人工合并的 Pull Request。
- `dev` 是 Agent 实施修改、提交验证结果和发起 Pull Request 的开发分支。

## Issue 驱动流程

项目根目录 `AGENTS.md` 的工作约定是修改流程的唯一规则来源。每次修改按以下顺序进行：

1. 在 GitHub 中找到或创建一项 issue，写明本次修改的范围和验收标准。只读查看 issue/PR 已获用户持续授权；项目的 [GitHub 只读命令规则](../../.codex/rules/github-read.rules) 放行相应查询，无需逐次询问。
2. Agent 在 `dev` 分支只实施该 issue 记录的内容；范围变更先沟通并更新 issue。最多 3 名开发子 Agent 同时工作，先划分文件所有权并互通接口。新任务与原 Agent 的历史任务不相关时另建开发 Agent，同任务或模块的修复可接续，避免无关上下文污染。
3. 以 issue 或预先划定的小模块为单元，完成全部代码和中文文档后，按[只读审查角色](../../.codex/agents/code-checker.toml)新建独立子 Agent 集中审查。每轮初审、修复复查和验收改码后的复审都新开 Agent，不复用旧审查员，不在开发半途审查。提供 issue、已完成单元、基准和完整变更文件，让新审查员独立读取；检查规范、可证实缺陷、回归风险、必要测试和规则一致性，不评价玩法合理性。问题附文件、行号、触发条件和证据；修复并同步文档后再新开审查，直到没有待修问题。
4. 审查通过后，完成编译、自动化场景测试、行覆盖率检查、Windows Release 中间导出及导出程序启动验证。
5. 使用仓库的 `farm-exchange-submit-pr` skill，按 `.github/PULL_REQUEST_TEMPLATE.md` 整理说明，提交并推送 `dev`，创建或更新从 `dev` 到 `main` 的 Pull Request；在说明中关联对应 issue，按实际改动多选附有简短说明的变更类型，并列出验证结果。推送会触发 GitHub Actions，再次执行编译、测试与覆盖率检查。
6. Agent 停止在待合并状态，由人工审查和合并 Pull Request。

## Issue 正文与提交

创建或更新 issue 时使用 [farm-exchange-submit-issue](../../.codex/skills/farm-exchange-submit-issue/SKILL.md)。入口按缺陷、功能、重构、性能、调研设计与维护任务加载对应参考模板，并负责查重、正文提交和结果核对；正文直接描述工程问题、现状、改进范围与验收标准。

短任务只保留必要字段，模板中的提示和空章节不进入正式正文。研究与维护任务按实际交付工件验收，具体执行约定和开源参照由 skill 维护；统一修改流程仍以根 `AGENTS.md` 为准。

## Main 分支保护

`main` 要求通过 Pull Request 合并，并禁止直接推送、强制推送和删除。持续集成检查名为 `测试与导出`：`dev` 推送执行 headless 测试和 80% 业务脚本行覆盖率门槛；PR 人工合并进入 `main` 后再次测试，并在通过后导出、启动验证和上传 Windows x86_64 构建。地图绘制、镜头、实体负载或渲染相关修改需要图形 FPS 数据时，可手动触发工作流并勾选 `performance`，生成独立性能报告；普通推送不自动运行图形性能测试。工作流细节及本地复现命令见[构建与验收](build-and-validation.md)。

## 版本发布

版本发布只能在人工合并 `dev` → `main` 后从 `main` 发起。版本号判定、构建目录、正式压缩包命名和验收条件以项目根目录 `AGENTS.md` 的“版本发布”为唯一规则来源；实际构建命令见[构建与验收](build-and-validation.md)。发布者创建同版本 Git 标签和 GitHub Release，并上传唯一的 Windows x86_64 zip，不把 `build/` 内容提交进 Git。
