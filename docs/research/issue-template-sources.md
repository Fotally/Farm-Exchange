# 议题模板的开源参照与采用理由

查阅日期：2026-10-04。以下均为项目维护者发布的模板或指南。链接固定到查阅时的提交；Wiki 为当时页面。分类模板使用本项目重新编写的中文内容，仅采用字段组织思路。

| 原始资料 | 采用的做法 | 应用位置 |
| --- | --- | --- |
| [Godot 缺陷模板](https://github.com/godotengine/godot/blob/e7cfa294a0b81bed7986be04a848cc1832a3f083/.github/ISSUE_TEMPLATE/bug_report.yml) | 单独问题单独建单、检索已有问题、记录版本环境、预期与实际、复现步骤和最小复现工件 | 缺陷模板与建单查重 |
| [Godot 改进提案模板](https://github.com/godotengine/godot-proposals/blob/77947dca0c03fe6d79ea0a780a14182b0b34bce1/.github/ISSUE_TEMPLATE/feature_proposal.yml) | 分开解释问题/限制与提出的改进，让描述足以讨论和行动 | 功能、重构与调研设计模板 |
| [VS Code 缺陷模板](https://github.com/microsoft/vscode/blob/0817aaa824590067854c361e77eb973a08b3cf48/.github/ISSUE_TEMPLATE/bug_report.md)与[功能模板](https://github.com/microsoft/vscode/blob/0817aaa824590067854c361e77eb973a08b3cf48/.github/ISSUE_TEMPLATE/feature_request.md) | 查重，缺陷携带版本及复现，简单功能请求保持简短 | 共同正文约定、缺陷与功能模板 |
| [VS Code 性能问题指南](https://github.com/microsoft/vscode/wiki/Performance-Issues) | 区分具体慢路径，记录运行状态并提供性能剖析或时序证据 | 性能模板的负载、基线与报告字段 |
| [Rust 回归模板](https://github.com/rust-lang/rust/blob/1d9e68013afd163e05032b507400ff74d007ed72/.github/ISSUE_TEMPLATE/regression.md) | 区分预期与实际，记录正常和发生回归的版本，附相关错误证据 | 缺陷模板的回归定位 |
| [Rust 文档问题模板](https://github.com/rust-lang/rust/blob/1d9e68013afd163e05032b507400ff74d007ed72/.github/ISSUE_TEMPLATE/documentation.yaml) | 文档问题使用专门类型，位置与问题摘要是核心字段 | 维护模板的文档分支 |

## 项目适配

- 中文正文、范围与验收标准、只写工程内容，来自本项目协作约定；不是开源模板的原文。
- 重构的行为保持与状态归属、性能的前后对照及现行阈值、研究的决策边界，按 Farm Exchange 的实际任务补充。
- 开源项目要求的引擎升级验证、禁用扩展、另建提案仓库等只适用于其自身流程。本项目不照搬这些操作。
- 六类中文 Markdown 模板统一维护在 [GitHub 议题模板目录](../../.github/ISSUE_TEMPLATE/)，由网页建单与 [issue 提交 skill](../../.codex/skills/farm-exchange-submit-issue/SKILL.md)共用。模板保留各类证据与验收指导，填写提示使用 HTML 注释。
- `name`、`about` 与 `title` 元数据分别提供模板名称、选择器用途与默认标题前缀；使用方法见[协作流程](../project/contribution-workflow.md)。不配置标签、负责人或外部机器人，不使用 YAML Issue Forms。
- GitHub 模板格式与默认分支生效方式依据 [GitHub 官方说明](https://docs.github.com/en/communities/using-templates-to-encourage-useful-issues-and-pull-requests/configuring-issue-templates-for-your-repository)。上述开源参照仍用于字段组织，不改变本项目规则。
