---
name: farm-exchange-submit-pr
description: 在 Farm Exchange 的 issue 范围内完成代码与文档验收后，自动提交并推送 dev，创建或更新关联 issue 的 dev → main Pull Request；不得合并。
---

# 提交 Farm Exchange Pull Request

适用于已经完成 issue 范围内实现、中文文档和本地验收的工作。项目流程以根 AGENTS.md 与 docs/project/contribution-workflow.md 为准；具体运行命令以 docs/project/build-and-validation.md 为准。

1. 确认当前分支为 dev、工作区改动只属于关联 issue、没有未解决的冲突；读取 issue 范围和当前差异。
2. 核对这次改动所需的编译、场景测试、覆盖率、Windows Release 导出、导出程序启动和适用的图形性能结果。缺少或失败的验收先完成并修复；不得把未运行项写成通过。
3. 依据 .github/PULL_REQUEST_TEMPLATE.md 整理 PR 正文：关联 issue、选择主要类型，写清问题、变化、接口与兼容性、文档与素材、实际验证结果及审查提示。正文用中文。
4. 按主要变更类型和系统名写明提交信息；只暂存本 issue 的文件。提交后推送 dev 到其远端。
5. 查找现有 dev → main PR：若已关联本 issue，则更新正文；若没有，则创建 PR。最终核对 PR 的 base、head、issue 链接和正文与提交一致。
6. 将 PR 地址和验证结果告知用户，停在人工合并前。

项目已授权上述闭环中的提交、推送 dev 和创建 PR。正常流程无需逐项再问用户；运行环境的权限或 GitHub 鉴权失败时报告具体阻碍，skill 不改变权限策略，也不直接提交、推送或合并 main。
