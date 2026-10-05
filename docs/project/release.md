# 正式版本发布

正式版本只从已人工合并的 `main` 构建，采用 `v主版本.次版本.修订版本`。不兼容既有存档或核心玩法规则的变更递增主版本；兼容地新增玩法、系统或内容递增次版本；兼容地修复缺陷、优化性能或调整表现、文档和构建递增修订版本。首个稳定公开版本为 `v1.0.0`，此前使用 `v0.次版本.修订版本`。

Windows 正式包固定写入：

```text
build/releases/v主版本.次版本.修订版本/FarmExchange-v主版本.次版本.修订版本-windows-x86_64.zip
```

压缩包不纳入 Git，只上传到同版本 Git 标签对应的 GitHub Release。目标提交必须已在 `main` CI 完成全部自动化场景测试、Release 编译、Windows 导出与程序启动验收。独立发布工作流复用该次 CI 的完整产物，下载后再验收启动并打包，不重复编译，不要求本地构建或上传；标签、GitHub Release 与压缩包的版本号一致才算发布完成。

## 在 GitHub 发布

`.github/workflows/release.yml` 提供独立的「发布 Windows 版本」手动入口；版本号暂由操作者指定，不自动递增。首次使用前必须将该工作流通过 PR 人工合并到 `main`。

1. 人工合并待发布的 PR 到 `main`，等待该提交的 **CI** 成功。
2. 在仓库 **Actions → 发布 Windows 版本 → Run workflow** 选择 `main`。
3. 输入新版本号，例如 `v0.2.0`，点击运行。只接受 `v主版本.次版本.修订版本`，每段为非负整数，无多余前导零。
4. 工作流以触发时的 `main` 提交 SHA 为目标，定位 `ci.yml` 中该提交成功的运行，下载 `FarmExchange-windows-x86_64-<提交 SHA>`。
5. 检查 EXE 与 C# 程序集，运行下载的游戏进行 headless 启动验收；将整个游戏目录压缩到约定版本目录，ZIP 根目录直接包含 `FarmExchange.exe` 及配套文件。
6. 通过后创建指向同一提交的版本标签和 GitHub Release，上传 ZIP，自动生成更新说明。Actions 摘要给出源 CI、提交及 Release 地址。

发布开始后 `main` 的后续推进不会改变本次目标。工作流仅需要 GitHub 自动提供的 `GITHUB_TOKEN`，配置 `actions: read` 下载产物及 `contents: write` 创建标签与 Release，无需额外上传凭据。多个发布任务串行执行，不取消正在发布的任务。

## 失败后的处理

- **分支或版本号错误**：重新选择 `main`，填写符合规则的新版本号。
- **尚无同提交成功 CI**：等待 CI 成功；若失败，先修复 CI。不会使用其他提交的构建。
- **产物缺失或过期**：Windows CI 产物保留 14 天；在 `main` 为同提交重新运行 CI 后再发布。工作流不会自动改用其他版本或本地包。
- **下载或启动失败**：检查 Actions 错误信息；启动非零退出或超过 60 秒会阻止创建 Release。
- **版本已用**：已有同名标签时拒绝发布，改用新版本号。创建 Release 或上传中途失败时，先查看 GitHub 中的标签及 Release/草稿状态；工作流不会自动覆盖、删除或重试已有版本。

发布成功后，玩家从仓库 **Releases** 下载带版本号的 ZIP。CI 的提交级 Artifact 与 Release 附件是两个独立产物，CI Artifact 后续过期不影响已经上传的 Release 附件。

操作依据：[下载 Actions 产物](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/download-workflow-artifacts)、[GitHub CLI 创建 Release 与上传附件](https://cli.github.com/manual/gh_release_create)。

[macOS CI](macos-build.md) 生成的 Universal 2 ZIP 是提交级验收产物；当前未配置 Apple 公证，不纳入本节的正式发布包。
