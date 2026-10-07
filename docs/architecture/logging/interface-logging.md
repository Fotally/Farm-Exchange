# 运行时日志 Interface（#126）

代码位于 `scripts/logging/`；字段与单位只在 [schema v1](../../project/runtime-log-schema-v1.md) 维护。当前实现只覆盖会话、经营局、单商品主动买入及对应指令关联、初始化异常、日志健康。其他事件登记仍是后续接入约定，不能解释为全场已有覆盖。

## 调用入口与生命周期

`RuntimeLog.OpenFile(directory, development, environment, retention?, diagnostic?)` 创建同步输出会话。目录由场景组装点决定，开发和发布的采集选择、真实构建名彼此独立：开发编译时 `development` 决定是否建立 debug 目标，Release 编译在入口强制关闭开发采集，外部传入 `true` 也不能开启 debug 文件或降低最低采集级别；`LogEnvironment.BuildKind` 由启动元数据提供 `Debug/ExportDebug/Release`，未知值保持 null。环境值是纯启动数据，不读取业务状态。GameVersion 空白归一为 null；窗口尺寸只有宽、高均为正时保留真实值，headless 的 0×0 或缺值为 null，不填假定分辨率。文件工厂不替调用方定位 exe 或 Godot 项目，也不切换失败路径。

`RuntimeLog.Capture(writer, environment?, diagnostic?)` 经过真实 MEL → Serilog provider → 同一 formatter 输出到内存文本；writer 由调用方拥有，关闭会话不释放它。`RuntimeLog.Disabled()` 完全关闭采集，经营局不分配日志身份或投影。默认 `new FarmGame(seed)` 同样不采集、不写文件，测试和无宿主的数据计算无需初始化文件。

`RuntimeLog.BindGame(game, seed)` 接收已经真实初始化的局及其实际种子，记录真实初始化基线并返回公开 `GameLog`；关闭采集返回 null。调用方在同一经营线程绑定和使用，每局只绑定一次；`new FarmGame(seed, logging)` 在设施、库存、钱包与报价真实初始化之后完成这次绑定。开局种子仍使用原随机源，日志身份使用独立 GUID，不消耗玩法随机序列。日志读取经营的现有只读查询，不建立第二份正式状态。

`GameLog` 的公开领域 Interface 只有 `BeginBuy(commodity, quantity, origin)` 与幂等 `Dispose()`。BeginBuy 保存原请求、分配局内命令身份、记录收到指令、取得提交前快照，返回轻量 `BuyLogOperation`；已结束或关闭采集返回 null。调用方执行原业务命令一次后，使用 `Complete(真实TradeResult)` 或在异常接收点使用 `Faulted(原Exception)` 终结关联。观察对象首次终结后不再输出，日志不执行业务回调、不重试业务；调用方无需学习或持有指令编号、私有资源快照和字段字典。

`game.Buy(commodity, quantity, origin)` 默认来源 `Player`，开发流程显式使用 `Scenario`；来源类型为经营模块的 `FarmExchange.Gameplay.CommandOrigin`，自动结算没有接入本指令入口。FarmGame 与 GameLog 的公开入口都要求来源为这两个已登记值；其他枚举值在业务提交与指令记录前抛出 `ArgumentOutOfRangeException(nameof(origin))`，资源零修改，也不分配一个假定来源的命令。该约束不依赖是否开启日志，不新增正常交易失败枚举。

每笔买入使用同一局内递增的 `CommandId`，依次提交 `CommandReceived`、真实交易提交后的 `TradeFinished`、`CommandFinished`。通用结束不重复资金或库存变动；正常拒绝仍为 INF，非法商品没有伪造价格和库存值。`UnitPriceCents` 仅投影真实 `TradeResult.UnitPriceCents`：报价读取前拒绝省略，报价读取后拒绝和成功保留执行实际价，不另查当前可查询价、不根据失败码猜判断分支。有值使用 HasValue 判断，零与 null 不混同；真实行情仍沿用至少 1 分的既有规则。该结果约定见[交易 Interface](../trading/trading-service/interface-trading-service.md)。真实业务异常记录 `BusinessException` 与 `Faulted`，随后原样传播。日志投影、格式或输出故障不改变经营结果、不重新执行业务；日志调用不捕获经营提交本身。

