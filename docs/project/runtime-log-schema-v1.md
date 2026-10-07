# 运行时日志 schema v1（设计草案）

关联 [#82](https://github.com/Fotally/Farm-Exchange/issues/82)；记录范围、框架及轮转方案见[日志设计](../research/runtime-logging.md)。2026-10-02 用户确认：两类业务日志均输出可读 `.log`，字段含义必须明确，供人和 AI 分析。本文件是字段与事件语义的唯一维护入口；目前没有日志代码，下面是接入时要履行的设计约定，不是已有日志的格式。

## 版本、文件与分析入口

- 业务日志每个事件携带 `SchemaVersion=1`。runtime 与 debug 使用同一 schema；详细程度不同，不改变同名字段的含义。
- Windows 发布包位置为 exe 同级 `logs/runtime/*.log`；开发构建另写 `logs/debug/*.log`。后续发布时附带本文件的同版本副本 `logs/schema-v1.md`，让 AI 不依赖仓库也能读懂字段。
- 当前为草案，可在第一次实际交付前修订。交付后修改既有字段含义、单位或事件记录时点要升版本，并保留旧版说明，不能用新含义解释旧文件；新增字段和事件也要先登记对应版本文档。
- Godot 原生 `godot.log` 不属于这套业务 schema；其阅读说明见文末，不往原生日志虚构 `SchemaVersion` 或业务字段。

AI 分析时先读本文件，再确认日志版本、会话和事件名。`Message` 是中文摘要；数值、判断和关联关系以具名字段为准。字段顺序不表达业务先后。

## 文本布局

建议使用 Serilog 自带文本输出模板和 invariant culture，不新增序列化器。事件头包含时间、级别、schema、会话、序号、运行毫秒、类别和事件名；后接中文消息与属性集合。框架使用的模板为：

```text
{Timestamp:yyyy-MM-ddTHH:mm:ss.fffzzz} [{Level:u3}] SchemaVersion={SchemaVersion} SessionId={SessionId} Sequence={Sequence} UptimeMs={UptimeMs} SourceContext={SourceContext} EventName={EventName} {Message:l} | {Properties}{NewLine}{Exception}
```

`Timestamp` 是带显式时区偏移的现实时间，不能省略偏移。`Properties` 是 Serilog 的具名属性集合，呈现为 `{ Name: Value, ... }`，不是 JSONL；属性名称不带 JSON 双引号。实现应保留类型和结构，不对对象只调用 `ToString()`。字段中不使用千位分隔符；数值小数点为 `.`。

Serilog 的 `Properties` 会省略已经出现在输出模板或消息模板中的属性。因此中文消息只作摘要，业务变量留在属性集合中，避免一部分数值只出现在句子里而找不到字段名。[文本 formatter 源码](https://github.com/serilog/serilog/blob/dev/src/Serilog/Formatting/Display/MessageTemplateTextFormatter.cs)、[属性输出源码](https://github.com/serilog/serilog/blob/dev/src/Serilog/Formatting/Display/PropertiesOutputFormat.cs)

普通事件通常一行；异常输出及包含换行的字符串可续行。只有符合上述完整事件头的行开启新事件；其他续行归属上一事件，不能把一条堆栈的多行当作多个错误。多进程共享文件的完整事件写入须在接入时验证；截取日志应包含完整事件头及其续行。

## 通用字段

下表全部是每个业务事件必填字段；日志头的位置也属于字段定义。

| 字段 | 类型 | 含义与约束 |
| --- | --- | --- |
| `Timestamp` | 带偏移的 ISO 8601 日期时间 | 事件记录时的电脑现实时间，不是游戏日期；系统校时可能使它回退 |
| `Level` | 级别枚举 | 头部缩写 `VRB/DBG/INF/WRN/ERR/FTL`；对应 MEL 的 Trace/Debug/Information/Warning/Error/Critical，Serilog 对 Trace/Critical 使用 Verbose/Fatal |
| `SchemaVersion` | 正整数 | 本文件定义的语义版本；当前草案为 1 |
| `SessionId` | 32 位十六进制字符串 | 本次进程日志初始化时生成的会话标识，不是用户 ID 或存档 ID；重启生成新值 |
| `Sequence` | 非负 int64 | 同一会话中每个逻辑日志事件的递增序号，从 1 起；分发到两个文件前分配，同一事件在两文件中的序号相同 |
| `UptimeMs` | 非负 int64，毫秒 | 从日志初始化起的单调计时；暂停游戏仍增长，不按游戏倍率换算 |
| `SourceContext` | 非空字符串 | `ILogger` 的模块类别，如 `FarmExchange.Trading.TradeOrderBook`；不是文件路径或业务对象 ID |
| `EventName` | 本文登记的字符串 | 稳定事件名，确定该事件的字段与记录时点；不要根据中文消息猜测事件类型 |
| `Message` | 中文字符串 | 给人的摘要，不能作为解析数值、识别成功或失败的唯一依据 |

涉及经营状态的事件还必须带以下完整上下文；`SessionStarted` 等游戏尚未创建的事件可全部省略，不能写 0 冒充已创建。

| 字段 | 类型/单位 | 含义 |
| --- | --- | --- |
| `SimulationSeconds` | uint32，经营秒 | 当前日历累计经营秒；暂停不推进，允许暂停时的主动命令共享同一个值 |
| `GameDate` | `{ Year, Month, Day }` | 当前经营日期，三项均为整数：年从 1 起，月 1～12，日 1～28；不是现实年月日 |
| `Season` | 季节字符串 | `Spring/Summer/Autumn/Winter`，分别为春/夏/秋/冬 |
| `IsPaused` | 布尔 | 记录时经营暂停状态，不表示程序或日志线程暂停 |
| `Phase` | 字符串枚举 | `Initialization/Command/Harvest/Processing/RawClaim/Workers/Calendar/Orders/Presentation/Shutdown`；说明记录所在阶段 |

同一个 tick 内日历在换日阶段推进；早期阶段可能读到推进前的经营秒，订单阶段读到推进后的经营秒。以 `Sequence` 和 `Phase` 判断顺序，不把相同秒值的多个事件视为重复。日期换算入口为 [GameCalendar](../../scripts/time/GameCalendar.cs)，比例见[经营与生产时间](../architecture/time/game-time-units/interface-game-time-units.md)。

本 schema 的日期对象是日志专用投影：记录前只取 `Year/Month/Day` 三个成员。源码 `GameDate` 还含 `ElapsedDays/Season`，不得直接结构化输出完整源码对象并带出未登记成员；`GameDate`、`QuoteDate`、`NextQuoteDate`、`PublishedDate` 均采用同一三字段投影。保留结构指保留这里定义的日志结构，不表示直接序列化业务对象的全部成员。

## 数值、标识、缺省与状态

- 字段缺失表示该事件不适用；`null` 表示该字段适用但没有对象/值，如空闲工人的目标。0 表示真实为零，不能替代未知值。事件必填字段缺失表示记录不完整，AI 应明确指出，不能补造数值。
- 所有 `*Cents` 均为金币整数分，100 分＝1.00 金币；不是浮点元。余额/库存按实际整数容量记录，计算货值和手续费可使用 int64。数量单位为“份”；建筑计数单位为“座”，基础格不是一座建筑。
- `Commodity` 为 `<Crop>.<Kind>`，Kind 为 `Raw/Product`，分别是原料/加工品。Crop 为 `Wheat/Corn/Rice/Potato/Sunflower/Sugarcane/Radish`，分别为小麦/玉米/水稻/马铃薯/向日葵/甘蔗/萝卜；加工品名称从[作物定义](../../scripts/farming/CropCatalog.cs)读取。
- `Cell` 为 `{ X, Y }` 整数对象：玩家输入的基础格；`Anchor` 同形，指解析出的整座设施左上锚点。合法范围为 0～383；被拒绝的越界请求可以原样记录越界输入。不得把子格当作独立农田或增加日志专用永久建筑 ID。
- `WorkerNumber` 使用现有快照编号，从 1 起，目前 1～3；`OrderId` 使用订单模块已有的正整数 ID，仅在本局中有意义。跨会话不能只凭相同编号认定同一对象。
- 枚举输出名字而不是序号；非法请求的原始参数可记录原数值并明确 `Outcome="Rejected"`，不能把它解释为合法枚举。每个 Debug 状态字段必须指明来自哪种状态枚举。

## 命令、交易与订单字段

同名字段在所有事件中保持下述含义；事件组合见后面的登记表。

| 字段 | 类型/单位 | 含义 |
| --- | --- | --- |
| `Outcome` | `Success/Rejected` | 该事件所述命令成功/正常业务拒绝；RuleChecked 中仅表示检查通过/拒绝，尚未证明提交。异常用独立异常事件，不伪装为成功；成功可能是零数量操作 |
| `FailureCode` | 可选枚举字符串 | 有枚举结果时原样记录原模块原因（如 `TradeFailure.InsufficientFunds`、`LandFailure.Occupied`）；成功为相应 `None`，没有现成枚举的命令省略，不另编字符串到枚举的映射 |
| `RejectionReason` | 字符串或 null | 正常拒绝的真实中文原因；成功为 null；它不是稳定状态键，也不是异常堆栈 |
| `Commodity`、`Crop` | 商品/作物标识 | 本事件操作的商品或所选作物，取值见上文；道路没有 Crop |
| `BuildingKind` | `Farm/Processor/Road` | 农田、加工场地、道路 |
| `Cell`、`Anchor` | 格坐标对象 | 输入子格与解析后的整座锚点；解析失败时 Anchor 为 null |
| `PreviousCrop` | 作物标识 | 改种前选种；不是自动认定已播下的作物 |
| `ChargedCents` | 非负 int64，分 | 此次建造实际扣费；失败或拆除为 0，不表示目录预览价格 |
| `PreviousReserveQuantity`、`ReserveQuantity` | 非负 int32，份 | 修改前/后的原料加工保留底线，不是冻结库存或订单目标 |
| `Side` | `Buy/Sell` | 买入/卖出；不是金币变化的正负号 |
| `RequestMode` | `Fixed/AllCommodity/AllProducts` | 请求固定份数/某商品可用量全部/全部加工品；不是订单的 QuantityMode |
| `RequestedQuantity` | 可选 int32，份 | Fixed 请求的原输入，非法负数原样保留；全部出售请求省略，不用 -1 表示全部 |
| `Quantity` | 非负 int64，份 | 本次实际成交量；失败为 0；不等于请求量、目标量或冻结量 |
| `UnitPriceCents` | 非负 int32，分/份 | 实际结算使用的报价；未取得有效报价的拒绝事件省略；不是显示时缓存报价或订单限价 |
| `ValueCents` | 非负 int64，分 | 商品货值，不含手续费；单商品成功时＝Quantity × UnitPriceCents |
| `FeeCents` | 非负 int64，分 | 实际收取手续费；即时为 0，委托为货值 1% 向上取分，不按请求预算计算 |
| `MoneyBeforeCents`、`MoneyAfterCents` | 非负 int32，分 | 这次提交前/后的钱包总余额，包含冻结金额 |
| `AvailableMoneyBeforeCents`、`AvailableMoneyAfterCents` | 非负 int32，分 | 总余额减总冻结后可用金额；冻结释放可能改变它而不改变总余额 |
| `FrozenMoneyBeforeCents`、`FrozenMoneyAfterCents` | 非负 int32，分 | 钱包被所有一次买单冻结的总额，不是本单额 |
| `StockBefore`、`StockAfter` | 非负 int32，份 | 当前商品库存总量，包含冻结卖单货量，不含已投入加工的原料 |
| `AvailableStockBefore`、`AvailableStockAfter` | 非负 int32，份 | 当前商品总库存减卖单冻结量；不减加工保留底线 |
| `FrozenStockBefore`、`FrozenStockAfter` | 非负 int32，份 | 当前商品被所有一次卖单冻结的数量 |
| `TradeLines` | 单商品结算对象数组 | AllProductsSold 的逐商品明细；每项含 Commodity、Quantity、UnitPriceCents、ValueCents、FeeCents 及该商品库存前后六字段，含义沿用上表；无可卖量的商品仍写 Quantity=0，七加工品按目录顺序排列 |
| `OrderId` | 正整数 | 已有订单 ID，创建失败而未分配 ID 时省略 |
| `OrderStatusBefore`、`OrderStatusAfter` | 订单状态字符串 | `Waiting/Disabled/Completed/Cancelled`＝等待/停用/完成/撤销；创建前状态为 null |
| `OrderFrozenCentsBefore`、`OrderFrozenCentsAfter` | 非负 int32，分 | 本单拥有的资金冻结，不是所有订单冻结总额 |
| `OrderFrozenQuantityBefore`、`OrderFrozenQuantityAfter` | 非负 int32，份 | 本单拥有的库存冻结，不是全部商品的冻结量 |
| `RequestedOrderRequest` | 订单配置对象 | 创建/编辑命令收到的完整原始配置，可能非法或被拒绝，不能当作已经生效；形状见下一表 |
| `OrderRequestBefore`、`OrderRequestAfter` | 订单配置对象或 null | 真正提交前/后的有效单据配置；创建的 Before 为 null；成功才必填，拒绝时若记录合法已有订单则前后相同，不用拒绝的输入冒充 After |
| `CashBasisCents`、`ReserveCents` | 非负 int32，分 | 原建单现金基准、实际现金保留门槛；同 ID 编辑不重置原基准 |
| `LockedQuantity` | 非负 int32，份 | 一次单建单时锁定的成交目标量；不是持续策略本次动态差额 |
| `ConditionSatisfied` | 布尔 | 本次订单判断是否满足全部成交前提；为 false 时不代表程序错误 |
| `WaitingCategory` | 稳定字符串集合 | 从 `Price/Stock/Season/LimitPrice/TargetReached/BudgetInsufficient` 及实际 `TradeFailure` 名称选取当前等待类别；按名称排序去重比较，不包含实时数值；空集合表示没有等待原因 |

`FailureCode` 使用完整枚举类型前缀和成员名，取值释义如下；`WaitingCategory` 引用交易拒绝原因时也使用 `TradeFailure.` 前缀且不包含 None：

- `TradeFailure.None`＝成功；`InvalidCommodity`＝非法商品；`InvalidQuantity`＝数量非法；`InsufficientFunds`＝可用/本单可支配资金不足；`InsufficientStock`＝可用/本单可支配库存不足；`InventoryCapacityExceeded`＝入库超容量；`WalletCapacityExceeded`＝收入超钱包容量；`CashReserveNotMet`＝成交后现金低于保留线。后七项均以 `TradeFailure.` 为前缀。
- `LandFailure.None`＝成功；`InvalidBuilding`＝非法建筑；`InvalidCrop`＝非法作物；`OutOfBounds`＝占地超地图；`Occupied`＝占地被占用；`InsufficientFunds`＝建造可用资金不足。后五项均以 `LandFailure.` 为前缀。
- `RawReserveFailure.None`＝底线设置成功；`RawReserveFailure.InvalidCrop`＝非法作物；`RawReserveFailure.InvalidQuantity`＝请求底线为负数。

订单配置的字段沿用 [TradeOrderRequest](../../scripts/trading/TradeOrderRequest.cs)，不要只写“预算”“数量”而丢失模式：

| 配置字段 | 类型/取值 | 含义 |
| --- | --- | --- |
| `Commodity`、`Side` | 上表标识 | 交易对象与方向 |
| `Frequency` | `Once/Continuous` | 一次单/持续策略，持续策略不冻结资源 |
| `QuantityMode` | `Fixed/BuyToTarget/SellToTarget` | 固定量/买到库存目标/卖到库存目标 |
| `Quantity` | int32，份 | Fixed 时是请求量；两种 ToTarget 时是库存目标，不能解释为成交量 |
| `BudgetMode` | `None/FixedBudget/LimitPrice` | 无买入预算模式/固定总预算/最高买入限价 |
| `BudgetCents` | int32，分 | 固定预算上限包含手续费；仅 FixedBudget 生效 |
| `LimitPriceCents` | int32，分/份 | 最高买入价；仅 LimitPrice 生效 |
| `ReserveMode` | `Amount/Percent` | 固定保留金额/原现金基准百分比 |
| `ReserveValue` | int32 | Amount 时单位分；Percent 时单位整数百分比（50 表示 50%）；须与模式一起解释 |
| `ConditionGroups` | 条件二维数组 | 组内全部满足（AND），组间任一满足（OR）；按原顺序记录；每项含 Factor、Comparison、Value |
| `Factor` | `Price/Stock/Season` | 报价/商品总库存/季节；Stock 不是可用库存 |
| `Comparison` | `Less/LessOrEqual/Equal/GreaterOrEqual/Greater` | `< / <= / == / >= / >` |
| `Value` | int32 | Price 单位分/份，Stock 单位份，Season 使用原配置编码 0/1/2/3＝春/夏/秋/冬；不是季节名称的文本比较 |

模式不适用的配置字段仍原样记录完整配置，但 AI 不应把其值当作生效限制。拒绝的配置允许带不合法输入；其合法性由真实结果说明。

## 汇总、行情与诊断字段

| 字段 | 类型/单位 | 含义 |
| --- | --- | --- |
| `IntervalStartUptimeMs`、`IntervalEndUptimeMs` | 非负 int64，毫秒 | 本次聚合窗口的单调现实时间边界，事件归属 `(start,end]`；各窗口不重叠，退出尾段也必须标边界 |
| `SimulationSecondsStart`、`SimulationSecondsEnd` | uint32，经营秒 | 窗口起止日历值，暂停窗口可能相同，不是所有事件记录时间之和 |
| `HarvestedByCrop`、`ProducedByCrop` | 七作物→非负 int64 字典，份 | 窗口内实际收获原料/完成加工入库的量；包括明确为 0 的品种，不能据此推断库存净增加 |
| `ClearedByCrop` | 七作物→非负 int64 字典，轮 | 窗口内越季清理的作物轮次数；一座农田本轮清理计 1，不是损失份数 |
| `Inventory` | 十四商品→库存对象字典 | 窗口结束时库存，每项为 `Total/Available/Frozen`，均非负 int32 份；不是该窗口累计入库量 |
| `MoneyCents`、`AvailableMoneyCents`、`FrozenMoneyCents` | 非负 int32，分 | 窗口结束时钱包总额、可用额、总冻结额 |
| `TickCount` | 非负 int64，次 | 窗口内实际完成的未暂停经营 tick 数；不包括暂停时无操作的计时器回调 |
| `FrameCount` | 非负 int64，帧 | 窗口内用于帧间隔统计的有效采样数，不是游戏日历推进次数 |
| `TickAverageMs`、`TickMaxMs` | 非负小数，毫秒 | TickCount 个 tick 的平均/最大耗时，0 个 tick 时为 null |
| `FrameAverageMs`、`FrameMaxMs` | 非负小数，毫秒 | FrameCount 个有效帧间隔的平均/最大值，无有效样本时为 null；不是 FPS 或 P95 |
| `FacilityCount` | 非负整数，座 | 窗口结束时农田与加工场地合计，不含道路或占用子格 |
| `PreviousPriceCents`、`PriceCents` | int32，分/份 | 此商品正式更新前/后的报价；不是公告当日提前执行的价格 |
| `QuoteDate`、`NextQuoteDate`、`PublishedDate` | 游戏日期对象 | 实际报价日/下一实际报价日/公告出现日；每项使用 Year/Month/Day，不是文件名日期 |
| `NewsLines` | 中文字符串数组 | 实际公开的公告内容，不能当作已经完成的行情结算 |
| `SupplyFactor`、`DemandFactor` | -2～2 整数 | 本次报价公式抽到的供需离散因子，不是玩家买卖数量 |
| `PriceFactors` | 行情因素对象 | 正式报价变更的简要因素，包含 SupplyFactor、DemandFactor、SupplyDemandPercent、EventPercent；原料额外带 SeasonPercent，加工品额外带 RawReferencePriceCents。因子含义见本表，百分比贡献含义见计算成员表；RawReferencePriceCents 为本轮对应原料的新报价，单位分/份。两类日志均记录，不包含计算分子/分母等 Debug 细节 |
| `CalculationInputs` | 下表定义的计算对象 | 当前商品本轮报价的公式输入、中间值与限幅结果，不是玩家成交价；对照[报价公式](../gameplay/trading/market-quotes.md)，不能直接放匿名数值数组 |
| `ExceptionType` | 完整类型名字符串 | 捕获到的 C# 异常类型；不同于 FailureCode |
| `Exception` | 原异常多行文本 | 异常消息、内部异常和堆栈，由框架输出；缺少源码行号不代表没有异常 |
| `GameVersion`、`BuildId`、`EngineVersion`、`DotnetVersion` | 字符串 | 游戏版本、可追溯构建标识、Godot 引擎版本、实际 .NET 运行时版本；未知版本明确为 null |
| `BuildKind` | `Debug/Release` | 导出/开发构建类型，不由日志级别倒推 |
| `OSDescription`、`Renderer` | 字符串 | 操作系统描述、实际渲染方式；不是业务规则 |
| `WindowSize` | `{ Width, Height }` 正整数像素 | 实际窗口/视口对应的显示尺寸，采集时明确使用 Godot 窗口尺寸，不当作世界坐标 |
| `Seed` | int32 | 初始化实际使用的种子，供定位开局和行情；不承诺凭日志重放整局 |
| `InitialFacilities` | 设施对象数组 | 每项 `Anchor/BuildingKind/Crop`，实际开局布局，不是所有占用子格 |

`CalculationInputs` 的成员定义如下；原料和加工品只携带适用的项，百分比贡献是整数百分点，10 表示 +10 个百分点，不是倍率 10：

| 成员 | 类型/单位 | 含义与出现条件 |
| --- | --- | --- |
| `Formula` | `RawFormula/ProductFormula` | 原料/加工品报价分支，必填 |
| `InitialPriceCents`、`OldPriceCents` | 正整数，分/份 | 商品目录初价、本次计算前正式报价，必填 |
| `SupplyDemandPercent`、`EventPercent` | 有符号整数，百分点 | 本次供需合并贡献、外部事件贡献，必填，不代表最终涨跌百分比 |
| `SeasonPercent` | 有符号整数，百分点 | 原料适宜季节贡献，仅 RawFormula |
| `RawInitialPriceCents`、`PendingRawPriceCents` | 正整数，分/份 | 对应原料的目录初价、本轮已算出的待发布价，仅 ProductFormula；不是当前显示的旧原料价 |
| `Numerator`、`Denominator` | int64 整数；分母为正 | 传入 RoundAndClamp 的真实整数分子/分母，含公式倍率；分子不能直接按金币分解释 |
| `RoundedPriceCents` | 整数，分/份 | 对 Numerator/Denominator 按现有公式四舍五入后的值，限幅前 |
| `MinimumPriceCents`、`MaximumPriceCents` | 正整数，分/份 | 本轮按初价及旧价算出的最终上下限 |
| `FinalPriceCents` | 正整数，分/份 | 限幅后的待发布价；正式生效仍以 QuoteUpdated 为准 |

## Debug 专用字段

Debug 的上下文、资金和库存字段仍遵循上面的定义。尚未逐项定义的计算数据不得直接加入日志；应先补字段字典和对应事件条件，避免用任意 `Details` 对象隐藏含义。

| 字段 | 类型/单位 | 含义 |
| --- | --- | --- |
| `RuleKind` | `Placement/Planting/Trade` | 所执行的规则检查类别 |
| `CheckResults` | 字符串→布尔字典 | 稳定检查项是否通过；Placement 为 Building/Crop/Bounds/Occupancy/Funds，Planting 为 Season/Time，Trade 为 Commodity/Quantity/Funds/Stock/Capacity/Reserve；未执行项省略，不写 false 冒充检查失败 |
| `ProductionKind` | `Farm/Processor` | 状态来自农田还是加工场地 |
| `StateBefore`、`StateAfter` | 状态字符串 | Farm 使用 CropStage 的 `None/Seeded/Growing`；Processor 使用详情状态 `WaitingForRaw/Processing/WaitingForReserve/ReadyToProcess`，分别为待原料/加工中/因底线等待/具备领取条件 |
| `HasWaterBefore`、`HasWaterAfter` | 布尔 | 农田本轮供水状态；加工场地省略 |
| `Transition` | 字符串枚举 | `Sown/Watered/GrowthStarted/Harvested/Cleared/RawClaimed/ProcessingStarted/Processed`；Harvested/Processed 加 Quantity（实际产量），RawClaimed 加 Quantity=1（领取量），Cleared 不写虚构产量 |
| `RemainingSeconds` | 非负整数，经营秒 | 当前状态剩余经营秒，按现有比例换算向上取整；不是现实秒或内部时间单位 |
| `WorkerNumber` | 正整数 | 现有工人编号，见上文 |
| `ActivityBefore`、`ActivityAfter` | 工人活动枚举 | `Idle/Moving/Sowing/Watering`＝空闲/移动/播种/浇水；不是任务成功/失败结果 |
| `TargetAnchorBefore`、`TargetAnchorAfter` | 锚点或 null | 认领的农田实例锚点，不是实际工作中心格；输入 WorkerSnapshot.TargetCell 须先按实际含义核对转换 |
| `PositionBefore`、`PositionAfter` | `{ X, Y }` 小数对象，基础格单位 | 工人经营快照格位置，不是屏幕像素、地图本地像素或视觉插值位置 |
| `Conditions` | 评估对象二维数组 | 对应原 ConditionGroups，每项 Factor/Comparison/Value 含义同配置，额外 `Actual` 是本次实际数值（同 Value 单位）、`Satisfied` 是布尔；按原组/项顺序对应 |
| `ComputedQuantity` | 非负整数，份 | 本次评估得到的候选成交量，不代表已经成交 |
| `ComputedBudgetCents` | 非负 int64，分 | 本次可用于买入并支付手续费的候选预算，不代表已扣费 |
| `TickStartSimulationSeconds` | uint32，经营秒 | 被计时的 tick 开始时经营秒 |
| `StageDurationsMs` | 阶段→非负小数字典，毫秒 | 对应实际 Harvest/Processing/RawClaim/Workers/Calendar/Orders 阶段耗时；未测阶段省略，不和未覆盖的 tick 总耗时强行相等 |
| `ViewportSize` | `{ Width, Height }` 正整数像素 | 地图画布视口尺寸，区别于世界范围和 WindowSize |
| `PlayerZoom` | 正小数，无量纲 | 镜头模块持有的玩家倍率，不是抵消画布拉伸后的内部 Camera2D.Zoom |
| `CameraCenter` | `{ X, Y }` 小数对象，地图本地像素 | 变化后的镜头中心，不是基础格坐标 |
| `RebuiltChunkCount` | 非负整数，块 | 本次外观同步重建的 8×8 基础格缓存块数，不是设施数 |

## 事件登记与字段组合

每项包含通用头；有游戏实例时包含完整经营上下文。下面的“资金前后”指 Money/AvailableMoney/FrozenMoney 三组 Before/After，“库存前后”指 Stock/AvailableStock/FrozenStock 三组 Before/After，“订单前后”指 OrderStatus/OrderFrozenCents/OrderFrozenQuantity 三组 Before/After。这些缩写只用于文档，不作为日志字段名。

| EventName | 文件/级别 | 记录时点与必填业务字段 |
| --- | --- | --- |
| `SessionStarted` | 两类/INF | 日志初始化后；GameVersion、BuildId、EngineVersion、DotnetVersion、BuildKind、OSDescription、Renderer、WindowSize；初始化时尚未取得的环境值明确 null，不伪造 |
| `SessionEnded` | 两类/INF | 正常关闭前；经营上下文（已有游戏时），运行时长由 UptimeMs 表达 |
| `GameInitialized` | 两类/INF | 初始化完成；Seed、InitialFacilities、MoneyCents |
| `BuildingPlaced`、`BuildingRemoved` | 两类/INF | 实际命令完成；Outcome、RejectionReason、Cell、Anchor、BuildingKind、ChargedCents、资金前后；生产建筑加 Crop，失败无法解析的建筑信息可 null；有枚举时加 FailureCode |
| `CropChanged` | 两类/INF | 实际改种命令完成；Outcome、RejectionReason、Cell、Anchor、PreviousCrop、Crop，无法解析时相关值 null |
| `RawReserveChanged` | 两类/INF | 底线命令完成；Outcome、FailureCode、RejectionReason、Crop、PreviousReserveQuantity、ReserveQuantity；成功 FailureCode=RawReserveFailure.None，拒绝使用该枚举实际原因；无效输入须另记录 RequestedQuantity，未取得合法品种时前后值 null |
| `PauseChanged` | 两类/INF | 暂停状态真正变化后；IsPaused 为新值，不为 UI 刷新重复写 |
| `TradeFinished` | 两类/INF | 单商品主动交易完成；Outcome、FailureCode、RejectionReason、Commodity、Side、RequestMode、Quantity、ValueCents、FeeCents、资金前后及库存前后；Fixed 加 RequestedQuantity；合法报价已取得时加 UnitPriceCents；非法商品时库存前后为 null |
| `AllProductsSold` | 两类/INF | 一次全部加工品出售完成；Outcome、FailureCode、RejectionReason、RequestMode=AllProducts、Quantity、ValueCents、FeeCents=0、资金前后、TradeLines；Quantity/ValueCents 为各行实际成交的合计，拒绝时各行实际量/货值为 0，不把合计货值除以合计数量假装单价 |
| `OrderCreated`、`OrderEdited` | 两类/INF | 命令完成；Outcome、RejectionReason、RequestedOrderRequest（请求原值）；成功加 OrderId、OrderRequestBefore、OrderRequestAfter、订单前后、资金/商品库存前后、CashBasisCents、ReserveCents、LockedQuantity；创建拒绝没有已分配 ID 时省略，不把失败的编辑输入当作生效配置 |
| `OrderCancelled`、`OrderEnabledChanged` | 两类/INF | 命令完成；Outcome、RejectionReason、OrderId；有合法订单时加订单前后、资金与库存前后 |
| `OrderFilled` | 两类/INF | 完成结算、冻结差额释放、订单状态更新后；OrderId、Commodity、Side、Quantity、UnitPriceCents、ValueCents、FeeCents、资金/库存前后、订单前后；仅实际成交，不能用本事件表达等待 |
| `QuoteUpdated` | 两类/INF | 商品正式报价更新后；Commodity、PreviousPriceCents、PriceCents、QuoteDate、NextQuoteDate、PriceFactors；因素须对应这次实际生效的计算，不读取下一轮或重新抽取随机数 |
| `NewsPublished` | 两类/INF | 公告实际公开后；PublishedDate、QuoteDate、NewsLines |
| `ProductionSummary` | 两类/INF | 聚合窗口结束/正常退出尾段；窗口起止现实及经营时间、HarvestedByCrop、ProducedByCrop、ClearedByCrop、Inventory、MoneyCents、AvailableMoneyCents、FrozenMoneyCents |
| `PerformanceSummary` | 两类/INF | 性能窗口结束/退出尾段；窗口起止时间、TickCount、FrameCount、TickAverageMs、TickMaxMs、FrameAverageMs、FrameMaxMs、FacilityCount |
| `BusinessException`、`FatalException` | 两类/ERR 或 FTL | 真正异常的接收边界；ExceptionType、Exception，已有命令/订单/设施标识加相应字段；前者 ERR，确实不能初始化/继续的后者 FTL |
| `RuleChecked` | debug/DBG | 实际规则检查完成；RuleKind、CheckResults、Outcome、相关 Cell/Anchor/Crop/Commodity；不等于该命令已提交成功 |
| `ProductionStateChanged` | debug/DBG | 实际状态转换后；ProductionKind、Anchor、Crop、Transition、StateBefore、StateAfter、RemainingSeconds；农田加供水前后，数量按 Transition 定义 |
| `WorkerTaskChanged` | debug/DBG | 任务认领/释放或活动状态变化；WorkerNumber、ActivityBefore、ActivityAfter、TargetAnchorBefore、TargetAnchorAfter |
| `WorkerMoved` | debug/VRB | 专项观察指定工人经营移动；WorkerNumber、PositionBefore、PositionAfter，不采集每帧插值 |
| `OrderEvaluated` | debug/DBG 或 VRB | 每未暂停 tick 的检查时点；OrderId、ConditionSatisfied、WaitingCategory；DBG 只在满足状态/稳定等待类别变化时写，选定订单 VRB 可逐次写并加 Conditions、ComputedQuantity、ComputedBudgetCents |
| `QuoteCalculated` | debug/DBG | 本轮行情计算完成；Commodity、QuoteDate、SupplyFactor、DemandFactor、CalculationInputs；QuoteDate 是这些计算面向的实际报价日，不必等于事件头当前 GameDate，最终以 QuoteUpdated 为正式提交证据 |
| `TickTiming` | debug/VRB | 专项观察的 tick 完成；TickStartSimulationSeconds、StageDurationsMs，普通运行用汇总 |
| `ViewChanged` | debug/DBG | 实际窗口/镜头/外观同步变化；按变化类型提供 WindowSize/ViewportSize/PlayerZoom/CameraCenter/RebuiltChunkCount，不为每帧刷新重复写 |

新事件及新的嵌套成员先登记含义和出现条件，再写入日志，不能以任意 Details 对象或匿名数值数组扩充。实施期间仍需按真实事件核对本草案；语义调整遵循版本规则。

## 示例与 AI 解释规则

下面是格式示意，不是实际验收记录。数值中的 5000 分＝50.00 金币，买入 5 份 × 100 分，零费支出 500 分；该商品库存从 2 变为 7：

```text
2026-10-02T20:00:00.000+08:00 [INF] SchemaVersion=1 SessionId=0123456789abcdef0123456789abcdef Sequence=42 UptimeMs=120000 SourceContext=FarmExchange.Gameplay.FarmGame EventName=TradeFinished 买入小麦原料成功 | { SimulationSeconds: 120, GameDate: { Year: 1, Month: 1, Day: 3 }, Season: "Spring", IsPaused: false, Phase: "Command", Outcome: "Success", FailureCode: "TradeFailure.None", RejectionReason: null, Commodity: "Wheat.Raw", Side: "Buy", RequestMode: "Fixed", RequestedQuantity: 5, Quantity: 5, UnitPriceCents: 100, ValueCents: 500, FeeCents: 0, MoneyBeforeCents: 5000, MoneyAfterCents: 4500, AvailableMoneyBeforeCents: 5000, AvailableMoneyAfterCents: 4500, FrozenMoneyBeforeCents: 0, FrozenMoneyAfterCents: 0, StockBefore: 2, StockAfter: 7, AvailableStockBefore: 2, AvailableStockAfter: 7, FrozenStockBefore: 0, FrozenStockAfter: 0 }
```

给 AI 日志和本 schema 时，按以下约束分析：

1. 先按 SessionId 分会话；同会话按 Sequence 排序。runtime 与 debug 同一 SessionId/Sequence 是同一事件的两份输出，不能重复计数；序号有缺口可能只是 Debug 被 runtime 过滤，不直接判定丢失。
2. 使用 EventName 和字段定义判断行为。Rejected 是正常拒绝，Waiting 是正常等待；只有异常事件或真实错误级别才作为错误证据，不把季节不适宜或钱不足当程序崩溃。
3. 成功买入余额减少 `ValueCents + FeeCents`，成功卖出余额增加 `ValueCents - FeeCents`；失败真实资源前后不变。冻结释放可改变可用量，不能把它解释为凭空获得金币/商品。
4. Summary 是该窗口累计发生量与窗口结束快照的组合，不是第二次入库；不要把 Debug 的逐次成熟记录再加到 ProductionSummary 里。库存净变化还受买卖、加工领取影响。
5. 只读日志不能证明完整运行状态；轮转、过滤、截段及异常退出会使上下文缺失。缺少 SessionEnded 仅是未观察到完整退出，不自行断定原因；种子和日志不提供完整重放保证。
6. 遇到未知 schema、未登记字段或缺少必填值，列出无法确认的含义，不根据相似命名、中文句子或别的版本猜测。引用问题时给文件、会话、序号和事件名。

## Godot 原生日志阅读说明

现有 `godot.log` 由引擎记录启动环境、stdout/stderr、错误及可能的原生崩溃输出；没有上述固定头。`Godot Engine ...` 表示引擎版本，`OpenGL ... Using Device ...` 表示图形环境，错误/警告及后续堆栈需作为同一诊断块阅读；测试输出也可能出现在同一位置，不能当作发布业务事件。[Godot 官方日志说明](https://docs.godotengine.org/en/stable/tutorials/scripting/logging.html)

将引擎日志交给 AI 时同时说明文件来源、运行构建和采集时间，不把引擎行强套到业务 schema 的金额/库存字段上。原生崩溃堆栈和 C# 异常也不相互替代。
