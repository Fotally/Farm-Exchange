# NpcCharacter 对外接口

对应类型：`FarmExchange.Characters.NpcCharacter`，代码位于 `scripts/characters/NpcCharacter.cs`；可复用场景为 `scenes/npc_character.tscn`。它只负责角色的移动表现与动画，不决定经营工人的任务、到达时点或生产结果。

| 成员 | 调用约定 |
| --- | --- |
| 场景属性 `CharacterSheet` | 初始角色图集；场景默认使用 `npc_animation_001.png`。 |
| `SetCharacter(sheet)` | 将现有三方向动画的图集切换为指定角色，保留动画帧的规格和当前位置。 |
| `SetMoveDirection(direction)` | 接收二维移动方向并限制最大长度为 1；角色按原示例每秒 180 本地像素移动。零方向停在当前朝向的首帧。 |
| `ShowAt(localPosition, direction, moving, paused)` | 外部快照驱动模式：按父节点本地坐标设置位置，方向只用于朝向；`moving` 决定是否播放走路动画，停步保留朝向首帧。暂停保持当前动画和帧，仅暂停播放。首次调用停用自主物理移动与碰撞，此实例随后由外部持续定位，不使用预览速度或碰撞决定到达。 |

图集每帧 64×64 像素，三方向各 6 帧、8 FPS 循环。向下、向左、向上分别取图集第 8、9、10 行；向右镜像向左动画。角色场景不读取键盘，也不持有游戏日历或 `FarmGame`。[独立预览](../../ui/npc-preview/interface-npc-preview.md)继续使用原方向控制与物理移动；[工人表现](../../world/worker-presentation/interface-worker-presentation.md)仅使用外部定位，不依赖动画完成回调。

`TestNpcPreview` 保留 20 位角色切换和原三方向预览回归，另检查外部模式的朝向、物理停用、暂停帧保持与停步首帧。两种实际调用方式共用同一图集与动画 Implementation，不增加角色策略或第二套图集解析。
