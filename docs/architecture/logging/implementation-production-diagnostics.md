# 生产与工人受控诊断实现

本实现履行[日志 Interface](interface-logging.md)，字段含义由 [schema v1](../../project/runtime-log-schema-v1.md)维护。`ProductionDiagnostics` 与 `WorkerDiagnostics` 只投影真实生产与调度事实；范围、事件集合、时长与总条数由共用采集控制拥有。默认关闭，Release 在建立明细前拒绝采集。生产汇总独立累计，详细采集结束或预算耗尽不影响 runtime 生产窗口。

## 生产转换

农田模块在真实赋值点区分播种、供水、开始生长和越季清理。湿田播种依次经过 None→Seeded→Growing，分别记录 Sown 和 GrowthStarted；供水先记录 HasWater 的真实变化，再记录随后开始生长。降雨也使用同一真实供水路径，重复已有水分不产生 Watered。详细 Watered 描述供水状态变化，不能直接当作工人成功浇水次数；工人次数继续来自调度器成功回调。

收获和加工完工在 FarmGame 的真实库存 Add 成功后记录，诊断前状态在原推进之前且通过门禁后采集。正常成熟和换季促熟沿用同一收获提交点。越季清理沿原遍历记录选定锚点的真实前后，不从按作物清理总量反推逐田历史。

加工领取和启动按原操作顺序记录。RawClaimed 表示真实扣除一份原料，Quantity=1；ProcessingStarted 表示随后建立批次，仅描述状态变化，不携带第二份消耗。加工前后状态通过 `GetStatus(index, inventory)` 查询，空闲时区分待料、因底线等待和可领取，不能从剩余秒数为零推断待料原因。忙碌、底线、冻结或无料导致的失败开工不产生成功转换。

所有前快照先经过对象与事件门禁；未选中、已结束或 Release 不构造详细状态。日志投影与输出异常被日志模块隔离，业务操作仍只执行一次。农田和加工模块仅持有可选诊断入口，不维护采集窗口、协议字典或第二份生产状态。

门禁方法与含捕获 lambda 的投影方法分开：C# 编译器可能在方法入口就创建闭包，即使源码把 ShouldCapture 写在 lambda 前，也会让被拒绝的调用分配对象。BeginFarm、BeginProcessor、农田/加工转换、播种检查、工人任务/移动入口只做门禁，通过后才调用私有 Capture 方法。此调整只影响日志实现，不改变事实、预算或生产执行。`TestProductionDiagnostics` 预热后通过 `GC.GetAllocatedBytesForCurrentThread` 验证关闭采集和未匹配对象各一万轮七种入口的托管分配严格为 0，不用容忍阈值掩盖每调用分配。

## 播种检查

只有 TryCompleteWork 的原执行重验向 PlantingRules 传入诊断入口，在同一次 Season/Time 判断后交出结果，不重新执行检查。GetWorkNeed、下一事件探测、详情与选种风险查询、计划静态校验不输出 RuleChecked，也不占用采集预算。WrongSeason 只提供实际执行的 Season=false，省略尚未执行的 Time；适季时提供 Season=true 和真实 Time 结果。InsufficientTime 是风险，CanSow 仍允许播种，RuleChecked 的 Outcome 保持 Success，不能把 Time=false 写成业务拒绝。测试通过真实 TryCompleteWork 验证跨季旧任务拒绝与原状态不变，并核对反复查询后只有一次实际重验占用预算。

## 工人转换与移动

调度器在真实认领、释放、开始行程、抵达、播种后转为供水等位置记录变化，避免仅比较整秒首尾而漏掉同秒释放再认领。TargetAnchor 使用 `FarmWorkRequest.CellIndex` 的实例锚点；公开 WorkerSnapshot.TargetCell 是工作中心，不能直接当作锚点输出。Activity 仍代表下一步活动，不伪装成成功结果。

WorkerMoved 记录经营位置前后，使用基础格坐标，不采集每帧插值。quiet 行程沿用原一次区间推进，只记录该实际区间的一组前后位置，不拆成逐秒循环。任务选择、独占认领、游标和移动公式保持原有实现。
