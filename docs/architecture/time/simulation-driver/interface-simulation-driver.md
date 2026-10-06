# SimulationDriver 对外接口

关联 [#100](https://github.com/Fotally/Farm-Exchange/issues/100)。代码为 `scripts/time/SimulationDriver.cs` 与 `SimulationRateSource.cs`，由宿主为每局维护唯一实例；`Main` 直接按渲染帧的现实秒数推进，不再使用场景计时器。

| 成员 | 调用约定 |
| --- | --- |
| `Rate` / `Progress` | 当前每现实秒经营 tick 数，以及已积累但尚未完成的 tick 小数。初始 1×、进度 0。 |
| `SetRate(rate, source)` | 公共接口仅接受 0.5、1、2；非法值抛 `ArgumentOutOfRangeException`，不改变倍率。改速保留小数进度。来源默认为 `Player`。 |
| `IsPublicRateAllowed(rate)` | 发布与开发构建共用的公共倍率检查。 |
| `RateChanged` | 通知实际倍率及 `Player` / `Scenario` 来源。玩家重复选择当前倍率也发出通知，现场流程据此中断。 |
| `SetDevelopmentRate` / `IsDevelopmentRateAllowed` | 仅 `DEBUG` 构建存在，接受公共倍率或有限正整数倍率。发布程序集不存在这些额外入口；公开接口不能解锁 3× 等开发倍率。 |
| `Advance(delta, game, checkpoint, maxTicks)` | 输入有限非负现实秒数；暂停返回 0 且不积累时间。计算完整 tick 后调用同一 `FarmGame.AdvanceTicks`，返回实际完成数。 |

完整检查点由经营模块产生，驱动不分拆经营相位。`checkpoint` 返回 false 结束本次批量请求；驱动重新读取倍率与 `maxTicks`，按余下现实时间提出下一段请求。回调内改速也结束旧倍率请求；余下现实时间使用新倍率，不把整帧按旧倍率结算。`maxTicks` 返回 0 停止本帧推进；宿主在流程结束后，当前局恢复正常限额，独立局结束受控推进。

日期终点、剩余等待预算、下一操作均由流程提供限额，经营内部再按真实事件拆段。驱动只拥有倍率及未完成 tick，小数进度暂停和改速时保留；日历、订单、生产与工人状态仍由原模块唯一维护。整数推进超过 uint32 请求容量，或经营日历不能接受完整请求时明确拒绝；不截断、不自动换倍率。界面显示真实拒绝并允许玩家调整倍率。

`TestSimulationDriver.RunChecks()` 验证半 tick、暂停不累计、恢复改速、公共/开发允许规则、主动选择来源、途中换速、宿主终点及容量拒绝。表现连接约定见[工人表现](../../world/worker-presentation/interface-worker-presentation.md)，批量结算约定见[批量方案](../../../project/batched-simulation-proposal.md)。
