# 运行时日志 Interface（#126～#129）

代码位于 `scripts/logging/`；字段与单位只在 [schema v1](../../project/runtime-log-schema-v1.md) 维护。当前覆盖会话、经营局、完整主动交易、订单命令/等待/成交、实际公告及正式报价、建拆与底线命令、每局生产窗口、耕作表和手动接管、暂停与倍率选择及开发流程生命周期，并包含对应指令关联、初始化异常和日志健康。受控诊断与性能汇总按 #130 接入。

## 当前职责与通用提交

[#127](https://github.com/Fotally/Farm-Exchange/issues/127) 先完成领域 Adapter 与通用输出的职责修正，再接入完整交易、订单与行情。本文说明当前接口，测试实际结果以统一验收记录为准。职责方向见[已确认安排](../../research/runtime-logging.md#已确认的后续修正方向)。

- `RuntimeLog` 是公开组装 facade，负责进程生命周期、已绑定局的集合和关闭顺序；不维护交易字段或业务事件名称名单。
- `GameLog` 拥有局身份、局内命令编号、日历上下文与局生命周期，公开 `Trading`、`Orders`、`Market`、`Gameplay`、`Cultivation`、`Time`、`Scenario`，内部组装 `Production`。初始化设施和库存基线属于其生命周期投影，关闭前封存订单等待和生产窗口尾段。
- `TradingLog` 直接承担主动交易 Adapter，拥有请求、事件描述、前后资源快照及真实结果投影；`TradeLogOperation` 与 `ProductSaleLogOperation` 保存本次观察所需原输入与前快照。
- `TradeOrderLog` 拥有订单配置投影、命令和成交资源观察及有界等待记录；`MarketLog` 投影实际公告、已提交报价和本轮真实因素，不重新求值或消费随机数。
- `GameplayLog` 观察建拆与原料底线命令；[建拆实现](implementation-building-observation.md)说明真实子格、锚点和费用投影。`ProductionLog` 只拥有逐局生产累计、资源边界与封窗状态，业务继续拥有生产、库存和资金。
- `CultivationLog` 观察年度表创建、更新、删除、批量应用和两种手动接管；`TimeLog` 观察暂停命令与实际接受的倍率选择；`ScenarioLog` 关联开发流程的启动、终结和报告保存结果。三个 Adapter 只维护日志投影与观察状态，不拥有耕作安排、倍率、流程执行或报告文件。
- 内部 `CommandObservation` 拥有收到/结束配对、共同命令字段、异常关联和幂等终结。其完成回调只生成日志投影；从不接受、执行或重试业务命令。
- 内部 `LogOutput` 提供统一 `Submit(description, message, fields)`，拥有会话标识、单调时间、序号、过滤、格式、输出和健康。`LogEventDescriptor` 由所属记录入口给出编号、稳定名称、来源、级别和 `RuntimeIncluded`，通用底座没有领域名称分支。新合法描述无需修改底座即可通过同一提交 Interface 输出。

通用提交属于日志模块内部 seam，业务调用方仍只使用具名领域入口。字段使用已有标量、列表及具名嵌套字典；事件描述、字段名与含义须遵守 schema，不能借任意业务对象自动序列化增加字段。会话公共头由输出统一赋值，领域不能覆盖身份、序号、名称或来源。内部路由属性 `RuntimeIncluded` 和 MEL 的传输字段不进入文件文本。runtime 只接收有 runtime 资格且级别至少 INF 的事件；开发 debug 接收全部已提交事件；发布入口在分配序号前排除不具资格或低于 INF 的描述。

## 低侵入接入与维护约定

### 耕作、时间和开发流程（#129）

`GameLog.Cultivation.BeginCreate/BeginUpdate/BeginDelete/BeginApply/BeginManualControl` 只开始观察原请求；真实经营调用完成后传入原结果，异常经 `Faulted` 关联后继续原样抛出。`FarmGame` 六个耕作命令接受默认 Player 的来源，并在业务或记录前拒绝非法来源。预检与快照查询不形成命令。`GetCultivationPlan(id)` 直接返回指定表的独立快照或 null，日志不为单表观察复制整套表目录。

创建和更新区分原请求、正式 Before 与 After；删除在真实解除引用完成后投影实际结果。批量应用沿用原业务全列表校验和子格去重，只在完整校验后把同一目标索引列表交给观察入口；提前拒绝不补跑剩余校验，未知去重数为 null，实际检查到空列表时为 0。目标和作物条各最多采样 32 项并标明原数量，采样不限制可提交的业务集合。手动接管从真实前后快照描述引用解除、当前轮保留和下一轮安排，同种立即重启仍是丢弃当前轮，不因选种相同声称保留。

`FarmGame.SetPaused(paused, origin)` 每次形成完整命令对，`PauseChanged` 只在真实状态变化时输出。`SimulationDriver` 接收可选 `GameLog.Time`，在原倍率校验和接受路径记录 `SimulationRateSelected`；合法同值选择仍保留来源和 `ValueChanged=false`，不声称解除了暂停。日志不拥有倍率和未完成 tick。

`FarmGame(seed?, logging?, purpose?)` 的 `purpose` 默认为 `GamePurpose.Main`，独立流程显式使用 `ScenarioIndependent`；该枚举没有开发模块依赖。`FarmGame.Log` 只读提供已有局上下文，关闭采集为 null。原地流程复用主局，独立流程复用宿主日志会话并拥有独立局标识；公开手动 `BindGame` 仍仅供手动领域观察，不接入自动生产累计。

配置成功加载且运行局已关联后、首个流程命令前记录 `ScenarioStarted`；终结、报告成功或保存异常在各自真实路径记录。RunId 只进入流程事件和报告，其他经营记录通过同一 GameInstanceId、Sequence 及显式 Scenario 命令来源关联，不建立环境隐式 scope。流程结束不结束原地玩家局；窗口退出先幂等中断运行流程并尝试报告保存，再结束独立局；Main 最后关闭主局与会话。报告保存失败保留实际异常，独立于流程已产生的 Outcome。

`TestCultivationLogging`、`TestTimeLogging`、`TestScenarioLogging` 注册到统一 `TestSuite`，验收覆盖真实成功/拒绝、前后语义、批量去重、来源与一次终结、主局/独立局关联、报告保存及关闭顺序；实际运行结果以统一验收记录为准。

### 建拆、底线和生产窗口（#128）

公开 `RuntimeLog.BindGame(game, seed)` 保留手动领域观察语义，不把日志重新安装到已存在的 FarmGame，也不启动生产窗口。手动调用交易/建拆观察仍有生命周期及真实领域记录，但不能据此声称自动生产覆盖。只有 `new FarmGame(seed, logging)` 在真实初始化后通过内部自动绑定启动生产累计；其 Dispose 和宿主关闭才补生产尾段。`TestProductionLogging.CheckManualBinding` 验证手动绑定后真实推进不会产生貌似完整的生产汇总。

`GameLog.Gameplay.BeginPlace/BeginRemove/BeginSetRawReserve` 记录原请求并返回一次观察；经营仍按原顺序执行，真实结果传给 `Complete`，异常传给 `Faulted` 后原样传播。`FarmGame.TryPlace/BuildFarm/BuildProcessor/RemoveBuilding/SetRawReserve` 增加默认 `Player` 的 `CommandOrigin`，开发流程准备显式传 `Scenario`；非法来源在记录和业务修改前抛出。建造预检不形成命令。建造实际扣款来自 `PlacementResult.ChargedCents`，拆除和拒绝为零；拆除前保存真实子格对应的实例，库存底线不触发领取。建造触发的全场加工领取归生产流量，不混入资金事件。

`ProductionLog` 内部入口只接收真实事实：`Harvested(crop, quantity)`、`Consumed(crop)`、`Produced(crop)`、`WorkerCompleted(kind)`、`Cleared(CropClearResult)`。收获和完工均在库存提交成功后累计；开工只在 `TryStart` 成功后计一份；工人只消费调度器成功回调，降雨、等待、重复供水不计入。清理结果由农田原遍历返回逐作物实际轮数，未成熟清除不是库存损失份数。没有从表现结果缓存或额外全场扫描推导事实。

换季事件秒中，促熟收获先于工人阶段，日期清理又在工人阶段之后：一块田可能先收获上一轮，随后工人播下新一轮，最后新一轮被禁生换季清理。此时收获份数、播种次数、清理轮数都应累计；同秒出现收获和清理不等于同一轮重复记账。测试同时检查真实库存与这三个事实，不改变已有相位顺序。

初始化完成后建立首个资源边界；现实预算为 60 秒。生产累计与资源快照只由本局日志持有，`CommandObservation` 终结后请求检查窗口，`AdvanceTick/AdvanceTicks` 完整请求返回后检查窗口。推进内的 checkpoint 可以提交命令，日志内部推进标记让命令检查延后至外层请求结束；不拆 quiet，也不补造多个整齐的超时窗口。暂停且没有调用可延长窗口，暂停交易和新建仍在真实命令结束后封窗。`GameLog.End` 在 `GameEnded` 前补一次 `IsPartialWindow=true` 尾段，重复关闭不重复输出。

每窗包含七作物完整流量字典、14 商品 Total/Available/Frozen 和钱包三种金额的 Start/End。同一实际期末快照作为下一窗期初；编号按本局递增。交易通过独立成交事件解释，冻结不是流量，跨窗投入与完工分别计入各自窗口。尾段短不代表覆盖不完整。日志累计异常标 `CollectorFailure`，边界缺失或非完整推进退出标 `BoundaryMissing`，开发满图/表现夹具在真实修改前标 `UnregisteredMutation`；有已知边界的缺失窗为 `Partial`，起终边界均未知为 `Unknown`。新真实边界之后可重新声明本区间采集 `Complete`，不补齐旧窗或夹具注入在制品的历史投入。单纯写盘失败由输出健康与缺失记录解释，不能混同为进程内累计丢失；任何 Complete 都不保证提供的文件完整。

内部 `RuntimeLog.Capture(writer, productionUptime, diagnostic)` 用于真实 FarmGame 的可控窗口测试，生产窗口读取该单调测试时钟，公共事件头仍读取 LogOutput 的真实单调时钟；公开 Capture/OpenFile 的时钟和 Interface 不变。生产测试不等待 60 秒、不反射内部状态。

首次读取窗口时钟失败时，仍保留先采集的真实日历和资源期初，`IntervalStartUptimeMs` 为 null，不用 0 冒充未知时间。下一次取得真实时钟的安全边界立即封存该窗，`CoverageStatus=Partial` 并标记 `CollectorFailure/BoundaryMissing`；真实期末时间和资源作为下一窗期初，后续区间可恢复 Complete。这一空值条件只属于新生产事件，不改变订单等待窗口等其他事件的时间契约。测试以非零经营秒开局，验证未知现实起点、真实经营起点和真实的 0 毫秒恢复时点彼此不同。

`TestProductionLogging` 验证暂停新建触发全场领取、底线与冻结阻挡、跨窗投入完工及交易等式、工人成功/降雨、换季清理与促熟、单秒批量及关闭采集等价、checkpoint 内命令不拆窗、多局与正常关闭、夹具覆盖、采集和输出故障区别，并经真实 OpenFile 留下双文件样例。`TestBuildingLogging` 验证原始输入、成功拒绝、子格锚点、实际费用与来源、预检无事件和日志故障隔离。实际执行结果由统一验收记录确认。

### 领域接入约定

后续领域接入集中在已有命令入口、真实状态提交点和局生命周期，通过少量具名领域 Interface 传递原请求与真实结果。字段投影、日志身份与关联、前后资源快照、汇总去重、采集开关、分流、格式与故障处理由日志 Module 拥有；业务调用方不拼属性字典，不选择事件级别、文件或第三方框架类型，也不维护日志专用状态。

优先消费已有结果与只读查询；确实缺少执行时的业务事实时，只给所属结果补最小具名事实，例如交易实际读取的单价。不能为日志拆开原有原子提交、改变检查顺序、继续短路判断或重跑业务；不把日志协议字段塞进业务结果。避免在 getter、基础库存增减、每帧 UI 刷新和每实例每秒循环散布调用。生产汇总在真实增量点接入日志内部累计，不要求业务循环逐步拼装统计窗口。

框架替换、字段布局、文件策略和采集预算的调整应局限在日志 Implementation；仅新增领域或实际业务事实需求才扩展对应领域 Interface。后续接入若使调用方重复编排身份、快照、过滤或结束手续，应先收敛日志 Interface，再继续铺设调用。优先在现有日志 Module 内局部重构，业务执行权和检查顺序仍由原 Module 维护。审查以一次日志实现调整需要改动多少业务调用点核对维护成本。

当前交易和订单命令仍有显式开始观察、完成或异常终结调用，不承诺零侵入；FarmGame 在本层局部收敛同类命令的执行手续。日志只接受原输入和真实结果，MEL/Serilog/File 与字段协议保持在日志模块内部；#128～#130 沿用这一职责划分。

## 调用入口与生命周期

`RuntimeLog.OpenFile(directory, development, environment, retention?, diagnostic?)` 创建同步输出会话。目录由场景组装点决定，开发和发布的采集选择、真实构建名彼此独立：开发编译时 `development` 决定是否建立 debug 目标，Release 编译在入口强制关闭开发采集，外部传入 `true` 也不能开启 debug 文件或降低最低采集级别；`LogEnvironment.BuildKind` 由启动元数据提供 `Debug/ExportDebug/Release`，未知值保持 null。环境值是纯启动数据，不读取业务状态。GameVersion 空白归一为 null；窗口尺寸只有宽、高均为正时保留真实值，headless 的 0×0 或缺值为 null，不填假定分辨率。文件工厂不替调用方定位 exe 或 Godot 项目，也不切换失败路径。

`RuntimeLog.Capture(writer, environment?, diagnostic?)` 经过真实 MEL → Serilog provider → 同一 formatter 输出到内存文本；writer 由调用方拥有，关闭会话不释放它。`RuntimeLog.Disabled()` 完全关闭采集，经营局不分配日志身份或投影。默认 `new FarmGame(seed)` 同样不采集、不写文件，测试和无宿主的数据计算无需初始化文件。

`RuntimeLog.BindGame(game, seed)` 接收已经真实初始化的局及其实际种子，记录真实初始化基线并返回公开 `GameLog`；关闭采集返回 null。调用方在同一经营线程绑定和使用，每局只绑定一次；`new FarmGame(seed, logging)` 在设施、库存、钱包与报价真实初始化之后完成这次绑定。开局种子仍使用原随机源，日志身份使用独立 GUID，不消耗玩法随机序列。日志读取经营的现有只读查询，不建立第二份正式状态。

`GameLog.Trading` 提供 `BeginBuy(commodity, quantity, origin)`、`BeginSell(...)`、`BeginSellAll(commodity, origin)`，返回 `TradeLogOperation`；`BeginSellAllProducts(origin)` 返回 `ProductSaleLogOperation`。它们保存原请求、分配局内命令身份、记录收到指令、取得提交前快照，已结束或关闭采集返回 null。调用方执行原业务一次后使用 `Complete(真实结果)` 或在异常接收点使用 `Faulted(原Exception)` 终结；全加工品观察消费同次结算的 `ProductSaleResult`。首次终结后不再投影或输出，日志不执行业务、不重试；调用方不持有指令编号或字段字典。

`FarmGame` 的主动买卖、单商品全售、全部加工品出售及四个订单命令默认来源为 `Player`；开发流程买入和创建一次卖单显式使用 `Scenario`。来源类型为 `FarmExchange.Gameplay.CommandOrigin`。经营入口与公开领域观察均在记录/业务提交前拒绝其他枚举值，抛 `ArgumentOutOfRangeException(nameof(origin))`，资源零修改，不分配假定来源的命令；关闭采集也保留此约束。自动订单成交不伪装为玩家命令。

每笔买入使用同一局内递增的 `CommandId`，依次提交 `CommandReceived`、真实交易提交后的 `TradeFinished`、`CommandFinished`。通用结束不重复资金或库存变动；正常拒绝仍为 INF，非法商品没有伪造价格和库存值。`UnitPriceCents` 仅投影真实 `TradeResult.UnitPriceCents`：报价读取前拒绝省略，报价读取后拒绝和成功保留执行实际价，不另查当前可查询价、不根据失败码猜判断分支。有值使用 HasValue 判断，零与 null 不混同；真实行情仍沿用至少 1 分的既有规则。该结果约定见[交易 Interface](../trading/trading-service/interface-trading-service.md)。真实业务异常记录 `BusinessException` 与 `Faulted`，随后原样传播。日志投影、格式或输出故障不改变经营结果、不重新执行业务；日志调用不捕获经营提交本身。

固定卖出与单商品全售同样使用 `TradeFinished`；固定请求保留 RequestedQuantity，全售使用 AllCommodity，不把当时库存伪装为固定请求。空库存全售成功但未读取价，省略 UnitPriceCents。全部加工品出售只输出一次 `AllProductsSold`，七条 TradeLines 按目录顺序携带原结算循环实际读取的价格、成交数量和库存六字段；冻结和空库存行仍存在且成交为零，拒绝时各行成交也为零。合计不伪造单价，不另输出逐商品 TradeFinished。

`GameLog.Orders` 提供 BeginCreate/BeginEdit/BeginCancel/BeginSetEnabled，返回 `OrderLogOperation`，共用同一命令关联。FarmGame 在 BindGame 后把本局 Orders 接入 TradeOrderBook；实际成交在完整资金/冻结/状态提交后记录一次。等待事实来自原短路判断的稳定原因与覆盖标记，不按中文反推或补跑未执行条件。每单每 60 个现实秒最多输出 8 条中间变化；首次、真实恢复与成交/撤销/停用前尾段不漏，同原因数值变化不刷屏，超限记录合并数量和最后状态。编辑先封存旧配置观察，局结束先输出尾段；这不改变订单求值时点。订单配置每份最多前 8 组/总 32 条条件，元数据保留原集合数量；跨商品编辑以最多两项 OrderCommodityStocks 分开展示原、新商品资源。详见[订单接口](../trading/trade-order-book/interface-trade-order-book.md)。

`GameLog.Market` 由经营协调在两处实际日历推进路径传给 MarketQuotes。MarketLog 只观察真实发布的公告和正式价格提交，保存本轮原计算因素，不因初始化历史回放补造新闻，不重新算价或取随机数。报价日、下一实际报价日及事件头当前日期保持各自含义，见[行情接口](../market/market-quotes/interface-market-quotes.md)。

`RuntimeLog.InitializationFailed(error)` 供实际不能初始化的接收点记录 `FatalException`，调用方继续传播原异常。异常文本作为数据转义并有明确预算，不允许注入事件头。

`game.Dispose()` 先封存本局订单等待尾段，再幂等结束局关联，`EndReason=Released`，不会修改经营资源。宿主直接关闭会话时，会话先结束所有仍绑定局（`Shutdown`），再提交 `SessionEnded` 并释放目标。`RuntimeLog.Dispose()` 同样幂等；正常 flush 不保证强杀或断电末条必达。调用示例：

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
var observation = context?.Trading.BeginBuy(invalidCommodity, -1, CommandOrigin.Scenario);
TradeResult result = game.Buy(invalidCommodity, -1, CommandOrigin.Scenario);
observation?.Complete(result); // 原请求、真实正常拒绝与结束在内部关联
```

## Main 的组装约定

主场景通过私有 `GameState` 在首次访问经营局时先建立日志，再创建真实局及注入本局 TimeLog 的唯一 SimulationDriver。测试在 `_Ready` 之前读取 `Main.Game` 或 `Main.Driver` 仍取得同一组对象；`_Ready` 不替换它们、不补造开局记录。编辑器引擎运行使用项目 `build/logs`；Windows 导出使用 `OS.GetExecutablePath()` 所在目录的 `logs`。其他平台导出明确使用 Disabled；macOS 业务日志尚未确认和接入，不向 `.app` 内写入，也不另选备用路径，经营照常运行。开发窗口共享会话；退出先由窗口幂等中断流程并保存报告，再释放已有主局，最后关闭日志；未创建局的场景退出不额外初始化。启动失败记录实际初始化异常并关闭已建立目标，异常继续抛出。

## 输出实现与健康

MEL 与 Serilog 仅存在于日志内部。领域入口使用原请求、真实 `TradeResult` 和只读状态，调用方不能拼任意字典或传入 Serilog logger。MEL State 提供标准 `{OriginalFormat}`，由 provider 消费，不输出 EventState、OriginalFormat 等传输伪属性。序号分配、MEL 提交和两目标分发放在同一会话锁内；相同事件在两类文件有相同时间、SessionId 和 Sequence。Sequence 表示采集接收顺序，不依赖现实时间递增。

File 使用成熟 `Serilog.Sinks.File 7.0.0`：UTF-8 无 BOM、同步逐事件 flush、`shared:true`、按现实日与文件大小滚动、内置保留策略。runtime 初值 10 MiB × 20 文件，debug 20 MiB × 10 文件；`LogFileRetention` 可在验收中使用小阈值。共享目录的不同会话分别排序；不承诺跨进程因果顺序或跨进程事务式两目标写入。尺寸是近似上限，最后一条完整事件可以超出文件阈值，不切碎事件。

两目标各自初始化和诊断：一目标配置失败不关闭另一已成功配置的原目标，也不换路径或重试；失败目标保持未接入/失败状态，后续成功输出不能将它误判为恢复。一目标失败时健康持续 Degraded，可用目标照常记录生命周期、原买入请求与真实结果；全部目标失败为 Unavailable。已构造成功的成熟 sink 交给同一 provider 持有，关闭会话时统一释放。

内部薄 formatter 将每个事件输出为一条物理行：固定头、中文摘要、具名属性结构。字符串引号、反斜杠、CR/LF/TAB、控制字符可见转义，中文保持可读；null、0、缺失分别保留原含义。日期只投影 Year/Month/Day。摘要最多 256 个 Unicode 标量，普通字符串 512 个；异常最多 64 行/16 KiB。实际截断才标注 `Truncated/TruncatedFields` 并计数原文本，`TruncatedOriginalCounts` 按字段路径给出原数量：普通字符串/摘要仅 `ScalarCount`（Unicode 标量数），Exception 另给 `Utf8Bytes`（转义前 UTF-8 字节数）和 `LineCount`（原文本 LF 数量加 1，CRLF 只计一次）。三个计数为 long，不把 UTF16 长度当标量数，不把日志转义后的物理行数当原异常行数，也不保存原文本副本。整事件连同这些元数据的 UTF-8 输出超过 32 KiB 时，仍改为同一序号的 `EventPayloadRejected` 与原事件名、可取得关联键，不输出貌似完整的原异常或结算。

`Health` 返回已观察的 `Healthy/Degraded/Unavailable`、首次故障运行毫秒和故障次数；`KnownLostCount=null`，不假装已知丢失量。各目标拥有独立 `ILoggingFailureListener`，不监听全局 `SelfLog`：文件目标的薄 Adapter 在级别过滤前直接向原始成熟 File sink 绑定监听，同时捕获同步 Emit 抛出异常；内存目标使用 Serilog 4.3.0 的 Fallible。不能把被过滤包装隐藏的内部故障当作一次成功输出，文件共享、轮转、保留和关闭仍由成熟 File 负责。写盘/投影故障首先交给独立 `diagnostic`（默认 stderr，Main 接 Godot 警告）：本次故障首次立即通知，随后现实 60 秒最多再通知一次；恢复可通知一次。每次已观察故障仍完整累计 FailureCount，通知抑制不是日志丢失计数。诊断点自身异常也隔离，不进入业务 logger。仅健康状态变化或恢复时补 `LoggingHealthChanged`；全部目标稳定不可用时不反复尝试额外健康记录。后续真实成功输出观察到恢复，不重放失败记录。缺少健康事件不能证明采集完整。

## 验证入口与边界

`TestLogging` 通过公开 Interface 验证真实初始化、两个局同号命令、原始非法输入、成功/拒绝资金库存、实际单价事实（负数/零/容量提前拒绝缺失，资金不足/成功保留执行价）、合法来源与非法来源提交前异常、公开观察的真实完成/原异常关联与一次终结、幂等释放、关闭采集的经营等价、连续故障完整计数与独立通知有界、恢复、独立诊断隔离、中文/null/0/缺失/转义、Unicode 及异常预算、截断原数量（普通中文/emoji 标量、异常行数/中文字节/emoji 三计数及完整字符片段）、核心超限拒绝，以及真实 provider 无传输伪字段、空版本/无窗口为 null、正尺寸保留。`TestLoggingFiles` 使用真实 File 验证共享目录两宿主和并行提交、两目标相同事件、单会话物理序号、正常文件释放、小阈值滚动与跨关闭重开的保留、发布 profile 不建 debug、实际普通文件占据目录的初始化故障、运行中滚动目标失效及后续内部通知、仍可写目标输出健康变化，以及 debug/runtime 任一配置失败的独立存活、双失败、真实结算、持续健康和关闭释放；样例保存在 `build/test-results/logging/`，不纳入 Git。以上是测试用例范围，执行结果以本轮统一验收记录为准。

通用 seam 另通过独立合法测试描述验证新事件、级别、来源、嵌套字段、公共头不可覆盖与投影故障隔离；真实 File 核对 runtime 资格和级别过滤、发布过滤前不分配序号、双目标共享同一事件以及过滤目标不被误判恢复。测试事件仅存在于测试代码，不登记为玩法事件。

`TestTradeLogging` 验证固定卖出、空库存与冻结下全售、批量七条明细与合计、容量拒绝实际价、来源、关闭日志等价以及集合/文本截断合并；真实 OpenFile 样例覆盖买卖、全售、订单等待至成交/取消、公告与报价，并检查双目标同一事件。`TestOrderLogging` 验证订单资源、短路覆盖、有界合并、尾段、跨商品编辑、大配置与跨局隔离；`TestMarketLogging` 核对实际公告和报价因素、随机序列等价及初始化回放不补造。领域采样的 ItemCount 和 formatter 的文本计数按路径合并，根 Truncated、TruncatedFields、TruncatedOriginalCounts 各出现一次，整事件仍受原尺寸预算。

上述测试注册到 `TestSuite`。跨滚动文件读取按日期和编号的 Ordinal 文件名顺序，不重新排序事件掩盖回退。单商品正常业务仍没有可由合法公开调用构造的抛出分支；不增加异常注入开关。公开 TradeLogOperation 的异常测试保留原异常引用及关联，实际经营 catch/throw 同时由代码审查核对。系统时钟回拨、真正跨进程同时启动、磁盘耗尽、强杀和断电需另做环境验收。受控详细诊断和重放执行器未实现。
