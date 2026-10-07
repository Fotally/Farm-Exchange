# NpcCharacter 对外接口

对应 `FarmExchange.Characters.NpcCharacter`，脚本 `scripts/characters/NpcCharacter.cs` 与场景 `scenes/npc_character.tscn`。角色只拥有朝向、动画及独立预览的移动表现，不决定经营任务或生产结果。

| 成员 | 调用约定 |
| --- | --- |
| `CharacterFrames` | 场景初始 SpriteFrames，默认 npc001；每角色包含 idle/walk/run/sow/water 四向共 20 个片段。 |
| `SetCharacter(characterId)` | 加载实际来源编号对应资源；20 位目录由预览维护，主场景保留 001、002、005。无旧图集兜底。 |
| `WorkAnimationFinished` | 当前单次作业片段自然播完的只读视觉结果；由引擎 AnimationFinished 设置，新片段或显式重启清除，不决定经营。 |
| `SetMoveDirection(direction, action=Run)` | 独立预览按长度不大于一的方向移动，仍为 180 本地像素/秒；静止使用独立待机，播种/浇水可以静态检视。 |
| `ShowAt(localPosition, direction, moving, paused, animationRate=1, workAction=null, restartAction=false)` | 首次调用禁用自主物理和碰撞，根节点是外部脚根位置。零方向保持朝向；真实位移默认跑步，无位移待机；仅调用方已验证的成功作业传入 Sow/Water，新结果首次传 restartAction=true。后续同动作不重启。暂停保留帧和帧内进度，恢复继续。 |

资源来自完整清亮 v2 2.0.0，PNG 原样保留。每帧 64×64，四行依次 down/left/up/right；右向已经烘焙，不再镜像。居中 Sprite 的位置为 `(0,-28)`，使画布脚根 `(32,60)` 与角色根一致。根节点加入地图共同 YSort 层，人物与建筑按脚根深度遮挡；不靠固定 ZIndex 盖住建筑。

SpriteFrames 以速度 1 配合每帧 duration 保存源秒数；idle 为 8 帧，walk/run 各 6 帧，sow/water 各 8 帧。移动片段 run 的 12 FPS 对应现有每秒三基础格的快速位移，walk 的 8 FPS 保留给预览；没有改经营速度。播种/浇水合成图已有工具及撒种/水滴，不另叠道具或共享 FX。两作业片段改为单次，其余循环；逐帧时长不变，新结果或取消可切断当前片段，绝不驱动经营回调。

公共 0.5×/1×/2× 由调用方同比设置动画速度，开发高倍率传 1 并选择最新有效结果。动作消费、过期取消和去重由[工人表现](../../world/worker-presentation/interface-worker-presentation.md)负责。

TestNpcPreview 核验全部 20 位角色、20 个片段、四行切片、逐帧时长、脚根、独立待机、烘焙右向、动作键及暂停进度。20 位源 JSON 的五动作帧数与逐帧秒数一致；Godot GetFrameDuration 返回 Single，测试将来源秒数转为同一 Single 表示后严格相等比较，不用 double 近似阈值误判浮点表示，也不增加额外容差。失败分别报告角色资源、动作方向、帧序号与实际时长/切片/尺寸。TestWorkerPresentation 验证真实主场景动作。不引入 carry、harvest、process 业务。来源与授权限制见[人物素材记录](../../../../assets/gameplay/npc/source_notes.md)。
