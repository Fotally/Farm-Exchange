---
name: farm-exchange-submit-issue
description: "在 Farm Exchange 创建或更新 GitHub issue 时使用；按任务类型读取仓库的中文议题模板，查重并提交问题、现状、改进范围与验收标准。"
---

# 提交 Farm Exchange Issue

默认仓库为 `Fotally/Farm-Exchange`。修改流程与授权以根 `AGENTS.md` 和 [GitHub 协作流程](../../../docs/project/contribution-workflow.md)为准。本 skill 负责组织和提交 issue；是否继续开发由当前任务范围决定。

## 选择模板

正文模板统一维护在仓库 `.github/ISSUE_TEMPLATE/`，供 GitHub 网页与本 skill 共用。按主要交付目标选择一份，仅加载当前需要的模板。相关表现属于同一问题时合并；可以独立交付、验收的不同任务分别建单。

| 任务 | 使用条件 | 仓库模板 |
| --- | --- | --- |
| 缺陷 | 已有行为与规则不符，或出现回归 | [缺陷模板](../../../.github/ISSUE_TEMPLATE/bug.md) |
| 功能 | 增加玩家行为、内容或经营能力 | [功能模板](../../../.github/ISSUE_TEMPLATE/feature.md) |
| 重构 | 调整内部结构，保持业务行为 | [重构模板](../../../.github/ISSUE_TEMPLATE/refactor.md) |
| 性能 | 降低明确负载下的时间、内存或绘制成本 | [性能模板](../../../.github/ISSUE_TEMPLATE/performance.md) |
| 调研与设计 | 收集证据、比较方案或澄清尚未确认的规则 | [调研设计模板](../../../.github/ISSUE_TEMPLATE/research-design.md) |
| 维护 | 文档、测试、构建、CI、配置与协作工具 | [维护模板](../../../.github/ISSUE_TEMPLATE/maintenance.md) |

调整分类或模板字段时读取[开源参照与采用理由](../../../docs/research/issue-template-sources.md)。无需在每次建单时重新调研。

## 正文约定

- 面向不了解对话的维护者，用中文直接写问题、现状、要做的改进与可检验的验收标准。标题采用模板中的类型前缀，点明对象和具体变化。
- 模板是填写指导。短任务压缩为必要段落；删除无关章节、填写提示、空字段和占位符。验收项描述完成条件，记录任务时保持未勾选。
- 读取模板开头 YAML 元数据中的 `title` 作为默认标题前缀，结合模板注释选择适合的类型；提交正文只使用元数据之后的 Markdown，填写后去除 HTML 提示注释。模板名称、用途与其他元数据不写入正文。
- 正文只留工程信息，不记录“用户要求”“本次会话”“仅登记”“报告候选”等对话过程。日期和版本仅在定位回归、测量或规则生效时保留；有工程意义的设计文档与关联 issue 可以链接。
- 从实际代码、现行规则、日志或测试读取事实；缺失证据标明待核实，不把推测写成已证实缺陷，不把未运行的验证写成通过。
- 区分已确认范围与待确认项。规则未明确时采用调研设计模板，或在相关字段明确限制，避免建单时自行补玩法、数值和实施方案。
- 验收只列本任务需要的结果与验证入口；统一开发闭环引用项目规则，不在每个 issue 重复粘贴完整构建流程。调研和文档任务按其工件验收。
- 标签、负责人、里程碑仅使用已确认的配置；任务未指定时保持默认。

## 查找、提交与核对

1. 核对请求、目标仓库与已授权的操作。要求创建/提交/更新 issue，或项目规则要求为当前修改先建单时，直接按授权执行；明确只要草稿时仅输出草稿。缺失信息先从已有上下文与仓库查找，关键范围仍不清楚时询问并继续独立工作。
2. 新建前用核心业务词搜索开放及关闭 issue，再读取候选正文核对目标和交付范围。相同目标且仍开放时复用；只有用户要求更新或任务范围需要补齐时才编辑。已关闭项作为历史依据，确认是否已有成果，再判断是否需要为剩余问题或新回归另建单。
3. 按选定模板整理正文。更新已有 issue 时保留仍有效的要求、依赖和验收项；仅改指定部分，标题只在请求或最终范围需要时修改。模板更适合另一类型时按实际任务组织，不扩大交付范围。
4. 将完整正文写入操作系统临时目录的 UTF-8 文件，通过 `--body-file` 创建或更新，保留真实换行。依据当前任务决定提交哪些 issue，完成建单不附带启动开发、关闭 issue 或创建 PR。
5. 提交后重新读取，核对标题、正文、范围和 issue 地址。向用户返回编号与链接；缺少确认的重要信息如实说明。
6. GitHub 写入失败时停止继续写入并报告具体错误。若超时或连接中断使提交结果不明确，先只读查询是否已生成或更新；不盲目重试创建重复单据。

## GitHub CLI

在项目目录核对远端。以下为常用命令，尖括号替换为实际值；搜索词取具体业务名称，正文使用已生成的 UTF-8 文件。

```text
git remote get-url origin

gh issue list --repo Fotally/Farm-Exchange --state all --search "<核心业务词>" --limit 30 --json number,title,state,url
gh issue view <issue号> --repo Fotally/Farm-Exchange --json number,title,body,state,url

gh issue create --repo Fotally/Farm-Exchange --title "<中文标题>" --body-file "<正文文件>"
gh issue edit <issue号> --repo Fotally/Farm-Exchange --body-file "<正文文件>"
gh issue edit <issue号> --repo Fotally/Farm-Exchange --title "<中文标题>" --body-file "<正文文件>"
```

已有授权不会因模板选择而增加确认步骤；本 skill 不改变运行环境权限和 GitHub 鉴权。
