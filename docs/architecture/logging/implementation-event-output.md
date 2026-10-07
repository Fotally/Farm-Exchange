# 统一事件输出与健康诊断实现

本实现履行[日志 Interface](interface-logging.md)；事件字段和格式契约统一见 [schema v1](../../project/runtime-log-schema-v1.md)，不在本页复制字段表。

本文描述 #126 已实现状态。当前 `RuntimeLog.EventNumber` 仍以固定表映射领域事件名称，公开 facade 同时负责局绑定与输出；[#127](https://github.com/Fotally/Farm-Exchange/issues/127) 内已确定先分离这些职责并明确共用观察终结机制，尚未实施。现行修正计划见[已确认方向](../../research/runtime-logging.md#已确认的后续修正方向)，不能把计划当作本文当前实现。

`RuntimeLog` 先建立进程会话，通过公开 `BindGame` 在经营真实初始化完成后返回 `GameLog`。GameLog 的公开 Interface 收敛为开始买入观察与释放；开始观察内部完成指令身份、原输入和前快照，返回公开 `BuyLogOperation`，由调用方在真实业务之后 Complete 或 Faulted 一次。FarmGame 只持有公开类型，执行原交易一次，不拼接指令编号或资源快照。CommandOrigin 属于经营语义，两个公开入口均在记录或提交前拒绝未登记值，不替用户选择默认来源。

GameLog 读取 FarmGame 的已有只读查询并投影专用字段，未启用采集时不构造局、库存或交易投影。合法来源 Buy 的原输入在业务验证前进入日志；业务提交独立执行，之后只观察真实结果，通用指令结果与领域结算分别承担关联和资源解释。单价由原 BuyCore 首次实际报价读取处保存到 TradeResult，日志直接消费这个可选事实，不能通过额外查询把读取前拒绝补成已经采用报价。GameLog 结束后不再输出该局事件；日志关闭也不改变已有经营状态。

统一输出持有会话锁，分配递增 Sequence、取得单调 UptimeMs，并向 MEL 提交 EventId、标准 `{OriginalFormat}` 及内部强类型领域入口生成的字段；`SerilogLoggerProvider` 消费标准格式键，不另生成 EventState 属性，把同一事件分发到 runtime 与开发 debug 两目标。业务调用方不访问 MEL、Serilog、内部字段集合或 formatter。日期、设施与库存的专用结构在 GameLog 中维护，不序列化业务对象全部成员，不增加状态拥有者。启动环境投影把空版本和非正/缺失窗口尺寸表示为未知 null，不用默认值冒充观测结果。

`LogTextFormatter` 只处理已投影的 Serilog 值。头部字段固定，属性保留标量、集合和具名嵌套结构；字符串进行可见转义。它先形成完整有界文本再提交一条物理行，防止控制字符制造假头和异常续行失去关联。超限先保留可安全缩短的文本并显式标记；只有实际截断的字段才遍历原文本，按字段路径把原 Unicode 标量数写入 TruncatedOriginalCounts，异常再累计转义前 UTF-8 字节数与 LF 数量加 1 的原行数，全部使用 long。截断字段路径和计数来自同一内部集合，不另保存原文本。整事件包括截断元数据仍受 32 KiB 上限约束，核心无法完整输出时改写为有界拒绝说明，共用原序号与可取得关联键，不能误认原事件完整。公开 Capture 测试核对实际 formatter 的原计数和完整中文/emoji 片段，不单测私有计数函数。

文件滚动、共享与保留交给官方 File sink，不自写锁文件、清理器或重试业务。文件薄 Adapter 先取得没有最低级别包装的原始 sink，直接接入自身故障监听并捕获同步抛出异常，再在外层设置 runtime/debug 最低级别；关闭透传由 Serilog Wrap 管理。内存目标使用 Fallible。日志模块完整累计已观察故障次数，而独立诊断首次立即、随后每现实 60 秒至多一次，恢复单独通知。仅实际健康状态变化使健康记录待输出；稳定不可用不额外写健康事件，恢复后的真实输出再提交最新健康快照。后续实际输出没有观察到失败才标记恢复，已失败记录不会重放。故障次数与丢失数量不相等，因共享目标及失败时点无法完整归因，已知丢失数量保持未知。

runtime/debug 初始化分别捕获各自配置错误，失败目标保留未接入标记，成功目标仍注册到同一 provider，不因另一目标失败跳过会话连接。恢复观察只作用于已经成功接入的目标；未接入者不会因为其 FailureVersion 没变就被误判恢复。连接后重新观察整体健康，使单目标配置失败持续 Degraded、双目标失败 Unavailable。成功构造的成熟 sink 的所有权保留在 provider，释放不另造文件管理层，真实 File 用例在关闭后独占打开文件核对释放。

真实普通文件占据日志目录的回归暴露了旧链接入的缺口：File 的最低级别先生成 RestrictedSink，而该包装不转发 ISetLoggingFailureListener；Fallible 只能向直接 inner 绑定，所以 runtime 内部失败没有进入目标监听、debug 仍有通知，整体错误表现为 Degraded。上述 Adapter 把监听放在级别包装之前，另接同步异常，不改变经营命令或文件路径。[RestrictedSink 官方源码](https://github.com/serilog/serilog/blob/v4.3.0/src/Serilog/Core/Sinks/RestrictedSink.cs)、[Fallible 官方源码](https://github.com/serilog/serilog/blob/v4.3.0/src/Serilog/Configuration/LoggerSinkConfiguration.cs)

Main 按首次局访问惰性组装，满足场景测试在 `_Ready` 前取得局的现有约定，同时保证日志先于真实开局；文件采集本阶段仅编辑器和 Windows 导出，其他平台导出使用 Disabled。OpenFile 在 Release 编译时封闭外部开启 debug 的设置。退出及初始化异常分别遵循正常释放与原异常传播；关闭顺序由会话统筹，场景不拼文件尾段。验证走公开 Interface、真实 provider 和 File，具体当前覆盖与环境验收边界见 Interface。