`RuntimeLog.InitializationFailed(error)` 供实际不能初始化的接收点记录 `FatalException`，调用方继续传播原异常。异常文本作为数据转义并有明确预算，不允许注入事件头。

`game.Dispose()` 幂等结束本局日志关联，`EndReason=Released`；不会修改经营资源。宿主直接关闭会话时，会话先结束所有仍绑定局（`Shutdown`），再提交 `SessionEnded` 并释放目标。`RuntimeLog.Dispose()` 同样幂等；正常 flush 不保证强杀或断电末条必达。调用示例：

```csharp
using var log = RuntimeLog.OpenFile(logDirectory, development, environment);
using var game = new FarmGame(17, log);
TradeResult result = game.Buy(wheatRaw, 5); // 调用方只依赖真实业务结果
```

```csharp
using var text = new StringWriter();
using var log = RuntimeLog.Capture(text);
using var game = new FarmGame(17); // 本例明确演示领域观察 Interface
using var context = log.BindGame(game, 17);
var observation = context?.BeginBuy(invalidCommodity, -1, CommandOrigin.Scenario);
TradeResult result = game.Buy(invalidCommodity, -1, CommandOrigin.Scenario);
observation?.Complete(result); // 原请求、真实正常拒绝与结束在内部关联
```

## Main 的组装约定

主场景通过私有 `GameState` 在首次访问经营局时先建立日志，再创建真实局。测试在 `_Ready` 之前读取 `Main.Game` 仍取得同一个局；`_Ready` 不替换它、不补造开局记录。编辑器引擎运行使用项目 `build/logs`；Windows 导出使用 `OS.GetExecutablePath()` 所在目录的 `logs`。其他平台导出明确使用 Disabled；macOS 业务日志尚未确认和接入，不向 `.app` 内写入，也不另选备用路径，经营照常运行。退出先释放已有局，再关闭日志；未创建局的场景退出不额外初始化。启动失败记录实际初始化异常并关闭已建立目标，异常继续抛出。

## 输出实现与健康

MEL 与 Serilog 仅存在于日志内部。领域入口使用原请求、真实 `TradeResult` 和只读状态，调用方不能拼任意字典或传入 Serilog logger。MEL State 提供标准 `{OriginalFormat}`，由 provider 消费，不输出 EventState、OriginalFormat 等传输伪属性。序号分配、MEL 提交和两目标分发放在同一会话锁内；相同事件在两类文件有相同时间、SessionId 和 Sequence。Sequence 表示采集接收顺序，不依赖现实时间递增。

File 使用成熟 `Serilog.Sinks.File 7.0.0`：UTF-8 无 BOM、同步逐事件 flush、`shared:true`、按现实日与文件大小滚动、内置保留策略。runtime 初值 10 MiB × 20 文件，debug 20 MiB × 10 文件；`LogFileRetention` 可在验收中使用小阈值。共享目录的不同会话分别排序；不承诺跨进程因果顺序或跨进程事务式两目标写入。尺寸是近似上限，最后一条完整事件可以超出文件阈值，不切碎事件。

两目标各自初始化和诊断：一目标配置失败不关闭另一已成功配置的原目标，也不换路径或重试；失败目标保持未接入/失败状态，后续成功输出不能将它误判为恢复。一目标失败时健康持续 Degraded，可用目标照常记录生命周期、原买入请求与真实结果；全部目标失败为 Unavailable。已构造成功的成熟 sink 交给同一 provider 持有，关闭会话时统一释放。

