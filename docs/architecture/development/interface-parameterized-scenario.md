# 参数化固定流程接口

配置字段与运行操作见[使用说明](../../project/parameterized-tests-usage.md)，固定步骤与证据范围由本文和[实现说明](implementation-buy-process-sell.md)维护。实现为 [ScenarioConfiguration](../../../scripts/development/scenarios/ScenarioConfiguration.cs)、[BuyProcessSellScenario](../../../scripts/development/scenarios/BuyProcessSellScenario.cs) 和 [ScenarioReport](../../../scripts/development/scenarios/ScenarioReport.cs)，位于开发编译目录、命名空间为 `FarmExchange.Development`，发布构建不包含这些类型。公共经营实现不依赖它们。

## 加载与启动

`ScenarioConfiguration.Load(path)` 一次读取原始UTF-8字节，计算SHA-256后严格解析。允许UTF-8 BOM，摘要仍包含BOM。未知、重复、缺失字段，非法类型、非整数数量、非正预算、未知版本/流程、非法原料、游戏日期与倍率均抛出 `ScenarioConfigurationException`。加载后的参数、只读时间段以及原文件绝对路径固定；修改文件只影响下一次加载。报告不保存配置副本或整份配置。

`BuyProcessSellScenario.Start(configuration, reportDirectory, currentGame=null, actualSeed=null, logging=null)` 返回独立或现场共用的运行器。宿主先创建并验证唯一报告目录；启动时将其目录名固定为报告 `RunId`，首个流程命令之前记录开始。独立局创建显式种子 `FarmGame`，使用传入的共享日志会话并标记用途 `ScenarioIndependent`，先暂停，通过正式命令拆除赠送设施、付费建造唯一匹配场地并设置原料底线0；启动完成后允许独立驱动推进。现场必须传入真实对象，保留其原日志身份与 `Main` 用途，只绑定匹配场地，不解除暂停、拆设施、撤他单或改底线。现场实际种子未知时传空。正式拒绝保留在 `Outcome` 和报告中，不通过异常强迫成功。启动准备抛出原异常时释放已创建的独立局观察并继续传播异常。

首段终点在任何经营命令前与实际基准核对。必须晚于当前时点，其他终点已在加载时严格递增。`processorAnchor` 必须是匹配场地的真实锚点，不能只传其子格。

流程暂停、拆除、建造、底线、买入和创建一次卖单显式传入 `CommandOrigin.Scenario`，倍率选择使用 `SimulationRateSource.Scenario`。日志用同一会话的局身份、序号及命令来源关联经营行为；`RunId` 仅关联本次流程事件与 JSON 报告，不隐式附加到玩家或自动经营事件。

## 唯一驱动的调用顺序

窗口与主场景通过 `Game` 得到目标，通过 `IsRunning`、`Stage`、`Outcome` 和 `Progress` 显示进度。运行器没有计时器、现场tick循环或生产计算。

1. 运行前由宿主调用 `ScenarioReport.CreateRunDirectory(root)`，创建唯一目录并检查可写，失败直接显示输出错误且不启动业务。
2. 加载配置、启动流程；仅在运行时通过共用 `SimulationDriver` 提交 `CurrentRateIntent`，来源为 `Scenario`。
3. 驱动以 `GetMaxAdvanceTicks()` 限制每次批量推进：当前时间段剩余完整tick与当前等待预算取最小值。暂停零推进，不消耗预算。
4. 稳定检查点调用 `ObserveCheckpoint(SimulationCheckpoint)`。检查点必须对应目标当前真实日历。回调可以在完整相位完成后提交一次建单，返回false结束本批；驱动在下一段重读预算、倍率和业务事件。false也可能只表示阶段转换或换速，是否终止由 `IsRunning` 判断。
5. 宿主在倍率来源通知及暂停变化时调用 `ObserveTime(actualRate, source, paused)`。玩家改速先记录实际通知，再 `Abort(reason)`，保留玩家倍率，不继续提交自动操作。
6. 流程终止后调用 `WriteReport()`，仅在启动时固定的目录输出 `report.json`。成功或保存异常分别记录报告事件；异常继续向宿主传播，保留内存中的执行结果，不更换路径、不重新读取现场。

现场测试结束后 Main 继续正常经营。独立测试终止后停止自己的驱动，不切换当前地图；宿主在报告保存尝试完成之后释放独立 `Game`，保存失败也须释放，原地流程不得释放主局。`Abort` 仅对正在运行的流程生效；已有买入、生产和订单保留，后续检查为未执行。

## 结果与证据

流程结果包括 `Passed`、`CompletedWithInsufficientEvidence`、`PreconditionsRejected`、`OperationRejected`、`WaitLimitExceeded`、`TimeRangeExhausted`、`CheckFailed` 和 `Aborted`；`Running` 表示进行中。配置错误属于启动入口的 `ScenarioConfigurationException`，输出错误属于写入入口的I/O错误，分别显示，不覆盖实际业务结果。

每项检查独立记录 `Passed`、`Failed`、`InsufficientEvidence` 或 `NotExecuted`。独立局所有必需检查通过才能给出 `Passed`；现场缺少批次凭据及完整资金历史，生产和资金归因明确记录证据不足，完成不会给出严格通过。

报告包含配置引用、`configurationValidation=Passed`、实际种子、真实初始化命令、同步操作前后快照、真实区间及产出汇总、请求/实际倍率与来源、暂停观察、等待tick数、首次观察本单成交的tick、检查结果及终止快照。真实区间不冒充每秒都观察过；`fillFirstObservedTicks` 不是订单模块提供的成交时间戳。最终快照在停止检查点捕获，之后现场变化不修改报告证据。

报告位置及操作说明见[参数化测试使用说明](../../project/parameterized-tests-usage.md)，内部顺序见[固定流程实现](implementation-buy-process-sell.md)。

日志观察只使用已加载配置的 `Revision/Sha256`，不重新读原文件。开发侧把既有结果投影为字符串和标量交给公共 `ScenarioLog`，公共日志不引用发布构建排除的开发类型。日志字段及生命周期见[时间与流程日志](../logging/implementation-time-scenario-observation.md)。
