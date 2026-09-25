# GitHub 协作流程

## 仓库与分支

- GitHub 仓库：`Fotally/Farm-Exchange`。
- `main` 是保护分支，只接收从 `dev` 发起并由人工合并的 Pull Request。
- `dev` 是 Agent 实施修改、提交验证结果和发起 Pull Request 的开发分支。

## Issue 驱动流程

项目根目录 `AGENTS.md` 的工作约定是修改流程的唯一规则来源。每次修改按以下顺序进行：

1. 在 GitHub 中找到或创建一项 issue，写明本次修改的范围和验收标准。
2. Agent 在 `dev` 分支只实施该 issue 记录的内容；范围需要变化时，先与用户沟通，并把确认后的范围更新到 issue。
3. 完成实现、中文文档同步、编译、自动化场景测试、行覆盖率检查、Windows Release 中间导出及导出程序启动验证后，将提交推送到远端 `dev`。推送会触发 GitHub Actions，再次执行编译、测试与覆盖率检查。
4. 使用仓库的 `farm-exchange-submit-pr` skill，按 `.github/PULL_REQUEST_TEMPLATE.md` 整理说明，提交并推送 `dev`，创建或更新从 `dev` 到 `main` 的 Pull Request；在说明中关联对应 issue，按实际改动多选附有简短说明的变更类型，并列出验证结果。
5. Agent 停止在待合并状态，由人工审查和合并 Pull Request。

## Main 分支保护

`main` 要求通过 Pull Request 合并，并禁止直接推送、强制推送和删除。持续集成检查名为 `测试与导出`：`dev` 推送执行 headless 测试和 80% 业务脚本行覆盖率门槛；PR 人工合并进入 `main` 后再次测试，并在通过后导出、启动验证和上传 Windows x86_64 构建。地图绘制、镜头、实体负载或渲染相关修改需要图形 FPS 数据时，可手动触发工作流并勾选 `performance`，生成独立性能报告；普通推送不自动运行图形性能测试。工作流细节及本地复现命令见[构建与验收](build-and-validation.md)。

## 版本发布

版本发布只能在人工合并 `dev` → `main` 后从 `main` 发起。版本号判定、构建目录、正式压缩包命名和验收条件以项目根目录 `AGENTS.md` 的“版本发布”为唯一规则来源；实际构建命令见[构建与验收](build-and-validation.md)。发布者创建同版本 Git 标签和 GitHub Release，并上传唯一的 Windows x86_64 zip，不把 `build/` 内容提交进 Git。
