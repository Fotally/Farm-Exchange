# 运行时日志现状与两类日志设计

调研日期：2026-10-02（北京时间）。关联 [issue #82](https://github.com/Fotally/Farm-Exchange/issues/82)。本次交付检查和设计，未安装日志依赖或修改游戏代码。

用户已确认：日志分为给开发者的 Debug 日志和发布版本日志；前者可以详细，后者记录关键事件；Windows 发布包的业务日志放在游戏自身目录的 `logs/`，第一版输出可读 `.log`，两类日志都要有明确的 schema 和字段释义供 AI 分析。[日志 schema v1 草案](../project/runtime-log-schema-v1.md)是字段与事件语义的唯一维护入口。下文的框架选型、采集范围、文件大小及保留数量仍是实施建议，尚未成为已生效规则。经营规则仍以[系统规划](../project/roadmap.md)及玩法文档为准。

## 当前并非完全没有运行日志

检查的是当前 `dev` 工作区，HEAD 为 `1baa79b991818a2e025dd2986b53099c9e973786`，包含用户原有的未提交改动；结论针对检查时的文件内容。

| 检查项 | 证据 | 结论 |
| --- | --- | --- |
| 游戏主动日志 | 搜索 `scripts/` 的 `GD.Print`、`GD.PushError`、`GD.PushWarning`、`Console`、`ILogger`、Serilog、NLog、Trace 等调用，未发现主动日志记录 | 缺少业务运行日志 |
| 框架依赖 | [FarmExchange.csproj](../../FarmExchange.csproj)仅有 `Godot.NET.Sdk/4.7.2`，目标 `net8.0`，没有日志包 | 没有接入 .NET 日志框架 |
| 项目配置 | [project.godot](../../project.godot)没有显式覆盖文件日志配置 | 不能据此判断引擎文件日志被禁用 |
| 实际日志 | `C:\Users\l\AppData\Roaming\Godot\app_userdata\Farm Exchange\logs\` 有 `godot.log` 和 4 个带时间的历史文件 | 已有 Godot 自动文件日志 |
| 当前日志内容 | 检查时 `godot.log` 为 185 字节，包含 Godot 4.7.2 Mono、OpenGL、驱动和 GPU 信息 | 能了解启动环境，不能追查交易、生产或工人任务 |
| 测试与构建输出 | `tests/` 使用 `GD.Print`、`GD.PushError`；`coverage/`、`build/` 中保留验收输出；历史 Godot 日志也含测试输出 | 开发验收日志已有，但不等于玩家业务日志 |

Godot 官方说明：桌面默认写入 `user://logs/godot.log`，每次运行轮转，默认保留 5 个日志。Release 中 stdout 的刷新行为与 Debug 不同，stderr 会逐次刷新；不能把输出到控制台等同于业务事件已可靠写入文件。[Godot Logging](https://docs.godotengine.org/en/stable/tutorials/scripting/logging.html)

## 同类作品可参考什么

| 参考对象 | 一手资料证明的做法 | 本项目可采用的思路 |
| --- | --- | --- |
| Factorio（异星工厂） | 用户数据目录保存 `factorio-current.log` 和 `factorio-previous.log`；记录运行秒数、版本、系统、图形与初始化过程，官方问题报告需要日志 | 启动环境、相对运行时间、保留历史，以及让用户容易找到日志。[官方 Wiki](https://wiki.factorio.com/Log_File) |
| Stardew Valley 的 SMAPI 模组工具 | 提供 `SMAPI-latest.txt` / `SMAPI-crash.txt` 的查找和分享说明；解析页面展示游戏版本、系统、启动时间和级别筛选 | 按级别区分信息，保留错误上下文，提供清楚的取日志步骤。[SMAPI 官方日志页面](https://smapi.io/log) |

SMAPI 是第三方模组工具，这不是对原版《星露谷物语》内部日志的断言。上述公开资料证明的是诊断方式，并没有给出商业游戏全部业务事件清单。下面的交易、工人、生产记录是结合 Farm Exchange 现有代码提出的设计，不冒充同类游戏已经采用的方案。

## 已确认的两类日志

| 项目 | 发布日志 `runtime` | 开发者日志 `debug` |
| --- | --- | --- |
| 目的 | 还原关键操作和结果，诊断玩家报告的问题 | 解释某次判断、状态转换或性能异常为何发生 |
| 构建 | Debug 和 Release 都生成 | 仅开发构建生成 |
| 级别 | Information、Warning、Error、Critical | 包含关键事件，并额外记录 Debug；Trace 只用于专项调试 |
| 内容 | 已提交的业务事件、有限周期汇总、异常 | 执行输入、检查结果、中间计算、状态变化、耗时 |
| 高频过程 | 按时间聚合，不能逐格逐秒落盘 | 记录状态变化；逐步移动等 Trace 限定观察对象并主动开启 |
| 数据采集 | 只收集关键事件所需字段 | Release 应在构造详细数据前关闭采集，不只关闭文件输出 |

建议 Debug 构建同时写两类文件，便于验证发布日志是否足够；开发者文件包含关键事件，排查时可单独阅读。Release 只创建 `runtime` 文件。Godot 自带 `godot.log` 继续承担引擎诊断，两类业务日志不替代它。

正常玩家操作失败（钱不够、库存不足、地块被占用、季节不适宜）属于业务结果，不自动升级为 Warning 或 Error。自动策略条件未满足属于正常等待；不应每秒记录“未成交”。

## 发布日志记录清单

| 类别与事件 | 记录时点及字段 | 建议级别 |
| --- | --- | --- |
| `SessionStarted`、`SessionEnded` | 启动及正常退出；游戏版本/构建标识、Godot/.NET 版本、系统、渲染方式、窗口尺寸、运行时长、最终经营秒、暂停状态 | Information |
| `GameInitialized` | 初始化实际使用的随机种子、3 座农田与 2 座加工场地的锚点/作物、开局金币；种子只用于定位初始化和随机行情，不承诺完整重放 | Information |
| 建造、拆除、改种、库存底线调整 | 命令完成后一次记录：输入子格、解析的锚点、设施/作物、费用、修改前后值或稳定失败原因；道路按实际完成的建造命令记录 | Information |
| 暂停、继续 | 状态确实变化时记录经营秒；不能每次刷新按钮都记录 | Information |
| 即时交易 | 商品、方向、请求量、实际成交量、执行报价、货值、费用、金币/库存/冻结前后值和失败原因；失败也记录真实零修改结果 | Information |
| 委托创建、同 ID 编辑、撤销、启停 | 已有订单 ID、频率、预算方式、数量/目标、条件组、现金基准/保留线、本单冻结前后值、结果 | Information |
| 委托成交及生命周期变化 | 订单 ID、实际成交量/价/货值/手续费、本单及总冻结前后值、剩余单据状态；只有真实成交或状态变化才写 | Information |
| 行情报价及公告 | 公告实际出现、商品报价实际更新时记录商品、前后价、相关因素、报价日和下次排期；不记录每秒检查 | Information |
| `ProductionSummary` | 建议每 60 个现实秒聚合一次已推进经营区间：七作物收获量、七加工品产量、越季清理数量、金币与 14 商品总量/可用量/冻结量；退出补记未满周期部分。暂停且无经营变化时省略重复生产汇总 | Information |
| `PerformanceSummary` | 实施时新增轻量采样，建议每 60 个现实秒汇总：帧间隔平均/最大值、经营 tick 平均/最大耗时、实际 tick 数、设施数；当前主场景尚无这些统计，普通发布不额外保存每帧明细 | Information |
| 业务异常 | 异常类型、消息、堆栈、发生阶段、相关命令或订单/锚点、经营秒；同一异常由接收它的边界记录一次 | Error |
| 无法初始化或继续运行的异常 | 已实际发生且确实导致当前流程终止时记录原因和阶段 | Critical |

周期用现实时间，因为暂停或经营速率不应改变文件记录间隔；每条仍携带经营时间。具体 60 秒周期是建议，实施时确认。降雨目前没有主场景天气生成，不将它描述为已经发生的玩家事件；将来实际接入后再记录天气变化。

发布日志按提交结果记账：不能在成功检查、扣款之前写“成交成功”。这些日志是排错记录，受轮转、写入失败和异常退出限制，不是永久账本、存档或事件重放系统。

## 开发者 Debug 日志额外记录什么

| 模块 | Debug 信息 | 控制记录量的方法 |
| --- | --- | --- |
| 放置/播种规则 | 输入、范围/占用/余额判断、季节、预计成熟时刻、检查失败原因 | 实际命令与状态变化时记录；鼠标悬停预检不逐帧记录 |
| 农田/加工 | 播种、供水、生长开始、成熟、清理、领取、加工开始/完成；锚点、作物、数量、前后状态 | 默认记录状态转换；满地图专项调试可限定锚点，避免整图输出 |
| 工人 | 任务认领、目标锚点、任务完成/失效/释放、版本凭据重验原因 | Debug 记录任务变化；Trace 才记录指定工人逐秒移动，不逐帧写插值 |
| 即时交易 | 数量、报价、资金、可用库存、冻结、容量各项执行检查 | 每次实际命令记录一次，不在 UI 刷新时计算或记录 |
| 委托策略 | 组内条件结果、组间结果、动态目标差额、可用预算、保留线、费用计算与取整 | 保持现有每个未暂停经营 tick 的评估时点，仅在条件满足状态或稳定等待类别变化时记录 Debug；不能比较包含实时数值的原因文本来判定变化，数值变化的逐次细节仅对选定订单开启 Trace |
| 市场 | 供需、季节、事件、原料成本、目标价格及限幅前后值 | 只在公告/报价变化时记录 |
| 经营性能 | 收获、加工、领取、工人、换日、委托阶段的耗时与累计统计 | 默认聚合；具体单 tick 阶段明细只在专项调试开启 |
| 地图/镜头/UI | 视口尺寸变化、倍率/中心变化、块重建数量、命令分发 | 记录变化和操作边界，不记录每帧位置或重复界面刷新 |

完整地图有 16,384 个生产实例、147,456 个占用子格。生产进度每秒逐实例记录会迅速放大文件和分配开销；即使开发版本，也必须控制对象范围或使用聚合。常规采集不遍历整图重建日志快照，不序列化 Godot Node 或完整对象树。

## 统一字段与可读格式

使用 UTF-8 可读文本 `.log`，固定事件头、中文消息和具名字段；不是把 JSONL 改扩展名。两类文件共享同一 schema，Debug 扩充事件与字段，异常可以带多行堆栈。具体格式、字段类型/单位、必填条件、枚举、事件记录时点、汇总区间和 AI 分析注意事项均见[日志 schema v1](../project/runtime-log-schema-v1.md)，本研究文档不再复制字段字典。

采用框架的消息模板和结构化属性，在文件输出时用 Serilog 文本 formatter 呈现；不要让变量含义只藏在中文句子里。微软 `ILogger` 支持类别、级别、模板及结构化字段。[Microsoft Logging](https://learn.microsoft.com/en-us/dotnet/core/extensions/logging/overview)

## 如何存放和清理

按用户确认，Windows 发布包的业务日志写在 `FarmExchange.exe` 同级的 `logs/` 下。使用 `OS.GetExecutablePath()` 取得导出程序路径并定位其父目录；不使用进程工作目录或 C# 程序集子目录猜测发布包根目录。目录须可写，实际失败时明确报告，不自动换回 AppData。[Godot 程序路径接口](https://docs.godotengine.org/en/stable/classes/class_os.html#class-os-method-get-executable-path)

```text
FarmExchange/
  FarmExchange.exe
  logs/
    schema-v1.md                     # 发布时随包附带的同版本字段说明
    runtime/
      farm-20261002.log              # 关键事件，两种构建都写
      farm-20261002_001.log          # 当天达到大小限制后滚动
    debug/
      farm-debug-20261002.log        # 仅开发构建创建
      farm-debug-20261002_001.log
```

`logs/schema-v1.md` 在后续发布流程中从仓库 schema 文档随包附带，不由每次运行生成，也不参与 `.log` 轮转；本次仅创建仓库设计文档。编辑器运行的 Debug 日志建议放在项目 `build/logs/`，不得写到 Godot 编辑器安装目录；macOS `.app` 的目录约定在其日志接入时单独确定，不把 Windows 的 exe 父目录规则照搬到 app 内部。

Godot 自带日志当前仍位于 `%APPDATA%\Godot\app_userdata\Farm Exchange\logs\godot.log`。它由引擎启动期管理，并未随本次设计调整被移动；若将它也纳入发布包目录，需要在实际接入时配置引擎启动期日志路径并验证，不能宣称 C# 初始化后改设置就能迁移早期输出。[Godot 日志说明](https://docs.godotengine.org/en/stable/tutorials/scripting/logging.html)

建议采用下面的初始预算，由框架轮转和清理匹配的文件：

| 类别 | 时间轮转 | 单文件上限 | 保留数量 | 预算含义 |
| --- | --- | --- | --- | --- |
| 发布关键日志 | 每个现实日 | 10 MiB | 最新 20 个文件 | 常规约 200 MiB；20 个文件不是保证保留 20 天 |
| 开发 Debug 日志 | 每个现实日 | 20 MiB | 最新 10 个文件 | 常规约 200 MiB，详细记录会更快轮转 |
| Godot 引擎日志 | 保持现有按会话轮转 | 本次不改变 | 现有默认 5 个 | 独立于业务日志预算 |

时间和大小两种轮转必须同时启用；只设置大小上限而不启用大小轮转可能停止记录。文件数预算不是严格磁盘配额：单条事件和格式开销可能使文件略超上限，长时间没有新写入时也不等于后台持续清理。该建议用数量限制保持配置简单，不额外开发清理程序。[Serilog File sink](https://github.com/serilog/serilog-sinks-file)

启动追加写入，依靠 `SessionId` 区分一天内多次运行。Debug 文件夹在 Release 中不创建；以前开发运行留下的文件也不能被误认为本次发布生成。并行开发进程的文件共享须在实际采用的 File sink 配置中验证；推荐其现成 `shared` 选项，不自己维护文件锁或另写轮转器。

第一版提供文档中的目录定位和按发生时间选择文件的方法即可。玩家报告问题时提供对应时间段的 runtime 文件和 godot.log；开发者额外提供 debug 文件。新增“打开日志目录”按钮、自动打包和远程上传不纳入本次设计实施范围。

## .NET 框架选型

| 方案 | 已有能力及限制 | 判断 |
| --- | --- | --- |
| `Microsoft.Extensions.Logging` | 官方 `ILogger` / `ILoggerFactory`，支持级别、类别和结构化模板；官方通用 provider 包含 Console、Debug、EventSource 等，没有通用本地滚动文件 provider，Azure 文件 provider 是特定托管环境 | 适合作为业务代码接口，配文件框架。[官方 providers](https://learn.microsoft.com/en-us/dotnet/core/extensions/logging/providers) |
| Serilog + File | 结构化事件、文本/JSON 文件、按时间/大小轮转及保留数量；`Serilog.Extensions.Logging` 可接到 `ILogger` | 推荐，适合此处两个文件目标和明确级别过滤。[File](https://github.com/serilog/serilog-sinks-file)、[ILogger 集成](https://github.com/serilog/serilog-extensions-logging) |
| NLog | File target 支持 UTF-8、文件归档和保留限制，也有 .NET 日志集成 | 同样可用，当前没有要求需要同时引入两个框架。[官方 File target](https://github.com/NLog/NLog/wiki/File-target) |
| 仅 Godot 内置日志 | 已有引擎输出收集和会话轮转；自行约定消息内容仍需开发，当前不能满足两类结构化业务记录 | 保留作引擎日志，不另造业务文件框架 |

推荐组合：`Microsoft.Extensions.Logging`、`Serilog`、`Serilog.Extensions.Logging`、`Serilog.Sinks.File`。使用 Serilog 已有文本 formatter 和输出模板；不额外写日志序列化器。包版本在实施时锁定能支持当前 `net8.0` 的稳定版本并实际验证，不照搬文档中其他 .NET 版本的示例版本号。

第一版以低频、聚合事件的 File sink 直接写入，普通记录不启用额外缓冲；正常退出关闭 logger factory 和文件目标。这样配置简单，但文件 I/O 仍可能影响主线程，必须在满地图场景测量，不能宣称零性能开销。Serilog File 官方支持默认逐事件 flush；flush 不等于断电时必然持久化。[File sink 性能说明](https://github.com/serilog/serilog-sinks-file#performance)

`Serilog.Sinks.Async` 可把写入转到后台，但默认队列满会丢弃新事件，选择阻塞会改变主线程等待行为，退出还需排空。因此暂不作为第一版必装组件；若性能测量确实要求改为异步，先沟通丢弃/阻塞策略，再实施。[Async 官方说明](https://github.com/serilog/serilog-sinks-async)

## 接入边界与异常

建议只增加一个小型日志启动/关闭模块，创建 factory、两类文件目标、构建过滤和本次会话字段；业务通过 `ILogger` 写事件。Godot 场景继续手动组装依赖，不为日志引入 Web Host、完整 DI 容器、插件系统或另一个经营状态仓库。

日志初始化必须早于 `new FarmGame`，才能记录真实初始种子和开局配置。[Main](../../scripts/ui/Main.cs)当前在字段初始化中创建 `_game`，实施时需要调整其组装顺序。不能等 `_Ready` 结束才开始采集初始化事件。

命令/经营汇总放在 [FarmGame](../../scripts/gameplay/FarmGame.cs)；自动订单实际结算在 [TradeOrderBook](../../scripts/trading/TradeOrderBook.cs)，正式报价/公告在 [MarketQuotes](../../scripts/market/MarketQuotes.cs)，各自于完成提交的位置记录一次。农田、加工、工人的详细记录只在需要 Debug 事件的地方接入。钱包、库存、每个 UI 刷新或快照查询不重复记录同一笔业务。

发布过滤以构建类型为基础。开发构建的 MEL 与 Serilog 根级别都允许 Debug，再将 runtime 文件限制为 Information+，debug 文件允许 Debug+；发布根级别为 Information，只注册 runtime。文件目标的级别限制不能恢复已被根过滤丢弃的事件。[Serilog 分目标过滤](https://github.com/serilog/serilog/wiki/Configuration-Basics#overriding-per-sink)

专项 Trace 启用时，开发构建的 MEL 根级别调整为 Trace，Serilog 根级别和 debug 文件目标同时调整为 Verbose（MEL Trace 对应 Serilog Verbose）；runtime 文件仍限制为 Information+。另以选定订单/工人/锚点限制详细采集范围。专项结束后恢复默认 Debug 级别；Release 不开放这个开关。[级别转换源码](https://github.com/serilog/serilog-extensions-logging/blob/dev/src/Serilog.Extensions.Logging/Extensions/Logging/LevelConvert.cs)

采集详细数据前检查构建类型及 `IsEnabled(Debug)`；专项 Trace 同理。关键事件只需要少量字段，不能为产生日志再次执行交易、重跑随机算法或重新推演工人任务。周期统计在现有状态转换处累计并定期输出，不增加遍历 147,456 子格的日志扫描。[微软级别守卫](https://learn.microsoft.com/en-us/dotnet/core/extensions/logging/high-performance-logging#log-level-guarded-optimizations)

异常需要保存 C# 原异常及堆栈。Godot 4.6 的 [CSharpInstanceBridge](https://github.com/godotengine/godot/blob/4.6-stable/modules/mono/glue/GodotSharp/GodotSharp/Core/Bridge/CSharpInstanceBridge.cs#L34)和 [ExceptionUtils](https://github.com/godotengine/godot/blob/4.6-stable/modules/mono/glue/GodotSharp/GodotSharp/Core/NativeInterop/ExceptionUtils.cs#L91)展示了绑定层捕获部分调用异常再输出的路径。因此不能承诺只挂 `AppDomain.UnhandledException` 就收齐异常；该源码证据固定在 4.6，具体接入还需用项目 4.7.2 引擎验收，不将 GDScript 调用栈设置当成 C# 异常方案。日志记录不得把异常转换成成功、重试或继续经营的兜底方案。

第一版保留引擎错误到 `godot.log`，在实际业务命令和 tick 边界记录带经营上下文的异常，遵循原有失败语义。Godot 4.5 起提供 `Logger` 与 `OS.AddLogger`，本机 4.7.2 的 GodotSharp API XML 已确认有相应 C# 接口；是否将引擎错误再转到业务日志是后续独立选择。无需为了第一版日志替换整个引擎输出流。[Godot Logger](https://docs.godotengine.org/en/stable/classes/class_logger.html)

正常退出补记周期汇总和 `SessionEnded`，释放日志目标。强制杀进程、断电或原生崩溃不能保证退出事件和最后记录一定存在；缺少 `SessionEnded` 只能作为未完整退出的线索，不能直接判定具体崩溃原因。官方说明原生崩溃堆栈的可用性取决于匹配的调试符号，文件日志不替代 crash dump。[Godot 崩溃日志](https://docs.godotengine.org/en/stable/tutorials/scripting/logging.html#crash-backtraces)

初始化或写入实际失败时明确输出日志故障到既有诊断渠道；不静默假装文件已写入，不自动改路径、转云端或添加重试策略。两类业务文件由成熟 File sink 管理，游戏不自行实现文件系统容错。

## 后续实施验收建议

1. Debug 实际运行生成 runtime 与 debug 两类文件；Release 实际运行仅新生成 runtime，详细采集关闭；运行正常和拒绝的建造、交易、订单命令，核对结果及资源前后字段。
2. 验证真实自动订单成交、冻结释放和按整数分收取的手续费；发布文件不出现每秒条件未满足噪声。成熟、加工与越季清理的累计量和实际库存变化一致。
3. 用缩小的测试文件阈值验证时间/大小轮转、数量清理、中文和异常字段；验证多次启动不覆盖历史，以及采用文件共享配置后的并行开发运行；从不同工作目录启动时仍写在 exe 同级 `logs/`。
4. 验证暂停后仍可记录主动交易，经营汇总不虚构 tick；正常退出补记尾段、关闭文件。针对实际异常路径验证一次记录，不以强制杀进程保证最后一条落盘为验收要求。
5. 用指定 Godot 对 Debug 与 Windows Release 导出测试；满地图负载、真实窗口 FPS 前后对比，检查正式日志采集没有每实例每秒写入；性能指标沿用项目既有平均至少 60 FPS、P95 帧间隔不超过 16.67 ms 的标准。
6. 逐事件核对 schema 的必填字段、类型、单位、枚举与语义；发布包附带同版本 `schema-v1.md`，给 AI 的日志截段能据此区分实际时间/经营时间、总量/可用量/冻结量、成交/拒绝/等待，以及异常续行。
7. 代码实施另按 issue 范围完成中文模块文档、AGENTS 系统结构、独立代码审查、模块覆盖率与编译/场景/导出/启动闭环。本次只执行文档检查和研究结论审查，不把未来验收写成已通过。

本次检查与设计文档完成后，框架组合、周期、保留预算和专项 Trace 开关可作为具体实施方案供用户确认；已确认的两类日志划分、Windows 游戏目录存放、可读 `.log` 及 schema 要求无需再次讨论。
