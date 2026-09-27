# NpcCharacter 对外接口

对应类型：`FarmExchange.Characters.NpcCharacter`，代码位于 `scripts/characters/NpcCharacter.cs`；可复用场景为 `scenes/npc_character.tscn`。它只负责角色的移动表现与动画，不决定经营工人的任务、到达时点或生产结果。

| 成员 | 调用约定 |
| --- | --- |
| 场景属性 `CharacterSheet` | 初始角色图集；场景默认使用 `npc_animation_001.png`。 |
| `SetCharacter(sheet)` | 将现有三方向动画的图集切换为指定角色，保留动画帧的规格和当前位置。 |
| `SetMoveDirection(direction)` | 接收二维移动方向并限制最大长度为 1；角色按原示例每秒 180 本地像素移动。零方向停在当前朝向的首帧。 |

图集每帧 64×64 像素，三方向各 6 帧、8 FPS 循环。向下、向左、向上分别取图集第 8、9、10 行；向右镜像向左动画。角色场景不读取键盘，也不持有游戏日历或 `FarmGame`。本轮只由[独立预览](../../ui/npc-preview/interface-npc-preview.md)使用；主地图工人经营移动仍由 #40 后续设计。
