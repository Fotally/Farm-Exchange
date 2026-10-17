# GitHub 协作流程

## 仓库与分支

- GitHub 仓库：`Fotally/Farm-Exchange`。
- `main` 是保护分支，只接收从 `dev` 发起并由人工合并的 Pull Request。
- `dev` 是长期开发与集成分支；按下述默认流程可直接实施修改，选定多 issue 分组交付时则接收独立修复分支的 Pull Request。

## Issue 驱动流程

项目根目录 `AGENTS.md` 的工作约定是修改流程的唯一规则来源。未选择下述“多 issue 分组交付”时，按以下默认顺序进行：

1. 在 GitHub 中找到或创建一项 issue，写明本次修改的范围和验收标准。只读查看 issue/PR 已获用户持续授权；项目的 [GitHub 只读命令规则](../../.codex/rules/github-read.rules) 放行相应查询，无需逐次询问。
2. Agent 在 `dev` 分支只实施该 issue 记录的内容；范围变更先沟通并更新 issue。最多 3 名开发子 Agent 同时工作，先划分文件所有权并互通接口。新任务与原 Agent 的历史任务不相关时另建开发 Agent，同任务或模块的修复可接续，避免无关上下文污染。
3. 以 issue 或预先划定的小模块为单元，完成全部代码和中文文档后，按[只读审查角色](../../.codex/agents/code-checker.toml)新建独立子 Agent 集中审查。每轮初审、修复复查和验收改码后的复审都新开 Agent，不复用旧审查员，不在开发半途审查。提供 issue、已完成单元、基准和完整变更文件，让新审查员独立读取；检查规范、可证实缺陷、回归风险、必要测试和规则一致性，不评价玩法合理性。问题附文件、行号、触发条件和证据；修复并同步文档后再新开审查，直到没有待修问题。
4. 审查通过后，完成编译、自动化场景测试、行覆盖率检查、Windows Release 中间导出及导出程序启动验证。
5. 使用仓库的 `farm-exchange-submit-pr` skill，按 `.github/PULL_REQUEST_TEMPLATE.md` 整理说明，提交并推送 `dev`，创建或更新从 `dev` 到 `main` 的 Pull Request；在说明中关联对应 issue，按实际改动多选附有简短说明的变更类型，并列出验证结果。推送会触发 GitHub Actions，再次执行编译、测试与覆盖率检查。
6. PR创建或更新后，使用 [farm-exchange-review-pr](../../.codex/skills/farm-exchange-review-pr/SKILL.md) 先询问是否需要提交后的独立代码与设计审查；明确同意后才委派。未回复或拒绝时保持PR已交付状态，明确要求审查同一PR的指令不重复询问。此审查保留第3步既有的提交前流程，结束后仍由人工决定合并。

## 多 issue 分组交付

用户要求按选定 issue 分组开发并提交到 `dev` 时，使用仓库级 [farm-exchange-parallel-issue-delivery](../../.codex/skills/farm-exchange-parallel-issue-delivery/SKILL.md)，替代上述默认流程中的直接开发 `dev`、`dev → main` 提交路径及可选提交后审查入口。

按实际代码影响划分：相互影响的 issue 在同一修复分支串行开发；每个独立组绑定独立分支、worktree 和 Codex 会话，调用 [farm-exchange-engineering-lead](../../.codex/skills/farm-exchange-engineering-lead/SKILL.md) 的分支受派模式开发与验收。总控通过[仓库会话控制接口](codex-thread-control.md)创建和跟踪真实 thread，统一分配开发预算及验证资源；等待所有组交付并停止写入后汇总，问题交回原会话返修，再重新核对全部交付。接口不可连接时报告阻碍，不能擅自替换为子 Agent 或 CLI session。

受派工程统筹只在指定修复分支提交本组成果，公共说明、推送、PR 和最终验收由总控组织。各组 PR 统一为修复分支 → `dev`；完成适用验证、当前版本 CI 与最终独立审查后等待人工验收，取得对应版本及范围的合并授权才合并。

总控确认原会话停止写入后，负责处理与最新 `dev` 的合并前冲突，在修复分支完成文档、验证、全新审查及新范围人工确认再合并。已经合入 `dev` 才发现的集成回归，由总控在 `dev` 关联 issue 修复并完成审查与适用验收，回到开发闭环；不绕过 `main` 的发布门槛。

