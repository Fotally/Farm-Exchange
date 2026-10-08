# 年度耕作计划 Module 的 Interface

实现位于 `scripts/cultivation/CultivationPlanBook.cs`，请求和值快照分别位于 `CultivationPlanRequest.cs`、`CultivationPlanSnapshot.cs`。外部调用方通过 [FarmGame](../game-state/farm-game/interface-farm-game.md) 提交命令；计划 Module 直接使用 [FarmingSystem](../farming/farming-system/interface-farming-system.md) 的精确生长时间和播种启停，不创建第二份作物生产状态。

`GetSnapshot(id)` 供 `FarmGame.GetCultivationPlan(id)` 按编号读取单张现存表，不存在时返回 null。它与全表查询使用相同的独立只读快照：只克隆目标表的条目，遍历现有绑定字典统计真实引用数，不创建计数缓存或反向索引。成本为目标表条目数加绑定数；不因一次命令观察而克隆其他表或遍历整张地图。原全表查询仍服务年度表管理列表。

创建、完整更新、删除、原子批量应用及两种手动接管的 [日志观察](../logging/implementation-cultivation-observation.md) 在 `FarmGame` 语义入口组装。日志只消费原请求、此处的真实结果与只读快照，不改动本 Module 的验证顺序、年度凭据、原子提交或当前轮规则。批量应用的完整解析列表来自原调用方验证循环；提前拒绝不为日志补做目标检查。

`Delete(id)` 由 `FarmGame.DeleteCultivationPlan` 调用：删除共享配置，并解除全部引用田的绑定、执行凭据、预备目标和日期事件。保留各田当前作物、当前轮阶段、精确剩余时长及水分，只恢复播种许可；空田和收获后的农田按当前作物自动复种，仍遵守原有季节判断并等待工人实际播种、供水。删除不推进经营、不收费、不修改金币或库存，不改变其他表、其他农田或手动预备安排；已分配的表编号不回收。成功返回 `null`；未知或已删除表返回“耕作表不存在”，全部状态保持。删除后旧表不能更新或再次应用，旧只读快照保持原值。

| 公开值 | 调用方约定 |
| --- | --- |
| `CultivationEntry(Id, Crop, StartDay)` | 条编号为正且在同表唯一；年度日期为零基第 0～335 天。移动或编辑片段仍使用原条编号；新增条使用不小于该表 `NextEntryId` 的编号，删除旧条不得复用其编号。`LengthDays` 唯一读取作物完整周期。 |
| `CultivationPlanRequest(Name, Mode, Entries)` | 完整提交草稿；保存时名称不能为空，模式只支持 `Immediate` 和 `PrepareNext`。允许无条的全年休耕表；编辑条目不依赖名称。 |
| `CultivationValidation` | `Error` 非空表示排程拒绝，包含作物条从不适宜季节开始的中文原因；`RiskEntryIds` 指明适季开始但越过禁生边界的条，风险不构成拒绝。 |
| `CultivationCommandResult` | 成功给出稳定表编号；失败为中文原因且表和农田零修改。 |
| `CultivationPlanSnapshot` | 名称、模式、按日期排列的独立只读条集合、实际引用农田数及该表下一可用条编号 `NextEntryId`。编号在表生命周期内只增不减，保存空表后仍保留。不会向草稿泄露可变配置。 |
| `FarmCultivationSnapshot` | 表编号与名称、缓存的预备目标作物和计划条绝对起点、是否为空田休耕、是否正在待水。本田手动预备时表编号为空。 |

`PreparedTimeUnits` 是累计精确比例单位：一模拟秒 7 单位，一游戏日 360 单位。它表示目标条的计划起点，可能早于当前轮预计结束日期，表示当前轮结束时仍落在该条内；不宣称工人会在该时刻实际播种。当前轮结束预测直接读取农田的 `GetExpectedRoundEndTimeUnits`，纳入正常成熟的实际 tick 与首个禁生季的促熟或清理结局；适季跨季不提前结束。初年春季定位跨冬春条时，起点可以为负数，代表上一年度冬季段；显示时按真实年度位置解释，不能直接强转 `uint`。待水没有确定结束日期，不缓存推测的计划目标。手动预备仅指定作物，时间为空。

