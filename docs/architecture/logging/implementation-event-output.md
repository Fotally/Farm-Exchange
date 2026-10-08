# 统一事件输出与健康诊断实现

本实现履行[日志 Interface](interface-logging.md)；事件字段和格式契约统一见 [schema v1](../../project/runtime-log-schema-v1.md)，不在本页复制字段表。

本文描述 #126～#129 的实现：覆盖生命周期、完整主动交易、订单、行情、建拆与底线、生产窗口、耕作命令、暂停与倍率及开发流程。`RuntimeLog` 是公开组装 facade，建立 `LogOutput`、记录进程生命周期，在真实经营初始化后通过 `BindGame` 返回 `GameLog`；关闭时先结束局及订单等待和生产尾段，再提交会话结束，最后释放输出。

## 生产累计与安全封窗

公开 BindGame 是手动领域观察入口，不反向修改已存在 FarmGame 的关联。内部自动绑定通路由 FarmGame 构造器显式声明已接入生产事实；GameLog 仅在此通路组装 ProductionLog，手动关联保持空生产入口，关闭时也不生成生产窗口。该区别不改变公开签名、不额外保存业务状态。

建拆投影单独见[建拆与底线观察](implementation-building-observation.md)。生产窗口履行[日志 Interface](interface-logging.md)：Harvested、Consumed、Produced、WorkerCompleted 和 Cleared 在原成功提交点累计固定大小计数；不创建逐实例明细。FarmingSystem 原清理遍历返回 CropClearResult，处理器沿用 TryStart 的真实成功值，事件秒和暂停新建共用 StartIdleProcessors，因此无额外全图扫描。

ProductionLog 在初始化后捕获14商品和钱包真实边界，每60现实秒到期后只在命令或完整推进请求结束处输出。FarmGame 用 try/finally 围住原单秒和批量入口，以局日志内部布尔推进标记延后 checkpoint 内命令的检查；原业务不允许嵌套推进，本实现不新增嵌套推进兜底。非正常返回把当前窗标为缺少完整边界，原异常保持传播。局退出先封存订单等待，再输出生产尾段，最后 GameEnded。

提交字段和窗口重置全部在日志内部；成功构造的期末作为下窗期初，输出目标失败不重试或重复累计。采集器失败保留 CollectorFailure，未知资源快照为 null 并标记 BoundaryMissing；不把未知数量填零。满图夹具绕开真实播种和开工，受影响窗口标 UnregisteredMutation；下一真实边界只重新证明后续区间，旧投入历史仍未知。窗口编号和实际计数器切换确定增量归属，同毫秒封窗不重新按时间戳分配。窗口内商品守恒依赖同一区间交易事件齐全；汇总和逐次成交不能重复入账。

初始化生产窗先采集日历和资源，再读取窗口时钟，现实起点使用可空整数。首次时钟失败不会抹掉已取得的非零经营时间和资源；下一成功安全边界以 null 现实起点输出 Partial，明确 CollectorFailure/BoundaryMissing，而不是填默认 0 或改用另一时钟。封窗后从真实期末重新开始计时，已取得的真实 0 毫秒与未知 null 保持区别。

## 通用领域观察与输出

#129 继续使用同一 CommandObservation：CultivationLog 保存有界原请求投影和真实前快照，Complete 读取正式后快照；FarmGame 保留原业务判断及提交顺序，批量应用只把正式校验已得到的目标列表交给 Adapter，不由日志重跑判断或扫描剩余输入。TimeLog 观察暂停设置与 SimulationDriver 原接受路径，未变化的暂停不发变化事件，合法同值倍率选择仍保留一次选择事实。

GameLog 组装 Cultivation、Time 和 Scenario 三个具名入口，并从真实构造用途投影 Main 或 ScenarioIndependent。GamePurpose 为发布可用的纯枚举，日志不引用开发流程执行类型。独立流程创建局时复用 RuntimeLog，原地流程使用 FarmGame.Log；RunId 的保存和使用局限于流程事件及报告，不把流程 ID 塞入其他业务字段或采用隐式全局上下文。Main 把同一 TimeLog 接入唯一驱动，开发窗口先终结流程和报告，再释放独立局，主场景最后结束主局与会话。窗口和主场景退出入口可重复到达，流程终结及日志关闭各自保持幂等。

`GameLog` 拥有局身份、递增命令编号和日历上下文，维护初始化设施/库存基线及 `Trading`、`Orders`、`Market` 领域入口。`TradingLog` 保存原交易输入和真实前快照，返回 `TradeLogOperation` 或 `ProductSaleLogOperation`；订单命令返回 `OrderLogOperation`。真实业务执行后由调用方 Complete 或 Faulted，共用 `CommandObservation` 管理共同字段、收到/结束配对、异常关联及一次终结，领域回调只投影日志事实。FarmGame 在经营层局部收敛单商品与订单命令的执行手续，仍只执行原业务一次并原样传播异常；日志不执行这些回调，也没有泛用业务执行容器。所有主动命令及领域观察均在记录或提交前拒绝未登记来源。

单价由原 BuyCore/SellCore 实际报价读取处保存到 TradeResult，交易 Adapter 直接消费可选事实；读取前拒绝与空库存全售不补查当前价。批量全售原汇总循环保存七个实际价格，`ProductSaleResult` 保留合计和只读逐商品明细，失败明细成交量/货值为零。日志只读取真实前后资源，不根据汇总货值倒推单价，批量事实不拆成第二份逐商品收支。原买入事件名、来源、级别、字段次序及正常拒绝/异常语义保持。公共字段或框架修改不要求 FarmGame 逐字段适配。