查证已合并到 `dev` 后，精确清理对应修复分支并关闭完整范围已交付的关联 issue；保护 `dev`、`main` 和默认分支，保留证据与未完成总入口。版本绑定、分组调整及清理条件以该 skill 为准。

## PR独立审查的调用

上节多 issue 分组交付已包含必经的最终独立审查，不再重复询问；其他 PR 使用以下可选入口。

可用 `$farm-exchange-review-pr` 在提交PR后询问是否审查，也可明确指定“独立审查PR链接／编号”直接表达同意。选择只针对该PR；修复后的复查每轮新建审查员，模型和思考强度默认值由技能维护。审查读取准确base/head提交及完整调用上下文，区分当前缺陷、非阻断建议与未独立运行的验收证据。审查结果本身不授权改代码、发布评论或合并；需要修复时继续按关联issue和已确认范围执行。

## Issue 正文与提交

六类正文模板统一维护在 [.github/ISSUE_TEMPLATE/](../../.github/ISSUE_TEMPLATE/)，包含缺陷、功能、重构、性能、调研设计与维护任务。人工从 GitHub 的 New issue 选择对应模板，直接填写正文。模板元数据提供中文名称、用途与默认标题前缀；填写提示为 HTML 注释，可在编辑器中查看，提交前删除无关章节与提示。

模板在 `dev` 开发，通过 `dev` → `main` PR 人工合并到默认分支后，GitHub 网页才会显示并由 Insights → 社区标准识别；社区标准提供检查与设置入口，模板内容保存在仓库文件中。格式与生效方式见 [GitHub 官方说明](https://docs.github.com/en/communities/using-templates-to-encourage-useful-issues-and-pull-requests/about-issue-and-pull-request-templates)。

Agent 创建或更新 issue 时使用 [farm-exchange-submit-issue](../../.codex/skills/farm-exchange-submit-issue/SKILL.md)。skill 按任务类型读取同一份仓库模板，负责查重、事实与规则核对、填写、正文提交和结果核对。读取 YAML 元数据选择标题前缀，填写其后的 Markdown；元数据、HTML 提示注释与空章节不进入正式正文，通过 UTF-8 文件和 `--body-file` 提交。

GitHub CLI 可用 `gh api` 读取远端模板。例如下面命令读取 `dev` 上的缺陷模板，查阅默认分支时去掉 `?ref=dev`：

```powershell
gh api -H "Accept: application/vnd.github.raw+json" 'repos/Fotally/Farm-Exchange/contents/.github/ISSUE_TEMPLATE/bug.md?ref=dev'
```

本地模板可直接读仓库文件，无需每次请求远端；CLI 读取依据[仓库内容 API](https://docs.github.com/en/rest/repos/contents#get-repository-content)。短任务只保留必要字段。研究与维护任务按实际交付工件验收，字段来源见[议题模板的开源参照](../research/issue-template-sources.md)；统一修改流程仍以根 `AGENTS.md` 为准。

## Main 分支保护

`main` 要求通过 Pull Request 合并，并禁止直接推送、强制推送和删除。测试检查名为 `测试与覆盖率`；测试、Windows 构建和 macOS 构建在 `dev`、`main` 推送时独立并行运行，并均保留手动入口。测试执行 headless 场景及总体、逐模块 80% 行覆盖率门禁；两平台各自导出与启动验收。分支保护如引用旧检查名，须同步调整。GitHub Actions 不执行图形 FPS；涉及地图、渲染或 UI 的修改仍必须本地运行至少 1080P 的 FPS 验收并保存前后截图，其他修改按需执行。工作流细节及本地复现命令见[构建与验收](build-and-validation.md)。

## 版本发布

版本发布只能在人工合并 `dev` → `main` 后从 `main` 发起。版本号判定、构建目录、正式压缩包命名和验收条件以项目根目录 `AGENTS.md` 的“版本发布”为唯一规则来源。操作者指定版本号并手动触发独立发布工作流，要求触发时 `main` 同提交的测试、Windows 构建、macOS 构建三条 CI 全部成功，再复用两平台构建产物；两平台都准备成功后，由工作流创建版本标签和单个 GitHub Release，同时上传 Windows x86_64 与 macOS Universal 2 两份 ZIP。标签、GitHub Release 与两平台压缩包的版本号一致才算发布完成，`build/` 内容不提交进 Git。详细操作及失败处理统一见[正式版本发布](release.md)。
