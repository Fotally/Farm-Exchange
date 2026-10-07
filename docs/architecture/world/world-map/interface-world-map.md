# WorldMap 对外接口

对应类型：FarmExchange.World.WorldMap，代码位于 scripts/world/WorldMap.cs。模块负责地图表现、屏幕输入与节点坐标变换、格子选择及镜头范围，不保存或推进经营。等距公式由[MapCoordinates](../map-coordinates/interface-map-coordinates.md)提供。

| 成员 | 输入与输出 | 调用约定 |
| --- | --- | --- |
| SetGame | FarmGame、可选同局SimulationDriver | 地图挂树后初始化一次；独立场景省略driver按1×，初次绑定不回播已有结果 |
| SyncFromGame | 无参数 | 每个成功经营命令或tick结束后调用；读取快照更新外观并按实际完整占地永久清除环境根点，拆建之间不可跳过同步 |
| AttachDepthSorted | 尚未挂树的Node2D表现容器 | SetGame后调用；设零位置并启用嵌套YSort，子节点原点为脚根，不另设ZIndex；地图负责生命周期 |
| SelectAtScreenPosition | 屏幕坐标 | 有效格发出SelectionChanged(cell)，保留原点击子格 |
| ScreenToCell | 逻辑视口坐标→基础格 | 返回未限制格坐标，使越界候选完整显示 |
| UpdatePlacementPreview | 建筑类型、作物、锚点 | 只读更新完整占地、局部冲突、半透明实例图及外围，不更新地面块 |
| ClearPlacementPreview | 无参数 | 隐藏预览，不改变摆放生命周期或经营资源 |
| PlacementPreviewAnchor | 可空格坐标 | 查询实际候选锚点，隐藏时为空 |
| GetCellWorldCenter | 有效格→全局位置 | 在地图本地格中心应用节点变换 |
| GetGridWorldPosition | 分数格→全局位置 | 同一等距换算，不取整，用于工人视觉插值 |
| ClampGlobalCameraCenter | 全局镜头中心→受限位置 | 限制在同一地图菱形 |
| LocalBounds | 本地外接矩形 | 用于地图范围检查，不表示全局矩形 |

WorldMap通过`FarmGame.GetBuildingSpaces()`读取稳定空间快照，每实例只读取一次`GetPlot(anchor)`；`GetBuildingSpace(anyCell)`解析整座占地。Sync次数、镜头位置或可见数量不得影响作物、加工、库存、日期或任务。暂停也可同步建造/改种，倍率只改变经营快照到来的时刻，不改变视觉档边界。

主场景通过`AttachDepthSorted`挂接[WorkerPresentation](../worker-presentation/interface-worker-presentation.md)。中间人物容器启用嵌套YSort，实际人物脚根参与共同排序；土层固定在人物之下，三种动效作物每株按根点排序，其余静态整田与建筑按工作中心排序。人物位置和完成时机仍由调度拥有。完整层级、静态粒度及跨块可见性见[实现](implementation-chunk-cache.md)。

## 实际外观与交互

固定使用清亮v2 q0草地、干湿土、通用播种、七作物三档、七加工设施与32类环境。小麦/甘蔗/萝卜生长层采用原包独立植株和固定根点风摆，磨坊/制糖坊采用拆分底图与局部活动层；其余作物/加工保持静态原图。Growing按精确`GrowthProgress`等分三档，不用向上取整的RemainingSeconds反算；成熟自动收获，没有新增待采收状态。None无作物层，种类仍在详情里显示；HasWater独立控制土层。道路保持原灰色路面。原图画布和pivot对齐工作中心，选取与费用仍由3×3/1×1逻辑占地定义。

`Main`唯一持有建造类型、候选生命周期、整体CheckPlacement结果与费用文字。鼠标处于地图且不拖动时，每帧用ScreenToCell获取候选；离窗、界面遮挡、拖动或取消即隐藏。经营入口实际提交时重验，预览不保存成功承诺。

候选通过同一BuildingFootprint偏移生成占地、逐格阻塞及外围边；空闲格淡绿，真实占用或越界格红，金币不足只影响文字。候选越界保留全部格、不自动移位。农田使用干土图，加工使用真实品种q0建筑图，道路用灰线；图半透明且位于真实对象前，之后叠加逐格颜色与外围，使图片不能挡住冲突反馈。小环表示鼠标锚点。相同候选外观不重复重绘，悬停/清除不调用全图同步或修改经营。

选中任一生产子格仍发出原格坐标，由经营入口解析同实例；金色选框按占地外边完整绘制，拆除后恢复为选中空小格。选框独立于地面缓存并位于设施/人物上方。地图位置转换及玩家操作见[地图规则](../../../gameplay/world/map-and-camera.md)。

## 验收入口

`TestWorldMap`保留统一坐标与不同规格预览外围几何检查。`TestRoadMap`验证道路跨块、非三格对齐农田的九子格选择/改种/整体拆除、相邻设施保持与完整选框；新纹理按实际PNG核对。`TestPlacementPreview`验证三类型候选、局部冲突、完整越界、资源零修改、不重建地面块、镜头平移/缩放/拖动和隐藏。

`TestWorldArt`经真实经营得到七作物三个档位、待水、干湿土与七建筑；有窗口时比较实际像素，验证嵌套人物容器的前后建筑遮挡与根点离屏的屋顶保留。独立图形验收至少1920×1080，截图与运行方式见[实现说明](implementation-chunk-cache.md#验收)。

## 动效、成功结果与环境同步

WorldMap唯一持有表现缓存、环境剩余落点、最近消费经营秒与可见实例的短结果。内部FacilityMotion仅接收真实快照、当前视觉时长及已确认结果，不访问库存、钱包或调度。公共0.5/1/2倍率跟随同局Driver，高开发倍率采用与人物一致的1×视觉；暂停传零时长，经营倍率不受影响。只有真实Processing驱动磨坊叶片及制糖坊蒸汽，待料停止发射并冻结叶片，已有蒸汽自然结束。

每个新经营秒至多读取一次`GetPresentationResults`，只播放Harvest/Product，并用`IsPresentationResultCurrent`确认新结果仍有效；已开始短效果用`IsPresentationTargetCurrent`验证原实例，失效立即清除。不会将批量推进汇总变成逐条积压播放，不推测搬运、采收工人或额外产量。初始历史不播放、离屏不保存待播队列，重进视野不重放；短效果只存在于可见实例。改种、拆除及同锚点重建必须及时调用SyncFromGame，让实例外观和原结果共同失效。

环境初始化使用独立固定视觉种子，不消费开局作物随机数。每次SyncFromGame对真实BuildingSpaceSnapshot的完整Footprint.Offsets清除环境根点，包含开局免费设施；预览、失败建造和选择不产生新占地，因此不清环境。已清根点永久删除，拆除不复生；视野开关只释放/恢复剩余根点的绘制节点。环境没有碰撞、正式占地、费用或产物。分布参数及32类采用表见[环境实现](implementation-environment-decoration.md)，局部动效层级与根点见[动效实现](implementation-bright-motion.md)。