订单领域在原判断分支取得稳定原因与实际覆盖，维持 AND/OR 短路；每单的观察记录和现实窗口归 TradeOrderLog，时间读取 LogOutput 同一个单调时钟。配置变化、撤销、停用、实际成交和局结束先封存应有合并尾段，不修改求值次数或业务状态。行情领域接收本轮已经产生的因素、实际公告及已提交报价，日志开关不增减玩法随机调用。具体领域规则分别由订单/行情接口维护。

每个记录入口声明 `LogEventDescriptor`，其中编号、名称、来源、级别和 runtime 资格作为不可变描述传给内部 `LogOutput.Submit`。生命周期与命令同样使用描述，健康事件由输出模块声明；通用输出没有 EventNumber 业务名称分支。独立测试可用同一 seam 提交新合法描述，无需把测试事件加入任何正式目录。

`LogOutput` 持有会话锁，在发布过滤后分配递增 Sequence、取得单调 UptimeMs，并向 MEL 提交 EventId、标准 `{OriginalFormat}` 与字段；公共头统一赋值，覆盖领域试图提供的同名值。`SerilogLoggerProvider` 消费标准格式键，不另生成 EventState 属性。内部 RuntimeIncluded 属性仅用于目标路由，并由 formatter 排除。runtime 只接收 runtime 资格为真且至少 INF 的事件；debug 接收全部已采集事件，两目标使用同一事件和序号。业务调用方不访问 MEL、Serilog、字典或 formatter。启动环境投影仍把空版本和非正/缺失窗口尺寸表示为未知 null。

`LogTextFormatter` 只处理已投影的 Serilog 值。头部字段固定，属性保留标量、集合和具名嵌套结构；字符串进行可见转义。它先形成完整有界文本再提交一条物理行，防止控制字符制造假头和异常续行失去关联。超限先保留可安全缩短的文本并显式标记；只有实际截断的字段才遍历原文本，按字段路径把原 Unicode 标量数写入 TruncatedOriginalCounts，异常再累计转义前 UTF-8 字节数与 LF 数量加 1 的原行数，全部使用 long。截断字段路径和计数来自同一内部集合，不另保存原文本。整事件包括截断元数据仍受 32 KiB 上限约束，核心无法完整输出时改写为有界拒绝说明，共用原序号与可取得关联键，不能误认原事件完整。公开 Capture 测试核对实际 formatter 的原计数和完整中文/emoji 片段，不单测私有计数函数。

领域集合采样先提供根层 TruncatedOriginalCounts（字段路径映射到 ItemCount），formatter 读取这些已投影的通用计数后再追加文本截断；由同一集合输出一份 Truncated/TruncatedFields/TruncatedOriginalCounts，不把集合元数据再作为普通属性重复写入。底座只理解通用计数维度，不认识订单配置字段。收到命令的采样路径包含 CommandArguments 前缀，实际领域事件保留自身路径。集合和文本同时截断仍保留各自原数量与完整结构。

文件滚动、共享与保留交给官方 File sink，不自写锁文件、清理器或重试业务。文件薄 Adapter 先取得没有最低级别包装的原始 sink，直接接入自身故障监听并捕获同步抛出异常，再在外层设置 runtime/debug 最低级别；关闭透传由 Serilog Wrap 管理。内存目标使用 Fallible。日志模块完整累计已观察故障次数，而独立诊断首次立即、随后每现实 60 秒至多一次，恢复单独通知。仅实际健康状态变化使健康记录待输出；稳定不可用不额外写健康事件，恢复后的真实输出再提交最新健康快照。后续实际输出没有观察到失败才标记恢复，已失败记录不会重放。故障次数与丢失数量不相等，因共享目标及失败时点无法完整归因，已知丢失数量保持未知。

runtime/debug 初始化分别捕获各自配置错误，失败目标保留未接入标记，成功目标仍注册到同一 provider，不因另一目标失败跳过会话连接。恢复观察只作用于已经成功接入、且本事件满足级别及采集资格的目标；未接入或本次被过滤者不会因为其 FailureVersion 没变就被误判恢复。连接后重新观察整体健康，使单目标配置失败持续 Degraded、双目标失败 Unavailable。成功构造的成熟 sink 的所有权保留在 provider，释放不另造文件管理层，真实 File 用例在关闭后独占打开文件核对释放。

真实普通文件占据日志目录的回归暴露了旧链接入的缺口：File 的最低级别先生成 RestrictedSink，而该包装不转发 ISetLoggingFailureListener；Fallible 只能向直接 inner 绑定，所以 runtime 内部失败没有进入目标监听、debug 仍有通知，整体错误表现为 Degraded。上述 Adapter 把监听放在级别包装之前，另接同步异常，不改变经营命令或文件路径。[RestrictedSink 官方源码](https://github.com/serilog/serilog/blob/v4.3.0/src/Serilog/Core/Sinks/RestrictedSink.cs)、[Fallible 官方源码](https://github.com/serilog/serilog/blob/v4.3.0/src/Serilog/Configuration/LoggerSinkConfiguration.cs)

Main 按首次局访问惰性组装，满足场景测试在 `_Ready` 前取得局的现有约定，同时保证日志先于真实开局；文件采集本阶段仅编辑器和 Windows 导出，其他平台导出使用 Disabled。OpenFile 在 Release 编译时封闭外部开启 debug 的设置。退出及初始化异常分别遵循正常释放与原异常传播；关闭顺序由会话统筹，场景不拼文件尾段。验证走公开 Interface、真实 provider 和 File，具体当前覆盖与环境验收边界见 Interface。
