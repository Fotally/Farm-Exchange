# 农田本轮身份与播种许可修复实现

关联 [#135](https://github.com/Fotally/Farm-Exchange/issues/135)。修复分支为 `fix/135-farm-work-identity`，基线为 `ed84e455d703e59cc0257b7295fe6c866e6987f6`。下面的 Interface 和 Implementation 已实施，并完成 Debug 编译与四个专项场景；完整集成、独立审查、覆盖率和发布导出验收仍由集成阶段完成，不能将专项通过视为交付闭环完成。现行约定见 [FarmingSystem Interface](interface-farming-system.md)。

FarmingSystem 已分开“当前轮次身份”和“未来播种许可的版本”。计划只关闭后续复种时，保留当前 Water 凭据及成功 Sow 的所属轮次；真正换轮时统一使旧工作和成功动作失效。工人与表现调用方继续向同一个 Module 查询，不各自解释计划状态。

## 问题和可观察结果

在基线代码、Godot 4.7.2 Mono 中，两种计划模式的四田复现均得到：第 1 秒三条 Sow 成功，但有效表现结果为零；第 2 秒原三田供水数量为零，预期为三。无计划对照的有效表现和供水数量均为三。只有三田时重新派工可能回到原田，掩盖供水缺陷，但不会消除表现失效。

基线的 `SetSowingEnabled` 递增统一 revision；计划在工人成功播种及续接 Water 后调用它，因而把许可变化解释为当前轮变化。本次修复收紧了 [工人认领约定](../../workers/worker-scheduler/interface-worker-scheduler.md) 与低层测试中“禁播使旧 Water 失效”的冲突，保持 [供水规则](../../../gameplay/production/water-and-rain.md)。

## Module 和状态归属

| Module | 唯一拥有的状态或判断 |
| --- | --- |
| FarmingSystem | 作物、阶段、水分、当前轮次身份、播种许可及其版本；工作能否执行、成功动作是否仍属本轮 |
| WorkerScheduler | 工人位置、移动进度、认领和轮转；保存不透明工作凭据 |
| CultivationPlanBook | 年度表、绑定和每条每年的执行凭据；通过原命令改变播种许可或真正切换作物 |
| FarmGame | 原经营相位、真实成功结果和最近秒窗口；委托 FarmingSystem 校验成功动作 |

本轮身份指“当前轮次槽”：建田、重启或上一轮结束后建立的空轮，与随后实际播种、生长属于同一身份。实际 Sow 不另换身份，避免成功前获取的凭据在成功后立刻失效。拆除后的代次必须保留在槽外，不能在同格重建时从零恢复。

## 已实现的 Interface

保持 `GetWorkNeed(index, calendar)`、`TryCompleteWork(request, calendar)`、`SetSowingEnabled(index, enabled)` 的调用方式。内部值为 `FarmWorkRequest(CellIndex, RoundRevision, Kind, SowingRevision)`，仅由 FarmingSystem 生成；Water 的 `SowingRevision` 固定为零，Sow 使用当前许可版本。

`IsCompletedWorkCurrent(completedWork)` 替换原 `IsWorkRevisionCurrent`，成员数量不增加。它验证原田仍存在且轮次相同，不要求动作仍可再次执行，不比较未来播种许可版本。FarmGame 继续另外检查原设施快照身份，以及最近秒窗口；已开始片段仍使用原目标有效性入口。

WorkerScheduler 原有的 `GetWorkNeed(...) == savedRequest` 和执行重验继续成立：Sow 比较轮次及许可版本，Water 只随轮次或真实需求变化失效。调用方不读取裸计数，也不增加“这是计划造成的变化”参数。公开 `ProductionResult` 不新增版本字段。

## Implementation 和失效矩阵

按格保存、跨拆建不回退的计数现仅表示 RoundRevision。在已有 FarmState 内保存 SowingRevision，实际启停变化只推进它；相同值重复设置零修改。工作需求仍由实际阶段、季节和水分判断，许可版本不是第二份生产状态。

| 操作 | 轮次身份 | 播种许可版本 | 旧待执行 Sow | 旧待执行 Water | 已成功 Sow 或 Water |
| --- | --- | --- | --- | --- | --- |
| 单纯启停播种 | 保持 | 改变 | 失效；再启用也不复活 | 需水仍存在时保持 | 保持所属轮次 |
| 重复设置相同启停值 | 保持 | 保持 | 按当前需求判断 | 按当前需求判断 | 保持 |
| 实际播种 | 保持 | 保持 | 阶段变化后不能再执行 | 新需求可被原播种者领取 | 保持 |
| 雨水满足需水 | 保持 | 保持 | 空田且仍适季可继续 | 真实需求消失，取消认领 | 已成功动作仍属本轮 |
| 异种切换或同种强制重启 | 改变 | 无须额外改变 | 失效 | 失效 | 失效 |
| 正常收获、促熟、禁生清理 | 改变 | 无须额外改变 | 失效 | 失效 | 失效 |
| 拆除、重建、清空 | 改变且不回退 | 新状态按初值建立 | 失效 | 失效 | 失效 |

成功动作的判断只供可信成功结果消费；待执行任务始终经 `TryCompleteWork` 验证当前需求。两种判断不能互相代替。计划在真实换轮时继续调用 RestartCrop；单纯关闭后续复种仍调用 SetSowingEnabled。经营相位、每条每年一轮、禁生清理、随机抽取及库存结算顺序保持。

## 方案比较和 Depth

| 方案 | 评估 |
| --- | --- |
| 只在非空田启停时不递增旧 revision | 可修眼前续水，但仍把两种身份混在一起；成功记录何时失效依赖阶段分支，未来调用仍需理解同一计数的两种含义 |
| 在工人、计划、表现分别忽略启停 | 相同规则散落三个调用方，易破坏真正重启及旧 Sow 的失效 |
| 分离轮次与许可版本，保留原 Seam | 已采用；同一处解释待执行工作与已成功动作，保留完整旧任务保护 |

FarmingSystem 的 Depth 来自少量完整判断支撑工人、计划、表现和测试。Locality 集中在它的身份生成及失效路径；删除这项能力会迫使多个调用方重新保存轮次和启停历史，说明它有实际 Leverage。已有 FarmingSystem 是真实 Seam，不需要新增 Adapter、策略注册器或公共调度框架。

## 专项验证与剩余集成

回归通过 FarmGame 的建造、计划、推进及查询 Interface 驱动；不靠反射设置计数器。扩展现有测试类，沿用其已接入 TestSuite 的入口。

- 两种模式分别验证三田和四田：第 1 秒三条 Sow 仍有效、三位工人保留各自 Water 目标；第 2 秒由相同编号工人在原田供水，没有额外移动。无计划对照保持。
- 验证雨水替代 Water、手动预备下一轮、首次向已播种田应用计划、同表重应用及编辑后保留当前轮；检查年度条没有重复执行。
- 低层验证禁播后旧 Sow 不可执行、再启用不复活旧 Sow、新凭据可执行；把旧 Water 必须失效的冲突断言改为仍能完成同轮供水，并防止重复执行。
- 同种强制重启、异种切换、拆建、换季清理、收获换轮仍拒绝旧任务及旧动作；暂停不让真实结果过期，雨水不伪造成功 Water。
- 单秒与批量推进对照、正常与促熟入库、工人轮转及生产表现回归；完整验证仍须满足逐模块和总体 80% 行覆盖率及项目导出启动约定。

在 Godot 4.7.2 Mono 与 .NET SDK 10.0.401 下，先只增加四田两种模式的真实回归，Debug 编译成功后运行 `test_production_results.tscn`，确认两种模式各三条 Sow 同秒失效、三个原工人第 2 秒供水均失败，场景退出码为 1。实施分离后同一场景退出码为 0。旧低层 Water 必须失效的断言随后按新约定改为“原 Water 可完成且不可重复执行”；旧 Sow 启停后不可复活的保护保留。

新增三／四田及无计划对照、手动预备／首次应用／重应用／实质编辑后续条、计划雨水取消 Water，以及两种模式的单秒／批量状态、工人、真实结果及逐年单轮约束。现有年度编辑、跨年、季末清理和促熟用例继续执行。四个专项入口 `test_production_state.tscn`、`test_production_results.tscn`、`test_cultivation_plan_book.tscn`、`test_worker_scheduler.tscn` 均退出 0；临时日志保存在工作区 `build/issue135-validation/`，不随源码提交。完整 TestSuite、覆盖率、Release 导出与启动仍待 dev 集成验收。

## 分支文件所有权与集成

实施文件归本分支独占：`scripts/farming/FarmingSystem.cs`、`scripts/gameplay/FarmGame.cs`。公开 `ProductionResult` 无需变化，WorkerScheduler 与 CultivationPlanBook 算法无需修改。

测试归本分支独占：`tests/unit/farming/TestProductionState.cs`、`tests/unit/gameplay/TestProductionResults.cs`、`tests/unit/cultivation/TestCultivationPlanBook.cs`、`tests/unit/workers/TestWorkerScheduler.cs`。专题文档归本分支独占：本目录 Interface 和本方案、工人 Interface、年度表 Interface、`docs/gameplay/production/water-and-rain.md`、`seasonal-cultivation.md`、FarmGame Interface。

`AGENTS.md`、`docs/README.md` 和 `docs/project/testing.md` 由 dev 集成步骤统一更新，三个修复分支不各自编辑。若需要超出上述文件，先重新核对所有权，避免暗中跨入其他分支。实现整合到 dev 后完成审查、编译、完整测试、覆盖率、Windows Release 导出和启动，再按现行流程交付，不能把专项回归当作完整交付。
