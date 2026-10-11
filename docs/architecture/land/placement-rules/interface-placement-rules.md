# PlacementRules 放置规则接口

代码位于 `scripts/land/PlacementRules.cs`。`PlacementRules` 是程序集内的只读规则模块，由 `FarmGame.CheckPlacement` 和 `FarmGame.TryPlace` 共用。调用方提供锚点格、建筑类型、作物与默认 q0 的候选朝向；`FarmGame` 从唯一占用表和余额读取当前状态。预检不预留空格，也不冻结余额。

| 结果 | 约定 |
| --- | --- |
| `PlacementCheck` | `Allowed`、`Failure`、`CostCents`；只读预检。成功时给出本次建造费，失败时费用为零。 |
| `PlacementResult` | `Success`、`Failure`、`ChargedCents`、`ErrorMessage`；执行成功才返回实际扣费，失败不扣费。 |
| `LandFailure` | `None`、`InvalidBuilding`、`InvalidCrop`、`OutOfBounds`、`Occupied`、`InsufficientFunds`，追加 `InvalidOrientation` 保留原值。先验证类型和生产设施作物，再查朝向、完整占地范围、完整占用和余额；合法类型为农田、加工场地、道路，未知类别不得进入占用表。道路没有作物语义，跳过作物检查；两类生产建筑仍要求合法作物。生产设施只接受 q0～q3，道路只接受 q0，其他值正常返回 `InvalidOrientation` 与“无效建筑朝向”。 |

内部 `Check(anchorCell, building, crop, occupancy, balanceCents, costCents, orientation=Q0)` 接受当前只读余额和经营入口提供的单座费用；朝向和完整几何检查委托 [LandOccupancy](../land-occupancy/interface-land-occupancy.md) 的 `CheckFootprint`。生产设施占九格但只按一座收费，道路按一格收费；旋转没有额外费用。部分越界先于范围内冲突返回 `OutOfBounds`，任一子格已有设施时返回 `Occupied`；失败费用均为零。

`TryPlace` 在同步执行时重新调用相同规则，不信任早先的预检；通过后按 `FarmGame.GetBuildingCostCents(building)` 的统一类型价格扣款、在锚点登记一份生产状态并登记完整占地。道路只写占用，不建立生产状态、不触发加工领取；加工场地建成后立即按稳定锚点顺序扫描空闲场地领取原料。免费开局建筑用同一规则检查范围、类型和占用，费用为零；满地图基准仍走专用内部夹具。

公开价格查询只接受三种合法类型，`None` 或未定义值抛出 `ArgumentOutOfRangeException`；玩家放置命令先验证类型，再查询价格，因此非法描述正常返回 `InvalidBuilding` 且零修改，不将未知类型解释成免费建筑。预检和执行返回的失败费用为零，仅表示此次没有收费。

玩家规则见[开局与建筑建造](../../../gameplay/land/opening-and-building.md)；经营入口见[FarmGame 接口](../../game-state/farm-game/interface-farm-game.md)。

PlacementDiagnostics 只由实际 TryPlace 命令重验后调用，接收原请求朝向与同次 CheckPlacementCore 返回的稳定失败码，按现有短路顺序投影已执行的 Building/Crop/Orientation/Bounds/Occupancy/Funds；非法建筑早拒只记录 Building=false，道路不补造作物检查，非法朝向后不投影范围、占用或余额。拒绝后不补查后续项。显式 Cells 坐标范围命中后才构造字段。公开 CheckPlacement 与实际执行共享同一私有规则入口，纯预检不输出 RuleChecked 或命令，不消耗执行诊断预算；实际重验只执行一次，RuleChecked 也不等于最终建造已经成功。
