# 年度表与手动接管观察（#129）

本文履行[日志 Interface](interface-logging.md)，字段含义与单位以 [schema v1](../../project/runtime-log-schema-v1.md) 为准。`CultivationLog` 是耕作领域 Adapter；年度表、引用和轮次仍由[耕作 Module](../cultivation/interface-cultivation-plan-book.md)与农田 Module 唯一维护。日志不执行排程检查或经营命令。

## Interface 与实际提交

`GameLog.Cultivation` 的 `BeginCreate(request, origin)`、`BeginUpdate(id, request, origin)`、`BeginDelete(id, origin)` 返回 `CultivationPlanLogOperation`。创建与更新使用 `Complete(CultivationCommandResult)`，删除使用 `Complete(string? error)`；成功为原业务返回的成功，不以记录收到命令推断成功。创建原请求保留名称空格、条目顺序及非法枚举或整数值；实际 `PlanAfter` 来自已保存快照，独立表示修整后的名称和排序后的条目。更新拒绝保留原有效前后配置；未知表没有有效配置。只有真实存在于前快照或后快照的表才输出领域 `PlanId`；原输入编号始终在 `CommandArguments.PlanId` 中，负数或已删除编号不假装仍有有效身份。删除成功从前快照真实引用数取得 `DetachedFarmCount`，拒绝为 0。

`BeginApply(id, cells, origin)` 返回 `CultivationApplyLogOperation`。完成时 `Complete(error, resolvedIndices)` 直接消费原验证循环已完成去重的锚点索引列表。调用方复用传给耕作 Module 的同一个列表，不为日志复制全量目标；Adapter 只在同步完成观察时访问前 32 个索引并转换为锚点坐标。提前遇到无效表、越界或非农田后仍按原逻辑立即返回，解析数为 null，应用数为 0；有效表的空输入已明确为 0。日志不将解析前缀当完整目标数，不遍历剩余目标凑完整结果，不修改原子应用顺序。

`BeginManualControl(cell, crop, mode, origin)` 返回 `FarmManualControlLogOperation`，调用方在真实 `SetFarmCrop` 或 `PrepareFarmCrop` 之后 `Complete(error)`。前观察只按单个目标读取锚点、选种、阶段和表引用，后观察读取真实当前选种及预备安排；不存在农田时状态为 null。成功立即接管即使选种相同仍记录重启，本轮保留为 false；成功预备接管仅在原田已有待水或生长轮次时记为保留，空田实际立即切换，下一轮为 null。拒绝时实际选种可查询并保留，`PlanDetached`、`CurrentCyclePreserved` 为 null，不声称发生接管。下一轮字段读取实际安排，不覆盖当前轮作物，也不把原计划尚存的预备安排当成成功接管。

六个命令分别使用现有经营方法名作为 `CommandName`，默认 `Player`，显式 `Scenario` 透传。非法来源在采集与业务之前抛出，关闭采集也相同。手动模式由两条语义入口确定，公开观察入口拒绝未定义模式。

三个观察类型均复用 `CommandObservation`，共享局内命令编号、收到/结束、原异常关联和一次终结。业务捕获点调用 `Faulted(原异常)` 后继续原传播；观察对象不消费异常、不重试业务。日志投影或写出故障沿通用日志隔离，重复完成或报错无副作用；关闭采集或局已经结束时 `Begin` 返回 null。生产安全封窗由通用命令结束维护，不另建耕作窗口。

## 采样与读取成本

原请求条目只保存前 32 条纯值投影，条数使用原列表 `Count`。原始输入 `Cells`、有效配置的 `Entries` 以及实际 `TargetAnchors` 各自最多输出 32 项；截断路径、原 `ItemCount` 与 formatter 的文本截断信息合并。目标样本同时输出 `TargetsTruncated`；完整输入数、完整去重数和应用数不受采样预算影响。单事件继续受通用 32 KiB 上限限制。日志样本不承诺恢复完整年度表或全部引用。

有效前后配置只查询本次表 ID，内部克隆目标表条目并统计现有绑定，不读取整个表库；手动接管查询为单田常数成本。没有新增计划修订号、配置副本文件、长期计数缓存或反向索引。原请求采样与目标采样是日志数据，不参与正式状态。

## 验证

`TestCultivationLogging` 通过经营命令和公开观察验证名称/排序与原请求区分、非法配置原值、更新拒绝有效配置保持、未知编号、按 ID 独立快照、删除引用与本轮保持、重复子格去重、空输入、中途拒绝和尾段不继续解析、同种立即重启、两种接管在空田/待水/生长阶段的事实、独立农田不受影响、来源与非法来源、集合及文本截断合并、关闭/未接入/输出故障时经营等价、原异常与一次终结。大目标采用既有满图夹具准备设施，实际应用仍通过公开命令验证，不修改日志或业务判定开关。

本页描述实现与用例范围，不宣称已通过运行验收；编译、完整套件、覆盖率、Release 导出与启动结果由主任务统一记录。
