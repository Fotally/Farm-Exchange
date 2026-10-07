# 日志接入怎样减少参数重构带来的维护

调研日期：2026-10-07（北京时间）。关联 [issue #82](https://github.com/Fotally/Farm-Exchange/issues/82)。本记录依据官方文档、成熟开源源码及本项目当前实现，回答接入设计问题；建议尚未成为实施方案，不改变[现行日志 Interface](../architecture/logging/interface-logging.md)、同步输出或保存路径。

## 问题与结论

业务函数把大量参数逐个再传给日志，函数重构后又要逐处修改日志，确实会形成维护负担。减少这种负担的有效方向是：把日志依赖放在稳定的业务含义和真实完成结果上，把公共上下文、字段选择与输出协议集中维护。

用户进一步关心底座日志与子系统日志的联动：子系统修改记录内容时不改底座，底座修改通用输出时业务调用无感。这一目标合理，但成立条件是两层共享的提交 Interface 保持稳定；日志消费者的兼容问题需要另行处理，不包含在“业务无感”承诺内。

调研材料展示了几种可组合的做法，并不能证明全行业采用同一种设计。自动化可以省掉重复字段或开始、结束、异常的手续，但无法自动判断“这个数现在是请求数量还是实际成交数量”。维护可以集中，语义变更仍须处理；零适配不是可保证的目标。以下设计判断由所列材料推导，不是框架官方对本项目的推荐。

需要区分三种变化：

| 变化 | 例子 | 合理的日志维护范围 |
| --- | --- | --- |
| 函数实现变化，含义不变 | 买入内部换算法、拆函数、调整参数顺序 | 已选业务事实与日志字段可不变；受影响的接入映射最多集中改一处 |
| 输入表示变化，含义不变 | 商品与数量从独立参数搬进业务请求对象 | 接入点或集中投影需要识别新表示；输出 schema 无须随对象结构一起改变 |
| 业务含义变化 | 固定数量改固定预算、允许部分成交、费用规则改变 | 请求与结果含义已经变化，应更新投影、schema 及验证，不能用自动记录旧字段掩盖 |

## 底座与子系统怎样联动

官方机制已有这种职责分离的基础。MEL 的 `ILogger.Log<TState>` 接收等级、`EventId`、状态、异常与 formatter，而不要求底层为每种业务建一个方法。[.NET 8.0.0：ILogger 定义](https://github.com/dotnet/runtime/blob/v8.0.0/src/libraries/Microsoft.Extensions.Logging.Abstractions/src/ILogger.cs) 源生成日志在调用侧提供事件 ID 和模板。[Microsoft：日志源生成](https://learn.microsoft.com/en-us/dotnet/core/extensions/logging/source-generation) Serilog 的结构化模板及受控投影决定业务属性，上下文补充公共属性，再交给输出管线。[Serilog：Structured Data](https://github.com/serilog/serilog/wiki/Structured-Data)、[Serilog：Enrichment](https://github.com/serilog/serilog/wiki/Enrichment)

由这些能力推导出的候选组织如下；这是设计示意，不是已确认重构路径：

```mermaid
flowchart LR
    A[子系统真实请求与完成结果] --> B[领域记录入口\n选择事件、投影字段、确定时点]
    B --> C[稳定通用提交 Interface\n事件描述、属性、异常及上下文关联]
    C --> D[日志底座\n公共头、序号、过滤、格式、输出与故障隔离]
    D --> E[文件或其他既定输出目标]
```

这里需要两个不同的 seam。业务面对具名领域 Interface，例如“观察买入完成”，不拼协议字典；领域记录实现面对通用输出 Interface，底座不需要理解商品、库存与金额含义。领域记录实现可以仍放在 `scripts/logging/` 内，也可以与所属子系统放在同一目录；语义归属与文件目录不是一回事。不能用搬文件代替职责隔离。

| 责任 | 候选归属 | 另一层不应重复维护 |
| --- | --- | --- |
| 会话公共头、统一序号、输出时间、转义与尺寸限制、sink、健康状态 | 底座 | 子系统不分配序号，不配置轮转，不实现输出失败处理 |
| 局/命令关联与公共上下文携带 | 共用上下文/组装逻辑 | 各领域不逐条手抄公共字段；底座输出实现不直接读业务私有状态 |
| 事件名及事件 ID、领域字段选择、字段含义、真实记录时点 | 所属领域记录实现 | 底座不靠领域名称 switch 决定这些含义 |
| 资金/库存快照、成交与拒绝解释、请求与实际结果区别 | 所属领域记录实现，依赖真实业务事实 | 底座不重算交易，不猜业务检查顺序 |
| 通用级别或采集类别的过滤 | 底座执行共同规则，领域提供所需描述 | 新事件不要求往底座增添业务名称分支 |

通用提交 Interface 可以包含事件身份、严重程度、必要采集类别、具名属性与异常等元数据；候选关键是由领域提供事件描述，底座按共同约定处理。具体类型尚未选择，不能据此直接实现泛型事件总线。属性合法性、保留字段冲突、允许的数据形状等共同约束也应集中规定，而非每个子系统各猜一遍。

这样分离仍会有共享契约，但可以把双向改动限制在真正共享的部分：

| 修改 | 子系统业务 | 领域记录实现 | 底座 | 消费者/字段文档 |
| --- | --- | --- | --- | --- |
| 改文件轮转、sink 内部适配或故障通知策略，保持已确认行为 | 通常无改动 | 通常无改动 | 修改 | 视可见行为而定 |
| 增加由底座自动提供的通用头字段，提交 Interface 不变 | 无须逐条传参 | 无须逐条传参 | 修改 | 可能需要兼容处理 |
| 新增某个领域事件或字段，使用已有提交契约 | 仅实际事实供给/记录点有需要时修改 | 修改事件描述与投影 | 理想情况下不改 | 更新领域说明及消费约定 |
| 调整业务输入表示、含义不变 | 正常业务重构 | 集中适配受影响映射 | 不改 | 字段保持时可不变 |
| 成交、费用或生产语义改变 | 修改 | 修改含义与投影 | 通用契约容纳时不改 | 必须重新检查 |
| 修改两层共享的提交 Interface 或上下文生命周期 | 取决于是否向调用方传播 | 需要适配 | 需要修改 | 另行评估 |

“底座变化无感”应理解为常见内部与通用内容变化保持调用 Interface，不是底座可以任意改变 Interface、行为与时序还要求调用方不适配。同样，子系统新增输出字段不应成为修改底座代码的理由，但所需事实尚不存在时仍需在业务内提供它。

## 一手材料支持哪些做法

### 结构化模板和源生成：帮助明确字段、提前检查，不自动解耦业务

Microsoft 的 `LoggerMessage` 源生成在编译期生成日志方法，减少装箱、临时分配等成本，并提供日志用法诊断；模板名称与日志方法参数按名称匹配。它检查的是日志方法，不会从任意业务函数中推断哪些事实值得记录。[Microsoft：日志源生成](https://learn.microsoft.com/en-us/dotnet/core/extensions/logging/source-generation)

因此，把十个业务参数复制成十个源生成日志参数，仍有两份需要维护的参数清单。具名字段有利于分析，编译错误有利于发现变更，但都不能决定业务字段的意义。源生成可以作为日志内部的实现选择，不应成为要求所有业务函数维护日志模板的理由。

### 上下文与 enrichment：公共字段设置一次

Serilog 支持把属性加入上下文，使作用期间的记录自动携带属性；`LogContext` 需要配置启用，嵌套属性按作用域撤销，释放顺序有明确要求。[Serilog：Enrichment 与 LogContext](https://github.com/serilog/serilog/wiki/Enrichment#the-logcontext)

这适合会话、经营局、命令关联等公共信息，减少每次传入 `SessionId/GameInstanceId/CommandId`。它不自动提供某次交易的实际成交价。对本项目的推断是：公共字段由日志上下文拥有即可，不必要求业务遍布 scope 配置；也不必为了采用框架机制替换已经存在的显式局上下文。减少重复字段与减少 scope 生命周期知识，两者都要考虑。

### 对象复用与受控投影：减少复制，保留日志字段的决定权

Serilog 的 `@` 可以保留对象结构；官方同时提供 `Destructure.ByTransforming<T>`，在集中配置处只选部分属性，也可使用自定义解构策略。普通格式说明与采集时的结构处理是两回事。[Serilog：Structured Data](https://github.com/serilog/serilog/wiki/Structured-Data#customizing-the-stored-data)

这证明“调用方传现成对象，日志内部选择所需事实”是框架支持的做法。对本项目的推断是：复用真实的业务 request/result，可以避免调用方手抄其每个属性；集中投影仍需维护类型映射。若业务仅新增无关属性，选取已有字段的投影不必变；若记录字段改名，集中改映射；若含义改变，必须重新确认。

`ByTransforming` 在这里是受控投影能力的证据；若把所有业务类型的转换硬编码在底座初始化中，新增领域仍会牵连底座，因此候选字段投影应归所属领域记录实现或独立组装逻辑，通用输出不逐项认识业务类型。

对象作为输入并不等于整个对象成为日志协议。直接记录 `{@Result}` 与内部明确投影 `Quantity/ValueCents/UnitPriceCents` 的维护方式不同：前者让对象公开结构影响输出，后者使日志字段选择留在日志模块。也不能把任意对象包装成 `object` 或字符串字典后宣称解耦，那只是失去了编译期提示。

### middleware、decorator 或 interceptor：集中调用手续，业务事实仍要提供

成熟实例是 Serilog ASP.NET Core 请求中间件：统一观察下一处理环节、计时、读取 HTTP 状态，记录完成或异常，并保留异常继续传播。源码还为额外诊断属性设置单独收集入口。[Serilog ASP.NET Core 8.0.3：RequestLoggingMiddleware](https://github.com/serilog/serilog-aspnetcore/blob/v8.0.3/src/Serilog.AspNetCore/AspNetCore/RequestLoggingMiddleware.cs)

这是统一入口确实存在时集中处理的证据。将同一思想放进 decorator/interceptor，可以减少开始、完成与异常代码重复；但这个 HTTP 实例不能证明通用包装会知道本游戏的资金冻结、成交价或库存提交时点。方法正常返回也可能是业务拒绝，不能都标成成交成功。

本项目目前明确由业务执行交易，日志只观察。调研没有提供必须把执行回调交给日志的理由。若后续很多接入点重复手续，可比较业务层统一协调与领域观察 Interface；选择之前仍须确认执行、异常与状态采集顺序。

### 领域事件与已有提交点：记录发生的事实，保持发生时点明确

Microsoft 将领域事件描述为已经发生的领域事实，数据应不可变；事件分派在提交前还是提交后会影响事务含义。其示例是领域架构，不能直接视为本项目的日志接入要求。[Microsoft：领域事件设计与实现](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/domain-events-design-implementation)

设计推断是：如果业务已有真实完成结果或事件，日志可在那个提交点集中观察，无须从 UI 意图、函数名或动画猜测。没有事件机制时，直接使用真实返回结果也能获得这一好处。只为日志新增事件总线、持久事件库或所有权转移，会增加需要共同维护的协议，不是本轮建议。

### 自动反射所有参数、解构整个对象：省传参，但不免费

Serilog 4.3.0 的默认对象解构会枚举公开实例属性，按属性名生成字段并调用 getter；源码也处理 getter 异常，并配置字符串、集合和深度限制。[Serilog 4.3.0：PropertyValueConverter](https://github.com/serilog/serilog/blob/v4.3.0/src/Serilog/Capturing/PropertyValueConverter.cs)

据此可推断，整个对象自动解构使新增或重命名属性可能悄然改变输出；遍历与 getter 也不是零成本。对象中哪些内容适合记录、尺寸限额以及排除属性仍有人维护。AOP 即便拿到所有方法参数，也不能仅凭名称知道结算是否发生、数值单位是否已变化。这是机制局限判断，本轮未验证某个 AOP 产品，也不声称反射不能用于受控诊断。

敏感值同样不会因自动采集变得安全；Microsoft 的日志源生成说明仍要求对敏感记录使用分类与脱敏。[Microsoft：日志中的敏感信息](https://learn.microsoft.com/en-us/dotnet/core/extensions/logging/source-generation#redacting-sensitive-information-in-logs) 本项目当前是否涉及敏感资料应按实际字段判断，这不是新增一套脱敏系统的实施要求。

## 做法对照

下表是针对本项目的设计判断；能力证据见上节。

| 做法 | 减少哪种耦合 | 仍需维护 | 对当前项目的适用性 |
| --- | --- | --- | --- |
| 结构化模板、LoggerMessage | 字符串拼装与部分运行时用法错误 | 字段含义、日志方法与参数清单 | 可放日志内部；不解决逐项复制业务参数 |
| 局/命令上下文、enrichment | 公共身份和环境字段重复传递 | 上下文归属、生命周期、时点 | 已有日志上下文可承担，不必散布框架 scope |
| 复用业务请求/结果，集中投影 | 调用方逐字段复制与框架知识 | 投影对业务类型的依赖，必要语义变更 | 优先复用已有结果；请求对象须有实际业务需求 |
| middleware/decorator/interceptor | 大量重复的调用开始、结束、异常手续 | 包装覆盖范围、异常语义、领域结果解释 | 有统一执行入口时才考虑；现阶段不直接迁移 |
| 观察既有领域事件/提交点 | 多处各自猜测或重复记录结果 | 真实提交时点、事件/结果语义 | 可复用现有事实，不新增日志专用总线 |
| 反射全参数、整个对象解构 | 手工列参数数量 | 隐式字段漂移、排除规则、成本与限额 | 可用于有界临时诊断，不宜默认充当正式 schema |

## 用参数重构例子说明维护怎样收敛

以下是讨论用伪代码，并非已实施的新 Interface；省略了现有来源校验和异常观察，仅对比请求/结果的传递方式。

把结果逐字段复制，调用方必须知道日志需要的全部字段：

```csharp
// 不理想的示例：业务调用点再次拆开结果。
var result = trading.Buy(commodity, quantity);
log.BuyFinished(commodity, quantity, result.Success,
    result.Quantity, result.TotalCents, result.FeeCents,
    result.UnitPriceCents, result.Failure);
```

本项目实际已经采用更集中的方式：`Complete(result)` 接收真实 `TradeResult`，字段投影留在 `GameLog`，调用方不用拆解结果。

```csharp
// 假设重构前的业务内部入口。
var observation = log?.BeginBuy(commodity, quantity, origin);
var result = trading.Buy(commodity, quantity);
observation?.Complete(result);

// 假设重构后：内部执行增加提示对象，但买入含义不变。
var observation = log?.BeginBuy(commodity, quantity, origin);
var result = trading.Buy(commodity, quantity, executionHints);
observation?.Complete(result); // 日志不必跟着复制执行提示。
```

若独立参数后来合并成真正的业务 `BuyRequest`，接入映射仍可能改一处；不能承诺完全不改：

```csharp
// 假设业务决定引入请求对象；日志输出字段无需跟着重命名。
var observation = log?.BeginBuy(
    request.Commodity, request.Quantity, request.Origin);
var result = trading.Buy(request);
observation?.Complete(result);
```

若项目本来就在多个业务环节共享这个稳定请求，则可候选比较 `BeginBuy(request)`，由日志内部选属性。仅仅为了把三个参数缩成一个日志参数而新增请求类，不一定减少 Interface 知识；它可能只是把维护搬进另一份数据结构。固定预算买入、部分成交等语义变化则不能继续套用“固定请求数量”的投影，需要明确变化。

## 本项目当前实现的得失

本轮只读核对了 `FarmGame.Buy`、`GameLog`、`BuyLogOperation`、`TradeResult` 与 `RuntimeLog`。参考实现提交为 `8e3b2cee05e78507e6ba265f034f2741985c8bd6`；工作区中另有未提交修改，核对不等于重新验收。实现约定以[日志 Interface](../architecture/logging/interface-logging.md)和[字段 schema](../project/runtime-log-schema-v1.md)为准。

已有的有效隔离：

- `BeginBuy(CommodityId, int, CommandOrigin)` 只接原请求；命令编号、局身份、资金/库存前后观察和字段字典留在日志内部。
- `Complete(TradeResult)` 接真实结果，没有要求 `FarmGame` 再次拆出成交量、货值、费用与拒绝字段。`UnitPriceCents` 是业务实际读取的报价事实，日志不另外查询报价或根据失败码猜测。
- 统一输出、序号与会话字段在 `RuntimeLog`，不需要每个业务调用方配置 Serilog。修改文件轮转或排版不应传导到 `Buy`。

仍存在的维护面：

- `RuntimeLog.EventNumber` 当前在固定 switch 中登记 `CommandReceived/CommandFinished/TradeFinished` 等名称，未知名称抛出异常。新增领域事件要同步修改这份映射，说明当前输出路径仍持有领域事件清单，尚未完全达到“领域新增事件不改底座”的目标。字段投影已有集中收益，但不应把它说成扩展完全解耦。
- `RuntimeLog` 当前同时是公开组装入口、局绑定与输出/健康实现：`BindGame(FarmGame, seed)` 创建并持有 `GameLog`。因此整个类不能简单等同于纯底座；后续可讨论如何让输出 seam 不持有领域清单，保留方便的组装入口，而不是为追求分类把所有公开入口一次改掉。
- `FarmGame.Buy` 需要显式开始、真实执行、正常完成、异常终结并原样抛出。Interface 包含这些时序知识，不能只按方法参数少就认为已经足够深。
- `GameLog` 读取 `FarmGame` 的现有只读查询，这把调用方的字段负担移到集中投影，也形成了日志对业务查询的明确依赖；查询 Interface 改变时，这里仍需适配。
- 实际价格若不在结果中，日志无法可靠推导。把它加入结果体现必要事实供给，但也说明获取更多事实可能需要业务修改。不要为了方便日志让业务返回所有内部步骤。
- 当前只有一条买入接入，尚不足以证明把相同开始/完成手续复制到后续所有领域后维护仍然轻。自动生产汇总还要考虑频率与真实提交时点，不能直接把每个函数调用都记录为发布事件。

## 后续候选与验收思路

以下只供方案讨论，不授权重构现有代码：

1. 优先保持业务入口只提供少量领域事实和已有 request/result；字段命名、schema 映射、身份、限额和输出配置归日志模块维护。
2. 区分领域记录与底座输出职责，候选比较将事件描述交给领域记录实现、输出遵循稳定通用契约的方式。先解决当前 `EventNumber` 领域清单的扩展耦合，不能仅因都在日志目录而认为已经分层；事件 ID 的统一治理可以集中在领域事件契约处，不必散落在底座输出逻辑。
3. 把采集放在真实命令入口或提交点，先防止同一事实在 UI、协调、结算层重复接入。只有发现重复手续实际扩散时，再比较统一协调或装饰方式，不为未出现的形态增加泛型执行容器。
4. 正式字段采用受控投影；临时详细对象采集另有明确限额与范围。新业务字段不自动成为发布日志字段，字段含义变化不能靠序列化自动解决。
5. 验收关注变化是否局部化：改轮转/formatter，业务文件应不变；新增领域事件，底座输出实现应不变；改内部执行参数但业务含义不变，正式日志契约应稳定；修改业务含义，集中映射和 schema 应有明确审查；关闭或输出失败时，业务结果、判断顺序和异常传播保持原语义。

“侵入小”的衡量应包括调用方知道多少协议、维护多少配置、守多少时序，而不只统计新增多少行。深模块的目标是把这些知识集中，给调用方稳定的 Interface；并不意味着日志无需了解任何业务事实。

## 证据边界

本轮浏览并核对五类关键一手材料：Microsoft 日志机制、Serilog 上下文、Serilog 对象投影及源码、成熟请求日志中间件、Microsoft 领域事件说明。源码分别锁定 .NET 8.0.0、Serilog 4.3.0 与 Serilog ASP.NET Core 8.0.3；Microsoft 与 Wiki 链接是调研时的官方页面，未来可能更新。本轮只写研究文档，没有试装框架、AOP 性能测试、修改业务代码或运行项目编译测试。

材料可证明所列机制与成熟实现存在，不能证明其市场占比、适用于所有 Godot 工程、或能在本项目实现零侵入。具体重构、采用中间件、请求对象或新的领域事实类型仍需根据实际接入负担评估并按项目约定确认。
