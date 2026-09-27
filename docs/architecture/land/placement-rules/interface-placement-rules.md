# PlacementRules 放置规则接口

代码位于 `scripts/land/PlacementRules.cs`。`PlacementRules` 是程序集内的只读规则模块，由 `FarmGame.CheckPlacement` 和 `FarmGame.TryPlace` 共用。调用方只需提供目标格、建筑类型、作物；`FarmGame` 从唯一占用表和余额读取当前状态。预检不预留空格，也不冻结余额。

| 结果 | 约定 |
| --- | --- |
| `PlacementCheck` | `Allowed`、`Failure`、`CostCents`；只读预检。成功时给出本次建造费，失败时费用为零。 |
| `PlacementResult` | `Success`、`Failure`、`ChargedCents`、`ErrorMessage`；执行成功才返回实际扣费，失败不扣费。 |
| `LandFailure` | `None`、`InvalidBuilding`、`InvalidCrop`、`OutOfBounds`、`Occupied`、`InsufficientFunds`。先验证描述，再查范围、占用和余额；未知建筑类别不得进入占用表。 |

`TryPlace` 在同步执行时重新调用相同规则，不信任早先的预检；通过后扣款、登记对应生产状态和占用。加工场地建成后立即按原顺序扫描空闲场地领取原料。免费开局建筑用同一规则检查范围、类型和占用，费用为零；满地图基准仍走专用内部夹具。规则不建立道路命令，未来道路只能成为另一种互斥的主要占用类别。

玩家规则见[开局与建筑建造](../../../gameplay/land/opening-and-building.md)；经营入口见[FarmGame 接口](../../game-state/farm-game/interface-farm-game.md)。
