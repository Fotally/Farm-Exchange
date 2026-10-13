# 正式版本发布

正式版本只从已人工合并的 `main` 构建，采用 `v主版本.次版本.修订版本`。不兼容既有存档或核心玩法规则的变更递增主版本；兼容地新增玩法、系统或内容递增次版本；兼容地修复缺陷、优化性能或调整表现、文档和构建递增修订版本。首个稳定公开版本为 `v1.0.0`，此前使用 `v0.次版本.修订版本`。

两平台正式包固定写入同一版本目录：

```text
build/releases/v主版本.次版本.修订版本/FarmExchange-v主版本.次版本.修订版本-windows-x86_64.zip
build/releases/v主版本.次版本.修订版本/FarmExchange-v主版本.次版本.修订版本-macos-universal.zip
```

压缩包不纳入 Git，只上传到同版本 Git 标签对应的 GitHub Release。目标提交必须已在 `main` CI 完成全部自动化场景测试、Release 编译、Windows 导出与程序启动验收。同一提交的 macOS CI 也必须成功完成 Universal 2 双架构程序集检查及应用启动验收。独立发布工作流精确检出目标 SHA 的分发命令与 schema，复用同 SHA 两平台产物：Windows 下载后经 `Invoke-WindowsDistribution.ps1` 再验收启动、保全日志并检查干净 ZIP；macOS 检查原始 ZIP 内的应用可执行文件和两套程序集后，直接复制为带版本号的文件，不解压重打包，保留原包字节及权限信息。不重复编译，不要求本地构建或上传；两份 ZIP 同时上传到单个 Release，标签、GitHub Release 与两平台压缩包的版本号一致才算发布完成。

## 在 GitHub 发布

`.github/workflows/release.yml` 提供独立的「发布游戏版本」手动入口；版本号暂由操作者指定，不自动递增。首次使用前必须将该工作流通过 PR 人工合并到 `main`。

1. 人工合并待发布的 PR 到 `main`，等待该提交的 **CI** 和 **macOS 构建** 都成功。
2. 在仓库 **Actions → 发布游戏版本 → Run workflow** 选择 `main`。
3. 输入新版本号，例如 `v0.2.0`，点击运行。只接受 `v主版本.次版本.修订版本`，每段为非负整数，无多余前导零。
4. 工作流以触发时的 `main` 提交 SHA 为目标，分别定位 `ci.yml` 与 `macos.yml` 中该提交成功的运行，下载 `FarmExchange-windows-x86_64-<提交 SHA>` 和 `FarmExchange-macos-universal-<提交 SHA>`。
5. 共享分发命令检查有效 EXE/PCK/程序集，复制同提交 schema 并真实 headless 启动，最多等待 60 秒；将实际 runtime/debug 会话保存到分发输入之外，逐文件校验成功后才从游戏目录移出。临时 ZIP 的必需文件、schema 字节及无会话数据全部检查通过后，才生成版本 ZIP；根目录直接包含 `FarmExchange.exe` 及其他配套文件，保留 `logs/schema-v1.md`。macOS 原始 `FarmExchange.zip` 检查通过后，复制成 `FarmExchange-<版本号>-macos-universal.zip`，其中保留原始 `.app`。
6. 两平台都准备成功后，创建指向同一提交的版本标签和单个 GitHub Release，同时上传 Windows 与 macOS ZIP，自动生成更新说明。Actions 摘要给出两平台源 CI、提交及 Release 地址。

发布开始后 `main` 的后续推进不会改变本次目标。工作流仅需要 GitHub 自动提供的 `GITHUB_TOKEN`，配置 `actions: read` 下载产物及 `contents: write` 创建标签与 Release，无需额外上传凭据。多个发布任务串行执行，不取消正在发布的任务。

Windows 再次启动成功或失败均单独上传 `windows-release-validation-<sha>-<run-id>-<attempt>`，保留 14 天；目录 `build/validation/windows-release-<run-id>-<attempt>/` 包含 stdout、stderr、`startup.json` 及实际存在的日志，不收入版本 ZIP。未执行验收时不上传旧证据，取消后不承诺继续上传。schema 与全部运行依赖保留，玩家解压首次启动正常产生自己的 runtime 会话；命令约束及本地用法见[Windows 构建与导出](windows-build.md)。

## 失败后的处理

- **分支或版本号错误**：重新选择 `main`，填写符合规则的新版本号。
- **任一平台尚无同提交成功 CI**：等待 Windows 与 macOS CI 都成功；若失败，先修复对应 CI。不会只发布一个平台，也不会使用其他提交的构建。
- **产物缺失或过期**：两平台 CI 产物均保留 14 天；在 `main` 为同提交重新运行对应 CI 后再发布。工作流不会自动改用其他版本或本地包。
- **下载、包检查或启动失败**：检查 Actions 错误信息与独立 Windows 验收 artifact；任一平台下载失败、Windows EXE/PCK/程序集缺失、schema 字节不符、最终 ZIP 含验收会话、macOS ZIP 无效或缺少应用可执行文件/任一架构程序集都停止发布。Windows 启动非零退出或超过 60 秒也会阻止创建 Release，超时先终止并等待进程退出。
- **Windows 日志保全或上传失败**：原启动错误继续保留；日志复制/校验失败保留未保存的源数据并停止打包，已有诊断保存或上传失败不能伪装成功。分发工具只处理受控 CI 构建，不清理玩家已有目录。
- **版本已用**：已有同名标签时拒绝发布，改用新版本号。创建 Release 或上传中途失败时，先查看 GitHub 中的标签及 Release/草稿状态；工作流不会自动覆盖、删除或重试已有版本。

发布成功后，玩家从仓库 **Releases** 按平台下载带版本号的 ZIP。CI 的提交级 Artifact 与 Release 附件是两个独立产物，CI Artifact 后续过期不影响已经上传的 Release 附件。

操作依据：[下载 Actions 产物](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/download-workflow-artifacts)、[GitHub CLI 创建 Release 与上传附件](https://cli.github.com/manual/gh_release_create)。

[macOS CI](macos-build.md) 生成的 Universal 2 ZIP 同时支持 Intel 与 Apple 芯片。当前保留内建临时签名、未配置 Apple 公证，发布工作流不改变该状态；下载后的 Gatekeeper 行为见 macOS 构建说明。
