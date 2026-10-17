# WorldMap 对外接口

对应类型：FarmExchange.World.WorldMap，代码位于 scripts/world/WorldMap.cs。模块负责地图表现、视口与节点坐标变换、已决定的格子选框及镜头范围，不保存或推进经营。等距公式由[MapCoordinates](../map-coordinates/interface-map-coordinates.md)提供。

| 成员 | 输入与输出 | 调用约定 |
| --- | --- | --- |
| SetGame | FarmGame、可选同局SimulationDriver | 地图挂树后初始化一次；独立场景省略driver按1×，初次绑定不回播已有结果 |
| SyncFromGame | 无参数 | 每个成功经营命令或tick结束后调用；读取快照更新外观并按实际完整占地永久清除环境根点，拆建之间不可跳过同步 |
| AttachDepthSorted | 尚未挂树的Node2D表现容器 | SetGame后调用；设零位置并启用嵌套YSort，子节点原点为脚根，不另设ZIndex；地图负责生命周期 |
| SelectCell | 已决定的基础格 | 只落实普通选框，保留原点击子格；地图外保持原选择，不发送输入或经营事件 |
| ScreenToCell | 逻辑视口坐标→基础格 | 返回未限制格坐标，使越界候选完整显示 |
| UpdatePlacementPreview | 建筑类型、作物、锚点、可选BuildingOrientation（默认Q0） | 只读更新完整占地、局部冲突、半透明实例图及外围，不更新地面块 |
| ClearPlacementPreview | 无参数 | 隐藏预览，不改变摆放生命周期或经营资源 |
| UpdateRoadPreview | 完整候选路径、Land.RoadMaterial、removing（默认false） | 只读筛选空闲有效新路或实际待拆路；候选和旧路共同计算四邻连接，冲突格仅提示 |
| ClearRoadPreview | 无参数 | 恢复旧路实际连接并清除道路提示；不更改Main持有的当前段 |
| PlacementPreviewAnchor | 可空格坐标 | 查询实际候选锚点，隐藏时为空 |
| GetCellWorldCenter | 有效格→全局位置 | 在地图本地格中心应用节点变换 |
| GetGridWorldPosition | 分数格→全局位置 | 同一等距换算，不取整，用于工人视觉插值 |
| ClampGlobalCameraCenter | 全局镜头中心→受限位置 | 限制在同一地图菱形 |
| LocalBounds | 本地外接矩形 | 用于地图范围检查，不表示全局矩形 |

WorldMap通过`FarmGame.GetBuildingSpaces()`读取包含固定实例朝向的稳定空间快照，每实例只读取一次`GetPlot(anchor)`；`GetBuildingSpace(anyCell)`解析整座占地。Sync次数、镜头位置或可见数量不得影响作物、加工、库存、日期或任务。暂停也可同步建造/改种，倍率只改变经营快照到来的时刻，不改变视觉档边界。

内部按稳定不可变空间快照的引用判断占地是否变化；引用未变时跳过道路快照投影、设施删除集合和环境占地清理。生产快照、湿土、生长档、加工启停和可见局部动效仍在每次同步刷新，镜头变化仍即时更新可见性。引用不是第二份占地状态，建拆仍只由经营登记；同数量拆建或同锚点替换同样触发完整空间同步。原有土层与表层显隐缓存只在真正变化时写节点属性，q0局部动效替换的表层开关继续独立控制。

主场景通过`AttachDepthSorted`挂接[WorkerPresentation](../worker-presentation/interface-worker-presentation.md)。中间人物容器启用嵌套YSort，实际人物脚根参与共同排序；土层固定在人物之下，q0三种动效作物每株按根点排序，其余静态整田与建筑按工作中心排序。人物位置和完成时机仍由调度拥有。完整层级、静态粒度及跨块可见性见[实现](implementation-chunk-cache.md)。

## 实际外观与交互

天气读取同局 `FarmGame.ActiveWeatherRegions` 的当前真实矩形快照，内部 `WeatherPresentation` 只负责可见范围内的灰蓝云影、稀疏斜雨线和短落地效果。默认晴天沿用原画面；雨水后的农田仍由原有 `HasWater` 和湿土图片表现。天气绘制在世界节点内，独立 UI 的 `CanvasLayer` 不受染暗；选框和候选冲突层继续位于天气上方。暂停冻结视觉时间，0.5×/1×/2×跟随公共倍率，高开发倍率仅按1×表现且雨点数量有上限。绘制不推进经营、不执行供水、不消耗天气或产量随机数，不展示未来事件。细节及独立验收见[天气表现实现](implementation-weather-presentation.md)。

