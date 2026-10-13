# Windows 分发内容和失败性能证据修复设计

关联 [#137](https://github.com/Fotally/Farm-Exchange/issues/137) 和 [#138](https://github.com/Fotally/Farm-Exchange/issues/138)。修复分支为 `fix/137-138-ci-artifacts`，基线为 `ed84e455d703e59cc0257b7295fe6c866e6987f6`。两项共同修改 Windows CI 的产物流程，安排在同一分支，仍按两个 issue 分别验收。2026-10-13 已实施下述脚本、两处工作流调用及自动契约；整合到 dev 后的正式引擎/Windows 导出闭环和远端 Actions 实际上传待验收。现行操作规则见 [持续集成](continuous-integration.md)、[Windows 构建](windows-build.md) 和 [正式发布](release.md)。

两个真实调用方复用一个完整 Windows 分发准备命令：检查构建、真实启动、保存验收日志、清理仅属于验收的 runtime/debug 数据、检查干净分发内容，并在发布时生成及验证 ZIP。性能流程另用明确测量步骤与失败后收集，把“性能是否通过”与“证据是否保存”分别记录。

用户确认修复分支先提 PR 到 `dev`，等待人工合并；CI 新增仅以 `dev` 为目标的 `pull_request` 入口，在合并前执行原生 Windows PowerShell 契约、headless 场景及覆盖率门禁。该入口复用原测试步骤，图形 FPS 仍仅手动选择，Windows Release 导出仍仅在 `main`，并发取消规则保持。触发规则同时由既有产物契约约束，避免修复 PR 在合并前缺少原生门禁。

## 问题与设计范围

最新基线的成功 Windows CI `38263728814`、artifact `11753872657` 实际包含 `logs/runtime/runtime-20261012.log`，其中有 runner 的 Release Runtime 会话开始与结束。随包 schema 与本地正文相同，直接字节差异来自 Windows CRLF 与 Linux LF，不能把它描述为 schema 内容错误。发布流程下载后再次启动再全目录压缩，第二次生成的日志也会被收入。

FPS 上传条件只有 `inputs.performance == true`，隐含 success()；测量先写 JSON 和结束截图再返回非零，上传因此跳过。源码同时只上传 JSON，遗漏现有前后截图。本轮核验未主动触发远端失败工作流，也未检查已发布版本 ZIP。

本方案保留玩家 exe 同级日志路径、同步 Runtime 采集、原性能阈值、手动性能开关、同 SHA 双平台来源和 macOS 原 ZIP 字节。游戏日志与 Main 无须修改，使 CI 分支和两项游戏修复的文件集合分离。

## Module 与 Seam

| Module | Interface 与拥有的知识 |
| --- | --- |
| Windows 分发准备脚本 | 对一个构建完成真实验收与干净分发准备；集中 exe/PCK/程序集/schema、日志归属和 ZIP 内容约定 |
| FPS 测量脚本 | 运行现有性能场景、清理本次固定输出、传播退出码和阈值失败 |
| 性能证据收集脚本 | 收集本次已产生的文件，输出有无及缺失清单，不判断性能通过 |
| ci.yml 与 release.yml | 选择提交、触发条件、步骤顺序、上传及正式发布；不分别重新实现 Windows 文件规则 |

Windows CI 和发布是两个实际调用方，共享命令的 Seam 有现实需求。性能测量和文件收集具有不同失败含义，保持各自完整职责，不合并成通用 artifact 框架。没有改变日志后端的 Adapter。

## Windows 分发准备 Interface

新增 `tools/Invoke-WindowsDistribution.ps1`，建议参数为 `-BuildDirectory`、`-SchemaPath`、`-EvidenceDirectory`，发布额外传入可选的 `-ArchivePath`。调用方只传同提交的构建、schema 来源和输出位置；普通调用返回可分发目录，带 ArchivePath 的调用返回验证成功的最终 ZIP。发生失败时抛出含阶段、路径和原进程退出结果的错误。

该命令包含以下完整约定，不要求调用方另行拼接复制、删日志与打包步骤：

1. 检查三个输入路径关系：EvidenceDirectory 和 ArchivePath 均不得位于 BuildDirectory 内；构建必须是明确传入的构建工件目录，不能根据安装路径寻找或清理玩家数据。证据目录采用本次运行独立位置，不覆盖上一轮会话文件。
2. 检查有效 `FarmExchange.exe`、`FarmExchange.pck` 和 `data_FarmExchange_windows_x86_64/FarmExchange.dll`，复制同提交 schema 到 `logs/schema-v1.md`，随后真实启动 `--headless --quit-after 5`。等待上限沿用发布现有的 60 秒，超时先终止并等待进程退出；正常与失败均记录退出事实。
3. 启动 stdout/stderr 直接保存到 EvidenceDirectory；在进程退出后的 finally 路径保全 `logs/runtime/`、`logs/debug/` 下现有验收数据。移出构建的仅为这两个运行目录，`logs/schema-v1.md` 和其他运行依赖保持。证据转移失败时保留未成功保存的源数据，并使准备失败，不能继续打包。
4. 检查准备后的构建无 runtime/debug 会话文件，schema 字节与本次 SchemaPath 一致，必需运行文件仍存在。原启动失败继续判失败，不因 finally 成功变为通过；日志目标本身没有产出文件时记录实际缺失，不伪造日志。
5. 指定 ArchivePath 时，在成功启动和内容检查之后生成临时 ZIP，再直接读取 ZIP 条目检查必需文件、schema 字节和无 runtime/debug 数据。全部通过后发布为最终 ArchivePath；临时 ZIP 位于分发目录之外，失败不留下冒充已验收的最终包。

文件检查与 ZIP 检查使用脚本内部的同一份内容规则。除定向移出 runtime/debug 数据外，其余构建文件原样保留；不以小型允许列表猜测并裁掉 Godot 运行依赖。这个命令用于受控构建产物，不用于玩家已解压目录。

## 两处调用与证据生命周期

CI 继续在 `build/windows/FarmExchange.exe` 原目录导出及验收。分发命令的证据输出使用 `build/validation/windows-<run-id>-<attempt>/`；成功才上传干净 `build/windows/`。另一步用 `!cancelled()` 和实际验收步骤执行状态上传已有诊断证据，即使启动步骤失败；上传失败仍是失败，不能遮蔽原启动错误。

release.yml 当前没有检出步骤。实施时先增加精确 `${{ github.sha }}` 检出，让脚本及 SchemaPath 来自已核对的发布提交，再沿用同 SHA 成功 CI 的来源检查和下载。对下载的原 `build/windows/` 调用同一个命令并提供版本 ZIP 路径；再启动生成的日志同样移出。只有命令成功返回、macOS 原包检查也成功后，才进入原创建 Release 步骤。验收证据与版本 ZIP 分开保存。

本地仍使用现有 dev 工作区及既定 Windows 中间导出目录；验收日志外移属于证据保存，不另建源码树或发布包副本。独立设计分支整合到 dev 后才执行正式本地闭环。

## FPS 测量与收集 Interface

将现有图形测量调用整理为 `tools/Run-PerformanceTests.ps1 -GodotConsole`。它在开始前移除本次固定的 `coverage/performance.json`、`coverage/performance.png`、`coverage/performance-end.png`，运行原场景，再校验原退出结果和报告存在性。失败继续抛出，不采用 continue-on-error。本地 `Run-Tests.ps1 -Performance` 在 headless 与覆盖率成功后调用它，保留现有一条命令的使用方式。

CI 的普通场景与覆盖率步骤不传 Performance；另设带 id 的 FPS 步骤，仅在手动勾选且前置步骤成功时调用测量脚本。这样 steps.fps.outcome 能明确区分成功、失败、取消及 skipped，不从覆盖率失败猜测测量是否执行。

新增 `tools/Save-PerformanceEvidence.ps1 -SourceDirectory -DestinationDirectory -MeasurementOutcome`，只识别上述三项本次文件。保存实际存在的文件并生成清单，记录每项存在或缺失及步骤结果；清单不是 FPS 测量报告，不填平均帧率或伪造成功。输出目录由本次 run-id/attempt 标识。

| 场景 | 收集与上传 | Job 结果 |
| --- | --- | --- |
| performance=false | 不测量、不收集 | 沿用普通流程 |
| 前置失败，fps skipped | 不生成性能产物，保留原错误 | 失败 |
| 测量成功，三个文件存在 | 上传 JSON、前后截图及清单 | 沿用成功结论 |
| 阈值失败，报告已落盘 | 上传现有文件及清单 | 原测量失败保持 |
| 测量启动后异常，仅部分或零文件 | 上传已有文件及明确缺失清单，不生成假 JSON | 原测量失败保持 |
| 取消 | 使用 !cancelled() 跳过后续收集，不承诺取消时上传 | 取消 |
| 测量成功但收集或上传失败 | 不伪装为证据已保存 | 失败 |

收集条件为 `${{ !cancelled() && inputs.performance == true && (steps.fps.outcome == 'success' || steps.fps.outcome == 'failure') }}`。上传步骤使用相同显式状态函数并要求 `steps.save-fps.outcome == 'success'`，不让缺省 success() 再次挡住失败证据。artifact 使用 `performance-<sha>-<run-id>-<attempt>`、保留 14 天；Windows 验收证据也包含对应提交和运行身份。

## 方案比较和 Depth

只排除 CI 上传日志会遗漏发布时第二次启动；删除整个 logs 会丢失同版本 schema；关闭 Runtime 会改变玩家诊断能力。完整分发准备命令把这项知识集中起来，CI 与发布学习同一 Interface。删除它就要在两个调用方重写启动失败处理、日志保存及目录/ZIP 检查，Depth 和 Locality 均有实际收益。

FPS 采用显式测量步骤而不是让上传 always() 猜测旧文件。测量脚本和证据脚本分别返回真实失败与文件事实，避免为保存证据改变性能判断。脚本数量对应真实职责，不按每条复制或压缩操作拆出转发 Module。

## 验证设计

新增 `tools/Test-ValidationArtifacts.ps1`，接入 CI 静态阶段，通过上述命令的真实文件输入和输出验证契约。工具使用当前 SDK 构建受控 .NET apphost；Windows runner 启动原生 exe，Linux 本地启动本机 apphost。这些结果仅证明工具契约，正式引擎导出程序仍在完整闭环中单独验收，不把不存在的 exe 或 Linux 夹具冒充 WindowsStartup。

- 两次调用路径均验证：已有哨兵 runtime/debug 日志及本次启动日志保存到证据目录；分发目录和最终 ZIP 无这些数据，schema 与 exe/PCK/程序集及其他依赖保留。
- 检查 schema 字节一致性在同一检出环境中比较；跨平台 CRLF/LF 内容对照只能解释差异，不能替代随包字节检查。
- 启动退出非零、超时、缺失必需文件、证据保存失败、错误路径关系、ZIP 缺 schema 或含哨兵日志均失败；已有源数据不得先删后复制。
- 干净 ZIP 解压后的真实首次启动产生玩家本次会话；该测试输出只作证据，不重新收入发布 ZIP。双平台同 SHA 与 macOS 字节保留回归。
- 性能成功、落盘后失败、只有首图、零产物、前置 skipped、取消及 performance=false 验证各自文件清单和条件。用失败夹具验证原测量错误不被收集成功覆盖；远端步骤语义仍需实际工作流验证，不把本地条件求值冒充 Actions 实跑。
- 对 ci.yml、release.yml 和本地入口进行 PowerShell/YAML 检查；整合到 dev 后完成项目规定的构建、测试、覆盖率、导出启动闭环。不得在设计阶段发布版本或关闭 issue。

契约红例先确认原上传条件在阈值失败时为 false，再用真实 apphost 启动复现旧整目录压缩含哨兵及本次会话。修复后的契约验证两次启动、schema 字节与其他依赖保留、解压首次会话、原始非零错误与日志复制错误并存、内容损坏、路径错误、FPS 旧三项输出全部清理和缺失清单；真实 60 秒进程超时也纳入 CI。运行记录保存在 `build/issue137-138-validation/` 的独立子目录，不纳入 Git。契约输出根自动创建 `.gdignore`，避免测试文件和截图哨兵进入 Godot 素材导入；实际 FPS 测量仍使用原 `coverage/` 输出。远端 job 仍失败及上传可下载的结论须以实际 Actions 运行补充，不能由本地条件求值替代。

## 分支文件所有权与集成

本分支独占 `.github/workflows/ci.yml`、`.github/workflows/release.yml`、`tools/Run-Tests.ps1`，以及新增 `Invoke-WindowsDistribution.ps1`、`Run-PerformanceTests.ps1`、`Save-PerformanceEvidence.ps1`、`Test-ValidationArtifacts.ps1` 和必要受控工具夹具。专题文档独占本方案、`windows-build.md`、`continuous-integration.md`、`release.md`。

不修改 Main、RuntimeLog、FarmGame、CameraController 或现有 FPS 计算场景。`AGENTS.md`、`docs/README.md`、`docs/project/testing.md` 在 dev 集成时由主 Agent 一次维护；两个 issue 的验收结果分别记录。三个设计分支之间没有实施前置依赖，完成后逐一整合到 dev；共享文档更新和完整验收完成前不宣称修复交付。