内部 Interface 为 `Validate`、`ValidateEntries`、`Create`、`Update`、`Delete`、`Apply`、`TakeManualControl`、`GetSnapshots`、`GetFarm`、`Harvested`、`RecordSownCrops` 与 `Synchronize`。`ValidateEntries` 集中检查完整年度作物条，起点适季定义来自 `CropCatalog`，日期季节换算来自 `GameCalendar`；从不适宜季节起点开始拒绝，适季开始而周期跨入禁生季节则保留风险编号。条编号、日期、重叠、年度首尾及异种间距也由同一次检查处理。界面通过 `FarmGame.CheckCultivationEntries` 在悬停和提交落位时预检完整候选，不要求草稿名称；`Validate` 先检查名称和模式，再复用同一条目检查，原 `FarmGame.CheckCultivationPlan` 与 `Create`、`Update` 使用完整提交检查。预检只读，创建或更新被拒绝时共享表及农田不变，不自动挪条或换作物。`FarmGame` 原子检查批量农田并解析子格到锚点后才调用 `Apply`；配置查询不调用执行。`Update` 和 `Apply` 保留已播种或生长中的当前轮，再计算其后安排。手动立即改种强制中断，手动预备保留当前轮；二者均只解除本田引用。

每表 `Plan` 唯一持有编号高水位：创建时下一编号为最大条编号加一，空表从 1 开始；成功更新取旧高水位和新增条所需下一编号的较大值，删空不下降。更新允许保留当前表中的旧编号，拒绝新增条使用低于高水位的历史编号；失败不消费编号或改变执行凭据。窗口只持有未保存草稿的局部递增编号，读取快照后以 `NextEntryId` 为起点；删除、移动、保存空表再重新打开都不能把新条恢复为旧条身份。

各田按条编号和所属年度保存实际播种凭据，共享编辑和同表重应用都保留本田全部有效凭据；把较早已执行条拖到本年未来日期也不能再播该条。只有实际开始该计划条才记执行，初次应用保留的手动当前轮不计入计划条。新年度允许再次执行相同编号；跨年仅保留本年与上一年度凭据以支持冬春条。手动接管解除绑定及其凭据，不影响其他田。

日期进入空白或下一同种条时，两种模式均保留当前轮及其原条凭据，关闭空田新播种；下一同种条必须等旧轮实际结束后独立启用，不能在同种起点重启旧轮或把旧轮虚记为新条已播。连续 n 条仍是 n 个完整周期、n 次独立执行。下一不同作物条起点到达时，`Immediate` 按日期中断旧轮并切换，`PrepareNext` 继续保留旧轮；两者均继续遵守农田禁生规则。空白缓冲不增加收获后的固定等待，手动立即同种重启约定保持。

经营收获完成后先调用 `Harvested(index, nextTimeUnits)`。计划田仅关闭空田播种、按实际结束位置保存目标并标记本轮完成，不能在尚使用旧日历的本步工人相位启用下一计划条；手动预备仍在该通知中即时接续。工人动作完成后、日历及换季清理前调用 `RecordSownCrops()`，只登记已实际播种的计划条凭据，不查询预备目标、不执行日期事件。这样在季末实际 Sow 随后立即被禁生清理时，该条仍被视为已执行。换日和换季清理完成后调用一次 `Synchronize(now)`，按真实已生效的结束位置重新定位并启用计划目标；旧缓存尚未过期也不能跳过实际结束校正。已播当前条变为空田时也更新中断安排。后者还检查缓存事件、实际供水导致的结束日期确定、当前轮中断与完成，不以日历天变化重新扫描年度表。具体日期定位和排程见[实现](implementation-annual-events.md)，玩家规则见[季节耕作](../../gameplay/production/seasonal-cultivation.md)。

批量内部接口 `GetNextEventSeconds(elapsedSeconds)` 只读取各引用的缓存条起止事件与下次年度凭据清理，返回首个到达整秒的相对距离；无共享表绑定为 `uint.MaxValue`。平静区间不重放 `Synchronize`，连续生长的预计绝对结束时刻保持不变；真实播种、供水、收获、日期起止与换季仍由原完整相位触发同步。手动预备的结束由农田事件驱动，不另造日期事件。参见[批量实现](../game-state/farm-game/implementation-batched-simulation.md)。
