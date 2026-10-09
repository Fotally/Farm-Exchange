# 日志专项验收实现

本页履行[日志接口](interface-logging.md)的故障隔离、发布门禁和有限开销约定；字段解释以 [schema v1](../../project/runtime-log-schema-v1.md) 为准，执行命令见[测试说明](../../project/testing.md#日志专项验收130)。报告属于具体提交的构建证据，本文不固化未经执行的通过声明。

## 测试职责与证据

测试通过真实日志入口和 MEL → Serilog provider → formatter 输出验证行为。既有 `TestLogging`、`TestLoggingFiles`、`TestScenarioLogging` 覆盖格式、共享输出、滚动保留、不可写目录、故障恢复、多个局及重复关闭；`TestTradeDiagnostics` 对关闭、runtime、development 和坏目标使用相同种子与输入，比对真实交易、行情、平静推进及异常。生产、工人及公共预算由各自专项测试覆盖，专项验收不重新实现业务判断。

`TestLoggingFaults` 补充三个已有测试未完整表达的条件：

- 可控写入目标每条延迟 5 ms，经真实 provider/formatter 输出一笔交易的三条事件；实测命令承受同步写入延迟，验证业务正常，不声称真实慢磁盘也达到 FPS 门槛。
- 输出目标抛出 HResult 为 `0x80070070` 的 `IOException`，验证真实业务成功、独立健康故障、恢复以及不补写缺失交易。这是磁盘空间不足异常的受控注入，不填满宿主盘，也不宣称实际盘耗尽。
- 正常建立 File 目标后，将下一滚动文件路径占为目录；后续连续真实命令持续触发实际 File 滚动写入失败，核对失败次数增长与业务资源不变。没有备用路径、异步队列或业务重试。

## 异常记录与传播回归

`TestExceptionLogging` 经真实 `FarmGame.AdvanceTick`、`AdvanceTicks` 和 `SimulationDriver.Advance` 验证异常采集，不调用共用异常观察的私有实现，也不新增业务注入开关：

- 单 tick 使用已有日历构造夹具到达 `uint.MaxValue`，批量使用真实容量拒绝；均检查 `AdvanceValidation`、原异常类型、消息、抛出位置与实际经营秒。单 tick 夹具会建立对应历史行情，不能以反射改写日历来跳过真实初始化。
- 真实检查点抛出可辨认的原异常，核对 `ReferenceEquals` 和堆栈；驱动非法帧时间、容量及宿主预算回调分别验证 `DriverValidation` / `DriverBudget`，前置失败不得增加已执行批次数。驱动传播批量检查点异常时仍保留最内层 `Checkpoint`，没有伪造 `CommandId`。
- 公开 `Trading.BeginBuy/Faulted` 观察放在真实检查点中，验证命令 → 批量 → 驱动传播仅有一条 `BusinessException`，独立命令终结和失败批次仍保留。这是公开领域观察的契约测试，不声称真实买入业务本身触发该受控异常。同一个异常对象用于后续独立推进或同次推进内的独立兄弟命令必须重新记录；不同对象同消息不合并，跨局调用保留各自的局身份。
- 受控 `Exception.ToString()` 投影故障及仅拒绝 `BusinessException` 的 Capture 输出故障均检查原异常身份、健康故障和投影/写入尝试次数；失败不导致外层换上下文重试，仍独立提交 `CommandFinished=Faulted`。`TestLoggingFaults` 另用真实 File 滚动路径冲突重复验证命令 → 批量 → 驱动链，确认只投影一次。
- 相同种子在开启、关闭和未配置日志时，先对照同一正常推进终点的结果，再分别验证异常传播；检查点抛异常前已经完成的真实推进不作为正常对照基线。正常短推进不增加逐 tick 成功事件。初始化 `FatalException` 和报告保存的 `ScenarioReportSaveFailed` 继续由既有 `TestLogging` / `TestScenarioLogging` 核对各自语义。

上述用例注册在统一 `TestSuite`。异常功能验收与图形 FPS、16×长帧优化分别记录；本轮执行统一功能套件、逐模块覆盖率、Release 探针及正式导出启动，剩余性能达标继续归 #66，不以本页的测试清单代替实际通过证据。

## 65 秒图形矩阵

`TestLoggingPerformance` 使用正式 Main、地图、工人表现和时间驱动，父节点 `_EnterTree` 经 Debug 专用 `LoggingFactory`/`InitialSeed` 组装 seam 在子节点初始化前选择档位；退出清空，正式默认行为不变。日志只由 Main 每帧采样一次，第二局推进不得重复采样进程帧。

| 负载 | 日志档位 | 格数 |
| --- | --- | --- |
| 16,384 实例满图，1× | 关闭 / runtime / development / 持续 File 故障 | 4 |
| 同一满图，16×请求 | 同上 | 4 |
| 同一满图 1×，每现实秒 10 组等价加工品买卖，另一个独立局执行相同买卖并推进 | 关闭 / runtime / development | 3 |
| 满图 16×，32 个现存锚点、3 工人及选定事件 | development | 1 |

共 12 格，每格新进程、种子 130、1080P、无垂直同步及帧率上限，预热 2 秒、采样至少 65 秒。镜头固定地图中心加同一现实时间正弦水平位移，不随工人经营位置变轨迹；两张截图用于核对相同负载可见内容。原有 8 秒 `TestFullWorldFps` 的移动工人基线保持；高倍率专项检查实际推进、设施数和选定倍率，不要求三工人在采样两端恰好都移动。

有界格使用 `ProductionStateChanged`、`WorkerTaskChanged`、`WorkerMoved`、`EventTickTiming`，本测试请求最长 120 秒、500 条。公共采集硬上限仍为 5000 条；16×的 65 秒窗口预计推进约 1040 个经营秒，而此前高倍率证据约 4000 个经营秒才累计到 5000 条，因此本测试选择允许范围内的较小预算，验证真实自动停止。报告以 `RequestedCaptureEventLimit` 标明本次请求 500，保存实际 `DiagnosticCaptureEnded`，并要求关局前按 `EventLimit` 结束且 `CapturedCount=500`；不能将本格称为实测 5000 条。若负载未达到限额，格判定无效，不把关局停止写成到限自动停止。正常档还须观察到非尾段 `PerformanceSummary`，确保采样跨越真实 60 秒窗口。

报告记录实际帧数、FPS/P95、三代 GC 次数、进程托管分配、真实推进量与第二局推进量、交易次数、实际事件数、输出健康和独立诊断次数。日志汇总的 QuietTicks/EventTicks 包含预热与尾段，而采样推进量仅取截图后的窗口，两者不混用。关闭或不可写档没有可读日志不代表没有实际推进。16×记录请求与实际吞吐，不人为调整玩法或调度来过门槛。

## 进程生命周期

`Test-LoggingLifecycle.ps1` 启动真实 Main 的独立图形进程；场景先推进并重复释放一个独立局，再让主局运行。场景以 `Environment.ProcessId` 写出原子完成的 `ready.json`；脚本核对该 PID 的实际可执行文件、创建时间及通向本次启动 PID 的进程树，保存 `process-identity.json` 后才写确认工件。主动退出等待该确认，避免核验前引擎已经退出。`*_console.exe` 可能只是包装进程，窗口消息、强杀和退出码始终针对已核实的实际引擎；清理只处理这次启动的包装对象与已核实引擎，不按名称寻找或终止其他 Godot。随后分别测试：

- 主动 `SceneTree.Quit`：真实 `_ExitTree` 结束主局与会话。
- 向实际 Godot 窗口发送 Windows `WM_CLOSE`：经过引擎窗口关闭路径退出。
- 操作系统强杀：只允许先前独立局的正常结束证据，主局和会话结束记录缺失。缺口只描述这次受控强杀，不推断其他缺失原因，也不承诺强杀末条必达。

正常两种退出必须恰好两条 GameEnded、一条 SessionEnded；重复 Dispose 不新增结束。所有模式退出后独占打开实际日志文件，验证进程文件句柄已释放。主场景所有权保持原实现，不添加生产用途退出入口。

## 实际 Release 门禁

`Test-LoggingRelease.ps1` 从 `tools/logging-release-probe/Program.cs.txt` 在 `build/` 生成临时控制台工程，引用指定的实际 Release 或导出 DLL；不修改正式工程、开发依赖或发布资源。验证 `Capture` 和 `OpenFile(development:true)` 仍禁止开发采集；有毒只读集合的 Count、索引和枚举一旦被读取就抛出，以证明开始入口在范围构造前返回。反射调用实际生产/加工的明细快照入口，以 null 状态依赖确认门禁先于状态读取。随后执行真实买入和 120 秒批量推进，确认 runtime 业务事件仍输出且不创建 debug 文件。

同一个实际程序集随后分别通过 Capture 和 File 目标触发真实批量容量、检查点、驱动容量及预算异常；核对原类型、消息、堆栈、经营上下文和实际阶段。公开命令观察接入真实检查点两次，核对同次传播仅一次、后续独立操作仍有记录、命令终结完整以及异常对象原样抛出。探针输出中两种目标各有六条异常；单 tick 日历上限和故障目标注入仍由统一功能套件验证，探针不反射修改业务状态。

探针输出程序集 SHA-256/MVID，工具再与输入发布 DLL 的哈希比较。该探针验证托管日志门禁，不能代替 Godot 正式导出启动；后者仍由构建闭环单独验收，并检查发布目录 `logs/schema-v1.md` 与当前源文件一致。