地表根据真实地形显示草／土／水，32类环境按固定独立种子选择q0～q3静态原图；干湿土、通用播种、七作物三档与七加工设施按实例只读 `Orientation` 选择清亮v2 q0～q3原图。所有农田图层取同一q，画布和pivot在四向均保持一致。q0小麦/甘蔗/萝卜生长层采用原包独立植株和固定根点风摆，q0磨坊/制糖坊采用拆分底图与局部活动层；其余作物/加工及全部q1～q3保持各自静态原图。包内只提供q0动效，不伪造其他方向动效。Growing按精确`GrowthProgress`等分三档，不用向上取整的RemainingSeconds反算；成熟自动收获，没有新增待采收状态。None无作物层，种类仍在详情里显示；HasWater独立控制土层。道路读取登记时五材质、四邻mask及稳定v0～v3，保留64×32完整原图；与建筑朝向及铺设顺序无关。原图画布和pivot对齐工作中心，选取与费用仍由3×3/1×1逻辑占地定义。

`Main`唯一持有建造类型、候选生命周期、整体CheckPlacement结果与费用文字。鼠标处于地图且不拖动时，每帧用ScreenToCell获取候选；离窗、界面遮挡、拖动或取消即隐藏。经营入口实际提交时重验，预览不保存成功承诺。

候选通过同一BuildingFootprint偏移生成占地、逐格阻塞及外围边；空闲格淡绿，真实占用、含水或越界格红，金币不足只影响文字。候选越界保留全部格、不自动移位。农田使用候选朝向的干土图，加工使用真实品种和候选朝向的建筑图，兼容单格道路候选使用土路孤立图；图半透明且位于真实对象前，之后叠加逐格颜色与外围，使图片不能挡住冲突反馈。小环表示鼠标锚点。朝向只改变候选选图，不改变锚点、占地、冲突和工作中心；非法枚举抛出 `ArgumentOutOfRangeException`，不改成Q0。相同候选外观不重复重绘，悬停/清除不调用全图同步或修改经营。

道路工具使用UpdateRoadPreview传完整范围。地图内完整陆地真实空格才参与有效新路连接，候选冲突格不覆盖正式路；已有路保持材质并临时呈现建后mask。拆除只排除范围内实际道路，待拆格红色半透明、保留旧路呈现拆后mask。纹理在地面层绘制，逐格绿/红提示独立覆盖。移动/隐藏/ClearRoadPreview撤销原候选四邻影响，仅刷新状态实际变化的缓存块；跨8×8块同样处理。预览不修改土地、资源、环境或经营，工具当前段仍由Main持有；正式提交使用FarmGame并SyncFromGame。拓扑、资源及独立图形验收入口见[道路实现](implementation-road-appearance.md)。

普通短按由 Main 解析原点击子格并决定选择后，再交给 SelectCell 显示选框；详情通过经营入口解析同实例；金色选框按占地外边完整绘制，拆除后恢复为选中空小格。选框独立于地面缓存并位于设施/人物上方。地图位置转换及玩家操作见[地图规则](../../../gameplay/world/map-and-camera.md)。

地图不持有工具模式，也不通过选框通知触发建造。`CameraController` 只发布已接受短按；`Main` 统一分派普通选择、生产或道路。`SelectionChanged` 与 `SelectAtScreenPosition` 的旧双事件输入协议已移除；普通选择调用 `ScreenToCell` 后使用 `SelectCell`，工具分支直接处理自身意图。独立地图表现用例仍可显式设置选框，并检查实际子格、完整外围与重绘；经营／UI连接测试经正式镜头输入进入同一分派。

