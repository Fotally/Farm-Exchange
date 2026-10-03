# 年度耕作计划 Module 的 Interface

实现位于 `scripts/cultivation/CultivationPlanBook.cs`，请求和值快照分别位于 `CultivationPlanRequest.cs`、`CultivationPlanSnapshot.cs`。外部调用方通过 [FarmGame](../game-state/farm-game/interface-farm-game.md) 提交命令；计划 Module 直接使用 [FarmingSystem](../farming/farming-system/interface-farming-system.md) 的精确生长时间和播种启停，不创建第二份作物生产状态。

| 公开值 | 调用方约定 |
| --- | --- |
| `CultivationEntry(Id, Crop, StartDay)` | 条编号为正且在同表唯一；年度日期为零基第 0～335 天。编辑片段仍使用原条编号。`LengthDays` 唯一读取作物完整周期。 |
| `CultivationPlanRequest(Name, Mode, Entries)` | 完整草稿；名称不能为空；模式只支持 `Immediate` 和 `PrepareNext`。允许无条的全年休耕表。 |
| `CultivationValidation` | `Error` 非空表示排程拒绝；`RiskEntryIds` 指明越过禁生边界的条，风险不构成拒绝。 |
| `CultivationCommandResult` | 成功给出稳定表编号；失败为中文原因且表和农田零修改。 |
| `CultivationPlanSnapshot` | 名称、模式、按日期排列的独立只读条集合和实际引用农田数。不会向草稿泄露可变配置。 |
| `FarmCultivationSnapshot` | 表编号与名称、缓存的预备目标作物和计划条绝对起点、是否为空田休耕、是否正在待水。本田手动预备时表编号为空。 |

`PreparedTimeUnits` 是累计精确比例单位：一模拟秒 7 单位，一游戏日 360 单位。它表示目标条的计划起点，可能早于当前轮预计完成日期，表示当前轮结束时仍落在该条内；不宣称工人会在该时刻实际播种。初年春季定位跨冬春条时，起点可以为负数，代表上一年度冬季段；显示时按真实年度位置解释，不能直接强转 `uint`。待水没有确定完成日期，不缓存推测的计划目标。手动预备仅指定作物，时间为空。

内部 Interface 为 `Create`、`Update`、`Apply`、`TakeManualControl`、`GetSnapshots`、`GetFarm`、`Harvested`、`RecordSownCrops` 与 `Synchronize`。`FarmGame` 原子检查批量农田并解析子格到锚点后才调用 `Apply`；配置查询不调用执行。`Update` 和 `Apply` 保留已播种或生长中的当前轮，再计算其后安排。手动立即改种强制中断，手动预备保留当前轮；二者均只解除本田引用。

各田按条编号和所属年度保存实际播种凭据，共享编辑和同表重应用都保留本田全部有效凭据；把较早已执行条拖到本年未来日期也不能再播该条。只有实际开始该计划条才记执行，初次应用保留的手动当前轮不计入计划条。新年度允许再次执行相同编号；跨年仅保留本年与上一年度凭据以支持冬春条。手动接管解除绑定及其凭据，不影响其他田。

经营收获完成后先调用 `Harvested(index, nextTimeUnits)`。计划田仅关闭空田播种、保存目标并标记本轮完成，不能在尚使用旧日历的本步工人相位启用下一计划条；手动预备仍在该通知中即时接续。工人动作完成后、日历及换季清理前调用 `RecordSownCrops()`，只登记已实际播种的计划条凭据，不查询预备目标、不执行日期事件。这样在季末实际 Sow 随后立即被禁生清理时，该条仍被视为已执行。换日和换季清理完成后调用一次 `Synchronize(now)`，按真实已生效日期重验并启用计划目标；已播当前条变为空田时也更新中断安排。后者还检查缓存事件、实际供水导致的完成日期确定、当前轮中断与完成，不以日历天变化重新扫描年度表。具体日期定位和排程见[实现](implementation-annual-events.md)，玩家规则见[季节耕作](../../gameplay/production/seasonal-cultivation.md)。