内部薄 formatter 将每个事件输出为一条物理行：固定头、中文摘要、具名属性结构。字符串引号、反斜杠、CR/LF/TAB、控制字符可见转义，中文保持可读；null、0、缺失分别保留原含义。日期只投影 Year/Month/Day。摘要最多 256 个 Unicode 标量，普通字符串 512 个；异常最多 64 行/16 KiB。实际截断才标注 `Truncated/TruncatedFields` 并计数原文本，`TruncatedOriginalCounts` 按字段路径给出原数量：普通字符串/摘要仅 `ScalarCount`（Unicode 标量数），Exception 另给 `Utf8Bytes`（转义前 UTF-8 字节数）和 `LineCount`（原文本 LF 数量加 1，CRLF 只计一次）。三个计数为 long，不把 UTF16 长度当标量数，不把日志转义后的物理行数当原异常行数，也不保存原文本副本。整事件连同这些元数据的 UTF-8 输出超过 32 KiB 时，仍改为同一序号的 `EventPayloadRejected` 与原事件名、可取得关联键，不输出貌似完整的原异常或结算。

`Health` 返回已观察的 `Healthy/Degraded/Unavailable`、首次故障运行毫秒和故障次数；`KnownLostCount=null`，不假装已知丢失量。各目标拥有独立 `ILoggingFailureListener`，不监听全局 `SelfLog`：文件目标的薄 Adapter 在级别过滤前直接向原始成熟 File sink 绑定监听，同时捕获同步 Emit 抛出异常；内存目标使用 Serilog 4.3.0 的 Fallible。不能把被过滤包装隐藏的内部故障当作一次成功输出，文件共享、轮转、保留和关闭仍由成熟 File 负责。写盘/投影故障首先交给独立 `diagnostic`（默认 stderr，Main 接 Godot 警告）：本次故障首次立即通知，随后现实 60 秒最多再通知一次；恢复可通知一次。每次已观察故障仍完整累计 FailureCount，通知抑制不是日志丢失计数。诊断点自身异常也隔离，不进入业务 logger。仅健康状态变化或恢复时补 `LoggingHealthChanged`；全部目标稳定不可用时不反复尝试额外健康记录。后续真实成功输出观察到恢复，不重放失败记录。缺少健康事件不能证明采集完整。

## 验证入口与边界

`TestLogging` 通过公开 Interface 验证真实初始化、两个局同号命令、原始非法输入、成功/拒绝资金库存、实际单价事实（负数/零/容量提前拒绝缺失，资金不足/成功保留执行价）、合法来源与非法来源提交前异常、公开观察的真实完成/原异常关联与一次终结、幂等释放、关闭采集的经营等价、连续故障完整计数与独立通知有界、恢复、独立诊断隔离、中文/null/0/缺失/转义、Unicode 及异常预算、截断原数量（普通中文/emoji 标量、异常行数/中文字节/emoji 三计数及完整字符片段）、核心超限拒绝，以及真实 provider 无传输伪字段、空版本/无窗口为 null、正尺寸保留。`TestLoggingFiles` 使用真实 File 验证共享目录两宿主和并行提交、两目标相同事件、单会话物理序号、正常文件释放、小阈值滚动与跨关闭重开的保留、发布 profile 不建 debug、实际普通文件占据目录的初始化故障、运行中滚动目标失效及后续内部通知、仍可写目标输出健康变化，以及 debug/runtime 任一配置失败的独立存活、双失败、真实结算、持续健康和关闭释放；样例保存在 `build/test-results/logging/`，不纳入 Git。以上是测试用例范围，执行结果以本轮统一验收记录为准。

两组测试注册到 `TestSuite`。跨滚动文件读取按固定日期和编号的 Ordinal 文件名顺序，不依赖系统文化；会话 Sequence 继续核对该文件顺序内的实际物理记录，不重新排序事件掩盖回退。单商品 Buy 的交易结算仍没有可由合法公开调用构造的抛出分支；不为覆盖率增加异常注入开关。公开 BuyLogOperation 的异常观察测试保留原异常引用、关联及传播；非法来源测试验证实际新增的公开入口约束，结算本身的异常传播同时由代码审查核对。系统时钟回拨、真正跨进程同时启动、磁盘耗尽、强杀和断电需另做环境验收，当前测试不声称已证明这些情形。订单、卖出、生产汇总、受控详细诊断和重放执行器未实现。
