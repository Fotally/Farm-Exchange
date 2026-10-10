# 原异常投影与同步传播观察

履行[日志 Interface](interface-logging.md)与 [schema v1](../../project/runtime-log-schema-v1.md)。本实现记录真实推进异常，不修改经营调度、时间换算、性能预算或异常处理决策。

## Module 与 Interface

业务保留执行、捕获及 `throw;`；领域 Adapter 决定事件来源和语义；日志内部处理异常字段、局上下文和传播关联。`SimulationLog.BeginTick/BeginBatch` 与 `TimeLog.BeginAdvance` 创建短生命周期的 `SimulationLogOperation`，调用方仅需 using、实际阶段标记与 Faulted。原命令保持 Complete/Faulted 的 Interface。

`ExceptionProjection.Add` 唯一投影原类型与原 ToString 文本，命令、推进、初始化 FatalException、报告保存失败都复用它；格式、转义、预算和截断继续属于 formatter。初始化和报告失败没有强制加入推进作用域，也没有被改写为通用 BusinessException。

`LogOutput` 不增加事件名单或业务判断，领域仍经同一 Submit 递交事件；既有 Observe 隔离投影故障。删除共用观察会让命令、单 tick、批量及驱动重新各自维护异常关联和去重，因此共用 Implementation 留在日志 Module 内，不扩散字段字典给业务。

## 凭据的唯一归属与清理

每个 GameLog 仅保存本局当前推进观察。新推进观察保存父调用引用，在 using 结束时清空自己收到的异常引用并解除活跃关联；Dispose 幂等且不输出成功。关联不使用 static、AsyncLocal、线程上下文或 Exception.Data，也不会跨局共享异常对象表。

命令创建自己的独立观察凭据，并保存创建时的活跃推进作为父调用；命令自身不成为隐式调用栈顶。因此公开命令观察可以按既有约定完成，不新增 Dispose 或严格栈序要求，也不会因未终结命令阻挡后续推进关联。

Faulted 先检查本次凭据是否已经观察到相同异常对象，再把该对象登记到自己及仍活跃的父调用。随后才投影和尝试输出：内层命令发出一次 BusinessException 后，批量和驱动的 Faulted 都能识别同次传播，保留最内层的 CommandId 与真实上下文。不同对象即使消息相同也分别记录。标记使用引用相等，不调用异常自定义 Equals/GetHashCode。

登记只向父调用传播，不向新子调用传播。兄弟命令各有新的凭据，所以外层仍活跃时，先后两个独立命令重用同一 Exception 也各自记录。新的推进请求亦建立新凭据；结束的作用域不形成长期黑名单。凭据表示“已接收并尝试投影”，不是“已成功写盘”。若 ToString、投影或 File 失败，外层不再次尝试记录同一传播；原业务异常照常抛出，健康诊断说明日志故障。

命令异常事件与 CommandFinished 使用独立的故障隔离调用，原异常文本投影失败仍可记录 CommandFinished=Faulted。生产窗口和失败批次的 finally 保持原顺序；它们表达资源覆盖和测量结果，不替代 BusinessException，也不因异常去重而被抑制。

## 实际阶段接入

| 接收点 | 执行前标记 |
| --- | --- |
| 单 tick、整批容量检查 | AdvanceValidation |
| 批量最近事件查询 | EventSearch |
| 平静状态与工人累计 | QuietAdvance |
| 事件结果清理与显式降雨 | TickPreparation |
| 稳定锚点循环中的农田 / 加工分支 | Harvest / Processing |
| 换季促熟 | Harvest |
| 领取 / 工人及实际播种记录 / 日期及计划同步 / 订单 | RawClaim / Workers / Calendar / Orders |
| 平静累计后的日历与行情更新 | Calendar |
| 完整推进后的宿主检查点 | Checkpoint |
| 驱动输入及每段帧容量校验 | DriverValidation |
| 驱动调用宿主 maxTicks | DriverBudget |
| 驱动返回后的剩余现实时间及累计 tick | DriverProgress |

这些标记不依赖 EventTickTiming 开启，不从异常文字、性能结果或业务结果反推。经营单秒依旧共用原六相位，批量保留平静累计，不额外运行任何相位或回调。Main 保持现有界面反馈，不再重复记录相同推进异常。

## 验收依据

通过真实公开调用验证单 tick/批量容量、检查点、驱动预算及帧容量异常；验证原对象传播、类型/消息/堆栈、实际 Phase 和局上下文。命令→推进→驱动只应出现一条 BusinessException，同时保留 Faulted 终结和失败批次。用独立兄弟调用及后续新请求检验相同对象可以重记；用不同对象同消息检验不按文本合并。

关闭采集、异常文本投影故障和真实输出故障必须保持业务结果与原异常，不产生外层重复尝试；Release 的关键异常记录保留，详细诊断门禁不受影响。完整测试和实际 Release 验收由主任务统一执行，结果记录于[日志验收](implementation-validation.md)；性能验收与进一步压测继续归 #66。
