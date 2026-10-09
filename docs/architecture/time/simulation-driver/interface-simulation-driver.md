# SimulationDriver 对外接口

关联 [#100](https://github.com/Fotally/Farm-Exchange/issues/100)。代码为 `scripts/time/SimulationDriver.cs` 与 `SimulationRateSource.cs`，由宿主为每局维护唯一实例；`Main` 直接按渲染帧的现实秒数推进，不再使用场景计时器。

| 成员 | 调用约定 |
| --- | --- |
| `SimulationDriver(logging=null)` | 可选接入同一局 `GameLog.Time`；宿主仍为每局维护唯一驱动。未注入时不采集。 |
| `Rate` / `Progress` | 当前每现实秒经营 tick 数，以及已积累但尚未完成的 tick 小数。初始 1×、进度 0。 |
| `SetRate(rate, source)` | 公共接口仅接受 0.5、1、2；非法值抛 `ArgumentOutOfRangeException`，不改变倍率。改速保留小数进度。来源默认为 `Player`。 |
| `IsPublicRateAllowed(rate)` | 发布与开发构建共用的公共倍率检查。 |
| `RateChanged` | 通知实际倍率及 `Player` / `Scenario` 来源。玩家重复选择当前倍率也发出通知，现场流程据此中断。 |
| `SetDevelopmentRate` / `IsDevelopmentRateAllowed` | 仅 `DEBUG` 构建存在，接受公共倍率或有限正整数倍率。发布程序集不存在这些额外入口；公开接口不能解锁 3× 等开发倍率。 |
| `Advance(delta, game, checkpoint, maxTicks)` | 输入有限非负现实秒数；暂停返回 0 且不积累时间。计算完整 tick 后调用同一 `FarmGame.AdvanceTicks`，返回实际完成数。 |

完整检查点由经营模块产生，驱动不分拆经营相位。`checkpoint` 返回 false 结束本次批量请求；驱动重新读取倍率与 `maxTicks`，按余下现实时间提出下一段请求。回调内改速也结束旧倍率请求；余下现实时间使用新倍率，不把整帧按旧倍率结算。`maxTicks` 返回 0 停止本帧推进；宿主在流程结束后，当前局恢复正常限额，独立局结束受控推进。

日期终点、剩余等待预算、下一操作均由流程提供限额，经营内部再按真实事件拆段。驱动只拥有倍率及未完成 tick，小数进度暂停和改速时保留；日历、订单、生产与工人状态仍由原模块唯一维护。整数推进超过 uint32 请求容量，或经营日历不能接受完整请求时明确拒绝；不截断、不自动换倍率。界面显示真实拒绝并允许玩家调整倍率。

#129 在原合法性检查之后、真实赋值之前开始倍率观察，赋值后先完成 `SelectSimulationRate` / `SimulationRateSelected`，再按原顺序发出 `RateChanged`。同值选择也有完整记录，因此玩家主动选择导致流程中断时，可以先看到已接受选择，再看到流程结束。非法倍率保留原异常且不记接受事件；通知处理器的原异常继续传播，不回滚已接受倍率。旧入口对非法强转来源的原样通知行为保留，日志记录原十进制数字字符串而不解释为合法来源。时间观察不改变暂停和 tick 小数进度，细节见[时间与流程日志](../../logging/implementation-time-scenario-observation.md)。

#130 在 Advance 的原输入检查前通过 `TimeLog.BeginAdvance()` 建立 using 观察；帧输入/容量、宿主 maxTicks 回调和返回后进度累计分别标记 DriverValidation、DriverBudget、DriverProgress。真实业务仍在驱动中执行，catch 记录后使用 `throw;`，不修改倍率、剩余现实时间、请求拆段或原失败效果。内部 FarmGame/命令已记录的同次异常向外传播不重复记录；正常帧不新增成功日志，释放仅清理关联。此观察使用构造时传入的同局 TimeLog，未注入或关闭采集保持原行为。Interface 与生命周期细节见[共用异常观察](../../logging/implementation-exception-observation.md)。

`TestSimulationDriver.RunChecks()` 验证半 tick、暂停不累计、恢复改速、公共/开发允许规则、主动选择来源、途中换速、宿主终点及容量拒绝；`TestTimeLogging` 验证实际暂停变化、同值倍率及通知顺序、异常传播、来源和采集开关/输出故障等价。表现连接约定见[工人表现](../../world/worker-presentation/interface-worker-presentation.md)，批量结算约定见[批量实现](../../game-state/farm-game/implementation-batched-simulation.md)。
