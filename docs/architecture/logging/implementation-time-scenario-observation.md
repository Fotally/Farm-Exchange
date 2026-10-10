# 时间与开发流程日志实现

履行[日志 Interface](interface-logging.md)。字段、单位和事件组合以 [schema v1](../../project/runtime-log-schema-v1.md) 为准；本文说明实际记录时点与组装责任。

## 时间指令

`TimeLog` 绑定本局日历上下文。`FarmGame.SetPaused` 在原修改前调用 `BeginPause`，提交后用真实 `IsPaused` 终结；同值设置仍有命令收到/完成，只有前后值不同时发出 `PauseChanged`。经营仍由原日历持有暂停，日志不推进日历。

`SimulationDriver` 构造可接收同一局 `TimeLog`。公共/开发倍率沿用原允许判断，非法倍率在原位置抛出，未通过的选择不伪造接受事件。通过校验后在赋值前开始观察，赋值后记录 `SimulationRateSelected` 和命令完成，再调用原 `RateChanged`。同值选择包含 `ValueChanged=false`，玩家通知仍能中断运行中的原地流程。通知异常保留原传播，已接受倍率不回滚。原入口未拒绝的非法强转来源保持原业务行为，日志保留其原十进制数字字符串。

时间命令共用 `CommandObservation` 关联与一次终结；Adapter 不执行设置，不包装或重试业务。采集关闭与输出故障不影响暂停、倍率、小数进度或原通知。

## 流程及报告

宿主加载配置并调用 `ScenarioReport.CreateRunDirectory` 后，将真实目录交给 `BuyProcessSellScenario.Start`。运行器在调用 `Prepare` 之前固定报告 `RunId`，用局的 `ScenarioLog.Begin` 发出开始事件。独立局由同一 `RuntimeLog` 创建，用途为 `ScenarioIndependent`；原地保留主局身份和 `Main` 用途。

`ScenarioLog/ScenarioRunLog` 只接收纯值：目录名、模式、实际加载修订与哈希、既有结果名称、结束说明、相对报告路径、原异常。公共日志没有开发类型引用；开发构建才会调用流程入口。局上下文的 `Phase` 使用已有 `Command` 值，不添加新相位。`RunId` 只进入流程事件和 JSON 报告，其他经营事件用局标识、序号和 Scenario 命令来源关联。

真实 `Finish` 完成最终快照之后通知 `ScenarioFinished`；Adapter 防止同一关联重复终结，不改变既有结果。`Passed`、证据不足、前置拒绝、经营拒绝、预算耗尽、日期耗尽、检查失败、中断均按原枚举名投影，不能从原因中文重新分类。

`WriteReport()` 在启动固定的目录使用 `CreateNew` 写入。成功路径为相对报告根目录的 `<RunId>/report.json`；实际异常记录保存失败后原样传播，不改流程 Outcome 或最终快照。报告写入的运行中调用错误仍是原前置错误，不冒充文件保存失败。宿主决定何时保存和释放；日志 Adapter 不建立执行容器、命令总线或配置副本。

## 宿主释放与验证

开发窗口把报告尝试封装为唯一幂等入口，普通刷新只展示返回的文件/错误说明；退出路径不访问子控件。窗口 `_ExitTree` 自行中断和保存，Main 关闭主局和会话前也可幂等通知。独立局在报告尝试后的 `finally` 释放，保证保存失败也不遗留局；原地流程不释放主局。

`TestTimeLogging` 覆盖同值暂停、真实变更、同值倍率、通知前记录、来源、异常传播和日志开关/故障等价。`TestScenarioLogging` 从实际流程生成八种终结结果，核对提前身份、加载凭据、保存成功/冲突失败、原地主局继续推进及不同局用途；使用真实 `OpenFile` 留下 `build/logging-samples/issue-129-flow-*` 双文件样例。`TestDeveloperToolsWindow` 通过真实节点退出验证独立释放和重复宿主通知。统一编译、测试与 Release 导出结果由 issue 验收记录维护。