正式建筑选框与候选预览共用内部 `PlacementPreviewGeometry.GetCellOuterEdges`：以土地定义的完整偏移集合排除内部相邻边，返回未经地图边界裁切的本地线段。两条绘制路径各自保留颜色、线宽、显示时机和缓存；正式选框仍为3像素金线，候选外围仍为2像素且逐段沿用所属格的绿／红冲突颜色。此共享计算不改变占地、子格选择、费用或候选生命周期，具体调用见[实现说明](implementation-chunk-cache.md#占地外围几何)。

## 验收入口

`TestWorldMap`验证统一坐标、当前3×3农田／加工和1×1道路及既有2×4夹具的共用外围几何，检查边数、基础格边长、重复边和内部边排除。`TestRoadMap`验证道路跨块、非三格对齐两座生产设施各九子格的整座选择/改种/整体拆除、相邻设施保持与实际完整金框，并对照未选中底图确认内部边不受金框影响；新纹理按实际PNG核对。`TestPlacementPreview`验证三类型候选的真实外围线、局部冲突和完整越界的逐段绿／红颜色、资源零修改、不重建地面块、镜头平移/缩放/拖动和隐藏。

`TestWorldArt`经真实经营得到七作物三个档位、待水、干湿土与七建筑；有窗口时比较实际像素，验证嵌套人物容器的前后建筑遮挡与根点离屏的屋顶保留。独立图形验收至少1920×1080，截图与运行方式见[实现说明](implementation-chunk-cache.md#验收)。

## 动效、成功结果与环境同步

WorldMap唯一持有表现缓存、环境剩余落点、最近消费经营秒与可见实例的短结果。内部FacilityMotion仅接收真实快照、当前视觉时长及已确认结果，不访问库存、钱包或调度。公共0.5/1/2倍率跟随同局Driver，高开发倍率采用与人物一致的1×视觉；暂停传零时长，经营倍率不受影响。只有真实Processing驱动磨坊叶片及制糖坊蒸汽，待料停止发射并冻结叶片，已有蒸汽自然结束。

每个新经营秒至多读取一次`GetPresentationResults`，只播放Harvest/Product，并用`IsPresentationResultCurrent`确认新结果仍有效；已开始短效果用`IsPresentationTargetCurrent`验证原实例，失效立即清除。不会将批量推进汇总变成逐条积压播放，不推测搬运、采收工人或额外产量。初始历史不播放、离屏不保存待播队列，重进视野不重放；短效果只存在于可见实例。改种、拆除及同锚点重建必须及时调用SyncFromGame，让实例外观和原结果共同失效。

环境初始化使用独立固定视觉种子，不消费开局作物随机数。每次SyncFromGame对真实BuildingSpaceSnapshot的完整Footprint.Offsets清除环境根点，包含开局免费设施；预览、失败建造和选择不产生新占地，因此不清环境。已清根点永久删除，拆除不复生；视野开关只释放/恢复剩余根点的绘制节点。环境没有碰撞、正式占地、费用或产物。分布参数及32类采用表见[环境实现](implementation-environment-decoration.md)，局部动效层级与根点见[动效实现](implementation-bright-motion.md)。

ViewChanged 以既有 ChunkRedrawCount（MapChunk._Draw 实际执行）计算本次观察区间的 RebuiltChunkCount，QueueRedraw 调度不计作已重建。Main 读取该累计和地图本地镜头中心，由日志模块去重；地图不维护诊断预算或日志对象状态。

#111新增的 `TestFacilityOrientations` 由 `TestWorldArt` 调用，经真实带朝向建造核对q1～q3七建筑、七作物三档、干湿土/播种同q与pivot；headless观察真实节点及加载，有窗口核对来源原图的不透明像素并保存1080P截图。`TestPlacementPreview`核对四次候选方向切换回q0、半透明屋顶原图、资源和锚点不变。环境测试同时核对四向随机分布、不同开局作物种子及镜头往返方向稳定。

## 水域表现验收

地表读取本局 `Terrain.GetCell(cell).TernaryCode`，映射同一 81 组合 × 三变体 mesh atlas。水纹经原包 water mask 裁剪，只影响水面颜色，不修改岸线或供水。水纹读取同局暂停与完整倍率，开发 16× 同样按 16×；地图块共享世界坐标相位。环境落根限于完整陆地，普通与道路新建候选统一将含水格显示为冲突；含水候选不参与道路临时连接。

`TestWaterPresentation` 使用正式含水配置，核验近景存在、普通与道路候选一致拒绝含水格、真实绕水路线在一秒内转角的采样、暂停对齐、四倍率水纹时间输入与批量跳秒。`TestWaterFps` 为独立真实 1920×1080 场景，保留默认镜头和 Main/UI，检查近景落点及真实界面遮挡、暂停状态，记录三工人跨 48 池塘地图长途路线的 1×／2×／0.5×／16× 图形负载。公共倍率每档预热 2 秒、采样 3 秒；16× 预热 0.5 秒、采样 2 秒，前后都要求三人处于含转角真实行程，要求平均至少 60 FPS、P95 不超过 16.67 ms；截图和 JSON 在 `build/issue110-validation/water-performance/`。该采样不是完整行程验证；它与显式纯陆地的原 16,384 实例满图基准分别执行。

真实水纹输出由 `TestWaterFps` 内的 `TestWaterRippleRendering` 验收。它用 1920×1080 独立 SubViewport 显示正式 WorldMap 地表，隐藏人物、设施、装饰和天气，直接读回 GPU 渲染帧。按原水 mask 的稳定内部区域分别采样三变体水面、岸格干地和完整陆地；四倍率下每个变体均要求至少 10 个水面探针实际变化、两类干地零变化，暂停后整帧字节一致。原 mask 以 alpha 裁剪，避免 PNG 透明边 RGB 扩散影响水域。水纹空间相位与三变体格序独立，仍在所有地图块共用，不会因纹理变体而永久漏绘。

每档检查在断言前保存实际前后和暂停帧，输出 `ripples-pixels.json`、`ripples-pixels-<倍率>-probes.json` 及 `ripples-isolated-*.png` 到同一验收目录。报告记录实际 shader／纹理绑定、shader 哈希和 uniform 时间；探针记录包括 atlas／mask UV、alpha 与真实 GPU RGBA，保留每变体／分类的代表样本及全部变化点。中途失败的报告明确标为未完成，不冒充四档通过。headless 仅验证时间输入和状态，不执行或冒充像素门禁；快 shader 诊断同样不代替有窗口 `test_water_fps.tscn` 的正式门禁。空间相位与快复现说明见[地面与水纹实现](implementation-chunk-cache.md#静态水域与共享水纹)。

FPS 的普通门槛失败与场景异常均保存 `failure.json`，仍保留全部已完成矩阵结果和各档 `rate-<倍率>.png`。普通失败继续完成矩阵，报告逐项记录实际失败倍率、阶段、原测量事实和对应截图；`failure.png` 在首个失败倍率的测量末帧从其原截图复制，报告注明来源，后续成功或失败阶段不会替代它。异常若发生在已有普通失败之后，额外保存实际 `exception.png` 并记录异常阶段，首个 FPS 失败图继续保留。没有降低平均 FPS、P95、真实推进或三名绕水工人的任何门槛。

道路偏好图形用例也由 `TestWaterFps` 通过同一正式含水局执行：在中心保护区真实建造 17 格 U 形土路及一座小麦农田，等待 `FarmGame.WaitForNavigationAsync` 后比较同一起终点的原生路线，并实际推进 Main 驱动，检查真实工人行程、道路格内位置和 `WorkerPresentation` 人物节点位移。拆路后再次等待正常同步，核对路线恢复；地图只响应已提交占地，不保存导航成本或重规划状态。用例保留默认镜头下的 `roads-without-before.png`、`roads-with-before-moving.png`、`roads-with-moving.png`、`roads-without-after.png` 及逐阶段 `road-preference.json`，异常报告同时保留已完成的道路事实。该段是图形行为验收，随后拆除道路与目标田，再执行原三工人跨图四倍率负载。后续三座田的费用来自该田实际成熟后出售小麦的收入，不注入测试金币。2026-10-14 保留新合入修复后的组合已重新通过正式有窗口运行：`build/issue110-road-preference/merged-final-water-fps.process.json` 记录实际退出码 0、完整运行 57.7288 秒、原生导航警告 0；同次 `water-performance/performance.json` 记录 Main 完整准备 26.1047 秒，四倍率平均 FPS 为 577.2887～593.0036、P95 不超过 2.331 ms，三名工人在各档采样全程保持移动。U 路真实工人按每经营秒 3 格采样，拆路后同端点恢复直线；目标田实际产出 12 份小麦并出售收入 3,036 分，提供后续三座远途田的真实费用。同次道路绘制专项的 `build/issue110-road-preference/merged-final-road-fps.process.json` 记录实际退出码 0、完整运行 23.9285 秒、原生导航警告 0，平均 417.493 FPS、P95 为 3.438 ms，继续通过原门槛。以上为新组合重测，原始导航的历史基线另见实现文档。
