# PlacementRules 放置规则接口

代码位于 `scripts/land/PlacementRules.cs`。`PlacementRules` 是程序集内的只读规则模块，由 `FarmGame.CheckPlacement` 和 `FarmGame.TryPlace` 共用。调用方只需提供锚点格、建筑类型、作物；`FarmGame` 从唯一占用表和余额读取当前状态。预检不预留空格，也不冻结余额。

| 结果 | 约定 |
| --- | --- |
| `PlacementCheck` | `Allowed`、`Failure`、`CostCents`；只读预检。成功时给出本次建造费，失败时费用为零。 |
| `PlacementResult` | `Success`、`Failure`、`ChargedCents`、`ErrorMessage`；执行成功才返回实际扣费，失败不扣费。 |
| `LandFailure` | `None`、`InvalidBuilding`、`InvalidCrop`、`OutOfBounds`、`Occupied`、`InsufficientFunds`。先验证描述，再查完整占地范围、完整占用和余额；合法类型为农田、加工场地、道路，未知类别不得进入占用表。道路没有作物语义，跳过作物检查；两类生产建筑仍要求合法作物。 |

内部 `Check(anchorCell, building, crop, occupancy, balanceCents, costCents)` 接受当前只读余额和经营入口提供的单座费用；完整几何检查委托 [LandOccupancy](../land-occupancy/interface-land-occupancy.md) 的 `CheckFootprint`。生产设施占九格但只按一座收费，道路按一格收费。部分越界先于范围内冲突返回 `OutOfBounds`，任一子格已有设施时返回 `Occupied`；失败费用均为零。

`TryPlace` 在同步执行时重新调用相同规则，不信任早先的预检；通过后按 `FarmGame.GetBuildingCostCents(building)` 的统一类型价格扣款、在锚点登记一份生产状态并登记完整占地。道路只写占用，不建立生产状态、不触发加工领取；加工场地建成后立即按稳定锚点顺序扫描空闲场地领取原料。免费开局建筑用同一规则检查范围、类型和占用，费用为零；满地图基准仍走专用内部夹具。

公开价格查询只接受三种合法类型，`None` 或未定义值抛出 `ArgumentOutOfRangeException`；玩家放置命令先验证类型，再查询价格，因此非法描述正常返回 `InvalidBuilding` 且零修改，不将未知类型解释成免费建筑。预检和执行返回的失败费用为零，仅表示此次没有收费。

玩家规则见[开局与建筑建造](../../../gameplay/land/opening-and-building.md)；经营入口见[FarmGame 接口](../../game-state/farm-game/interface-farm-game.md)。
