# WorkerPresentation 对外接口

对应 `scripts/world/WorkerPresentation.cs`。只读工人表现拥有视觉位置、插值进度和当前已消费成功结果；工人任务、移动、农田进度和库存仍由经营模块唯一拥有。

| 成员 | 调用约定 |
| --- | --- |
| `SetGame(game, map, driver)` | 进入节点树后首帧前调用一次；读取三人初始位置，加载 npc001、npc002、npc005，并记住当前经营秒以跳过挂接之前的结果。正式场景传同一局 SimulationDriver；测试可省略并按 1×。 |
| `_Process(delta)` | 新经营秒更新显示段并仅消费该秒有效 Sow/Water 结果；公共倍率同比插值和播放，开发高倍率直接显示最新真实位置、动画按 1×。每帧重验当前作业结果，同秒改种或拆建即取消。 |

WorkerSnapshot.Activity 是调度下一步任务，不能证明作业已经发生。播种/浇水只消费 FarmGame.GetPresentationResults() 与 IsPresentationResultCurrent() 确认的成功记录；每个结果以经营秒、工人编号绑定一次，只在第一次选择时重启动作。真实作业发生时对齐实际工作中心，朝向保留到达方向，不向旧插值位置撒种。合成图已含道具及局部 FX，不额外重叠播放。自动收获和加工结果由设施表现消费。

下一经营秒或批量尾部平静区间覆盖可供新消费的旧记录。公共倍率下，已经开始的片段通过 IsPresentationTargetCurrent 继续验证原设施与工作凭据，不因秒数变化截尾；使用 NpcCharacter.WorkAnimationFinished 的真实引擎完成状态，在最后一帧时长结束后返回待机。新成功结果立即替换在播动作；真实位置离开工作中心或目标改到别田、原凭据失效时立即取消，不延误真实移动。开发高倍率只呈现最后经营秒的有效结果，已跨过期秒的动作也取消；不保存或补播队列。初次挂接跳过旧秒，重复刷新不重复启动。暂停立即对齐真实位置并清除旧移动段，保留动画帧和帧内进度；暂停期间发生拆改仍取消失效动作。恢复不补暂停时间或已经跳过的结果。

移动方向取真实路线当前位置切向经 MapCoordinates.GridPositionToLocal 换算的画面向量；根节点位置经过 WorldMap.GetGridWorldPosition 与父节点 ToLocal 变换。脚根为 64×64 帧内 `(32,60)`，居中 Sprite 上移 28 像素。WorldMap.AttachDepthSorted 将表现放入与设施共用的 YSort 容器；表现自身 YSortEnabled=true，使三名叶节点按各自脚根参加全局排序，人物不设固定前景 ZIndex。

停步播放独立 idle；主场景实际快速位移用 run，预览可比较 walk。角色不自主移动或碰撞，隐藏、屏幕外、帧率、镜头与动画结束均不改变经营结算。

TestWorkerPresentation 验证插值、地图变换、暂停、四种倍率、帧率独立、真正成功动作、同秒改种与拆建取消、重复刷新、批量过期、初次挂接跳过历史。完整片段测试让真实 Godot 动画从首帧走到末两帧时推进经营秒，随后观察播种与浇水各自自然触发 AnimationFinished，确保末帧时长不被截断，并另验开发高倍率跳过旧片段。TestProductionResults 覆盖真实结果与库存、雨水、换季及加工同秒再开关系；图形层级由 TestWorldArt 验证。主要图形验收基准为 1920×1080。

#110 将移动采样统一到调度发布的 `WorkerMovementSnapshot`：只接收相邻完整经营秒、本秒有效的原路径引用与 FromDistance／ToDistance，调用 `LandPath.GetPositionAtDistance` 取当前位置，方向取 `GetDirectionAtDistance`。禁止在该秒两个位置端点间直线插值，因一秒内可以经过多个岸角。每帧用 `FarmGame.IsWorkerMovementCurrent` 重验原目标、版本和任务；同秒改种、拆除或水任务已满足立即停止旧区间。首次挂接、批量跳秒和开发高倍率直接对齐真实位置，不积压历史路线。暂停清区间，恢复只接新经营秒。到达秒仍可沿真实移动区间播放，不能因快照 Activity 已转为 Sowing／Watering 就丢失当秒移动；播种浇水动画继续只消费成功记录。

原生导航就绪后，表现消费同一路线的 double 距离采样，经营仍按整数 tick 记录整趟累计移动。Godot 返回的共线中间节点保留，不额外平滑、吸附或修改路线；当前段方向经等距投影后决定人物四向，集成测试逐帧核对位置与跑步朝向。导航服务器等待只发生在建局，人物不靠物理帧、刚体或动画控制经营位置。
