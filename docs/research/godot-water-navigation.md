# Godot 原生二维水域导航调研

> 2026-10-14 决策更新：采用 Godot 原生导航、1/32 格保守简化、额外离岸 1/8 格及原有 1/256 格数值余量；道路优先也已纳入本期。现行范围见[水域实施方案](../project/water-terrain-proposal.md)，道路成本与正常同步约定见[地形 Interface](../architecture/terrain/interface-terrain-map.md)。下文保留各阶段实验，不能将分块烘焙或旧参数的时间当作最终完整开局时间。

## 完整地图的分块实验（2026-10-13）

恢复新 `dev` 后，种子0的正式生成得到217,137个权威岸点，Round处理后为286,125点；生成约17.8秒。整图默认凸分区及三角分区在本机长时间未完成烘焙，均明确停止，不能算通过。引擎RDP在1/4096格误差预算下减至129,706点，烘焙仍未及时完成；没有将该简化接入正式导航。

采用 [Godot 官方分块示例](https://github.com/godotengine/godot-demo-projects/blob/master/2d/navigation_mesh_chunks/navmesh_chunks_demo_2d.gd)的 BakingRect、BorderSize 和边界对齐方式，保留原始Round轮廓，16格一块共576块。指定引擎实测总烘焙约14～15秒，最慢块约0.17秒；不通过扩大边缘连接距离拼接道路。区域默认异步同步，因此两帧后地图虽有iteration，所有查询仍可能为空。等待全部576区域实际同步后，第5物理帧得到完整地图，七条同块、跨块、近景及四角路径均到达真实目标并通过原岸线圆净空检查。

显式使用与正式相同的 A*／Corridorfunnel 后，近景路线1534点、查询17.3ms，但朴素全岸段核验约1112ms；四角查询31～55ms、核验22～2145ms。点数不能直接当可见转角数，真实观感仍须1080P检查。岸段空间筛选已接入，后续顺序调整为先排除净空误判、再评估保守简化；业界依据见[0 A.D. 与分块优化](water-navigation-game-practices.md)。

这些是局部实验，不是正式全图验收；全部合法工作中心的连通、可取消准备、100次连续改派、场景及图形性能仍须在接入后验证。日志保存在 `build/issue110-recovery/` 的 `terrain-timing`、`terrain-triangulate`、`simplify-probe`、`chunk-sync` 与 `funnel-timing` 文件中。

### 净空误报与保守简化第一阶段（2026-10-14）

实际失败点距原岸线超过2格，原生 `IsPointInPolygon` 却将其中一点报告为水内。将5,064点的原轮廓缩减到6点后仍能复现；按固定引擎源码重放射线，可见射线命中共享顶点时相邻两条岸边分别计数，导致奇偶判断反转。平移、缩放都没有消除这个复现，因此不能仅归因于坐标太大或轮廓太密。[固定版本的多边形射线判定](https://github.com/godotengine/godot/blob/ed1daf0bf/core/math/geometry_2d.h#L351-L382)

修复先检查圆脚底距岸，再用原生 Clipper 与圆内方框判断真实起终点的水内外；距岸已满足半径时，方框不会跨岸，仅用于分类。中间折线仍检查地图界限与完整扫圆，不改变脚底、岸线或1/256格余量。最小复现的两个陆地点、真实水内点、岸上点、距岸不足点和原生闭三角形边界检查全部通过；实际全图路线回归另行运行。证据为 `build/issue110-recovery/clearance-minimal.json`、`clearance-rays.json` 和 `clearance-clipper-regression.json`。

同一正式种子0的217,137个原岸点，用 Godot `NavigationServer2D.SimplifyPath` 执行RDP，再用Round补偿外扩。每座池塘均以 `ClipPolygons(原水域, 补偿障碍)` 的空结果验证原水域被完整覆盖，不把RDP本身当保守性保证。随后按原脚底半径加原数值余量外扩，结果如下：

| 实验RDP容差（格） | 补偿外扩（格） | RDP后点数 | 最终障碍点数 | 几何预处理（毫秒） | 抽样最大距原岸（格） |
| --- | ---: | ---: | ---: | ---: | ---: |
| 原始基准 | 0 | 217,137 | 286,125 | 596.0 | 0.12893 |
| 1/256 | 1/256＋1/4096 | 27,133 | 83,035 | 233.1 | 0.13564 |
| 1/64 | 1/64＋1/4096 | 10,570 | 40,843 | 243.0 | 0.14720 |
| 1/32 | 1/32＋1/4096 | 4,148 | 21,857 | 176.9 | 0.16713 |
| 1/16 | 1/16＋1/4096 | 3,156 | 20,153 | 152.9 | 0.20981 |

五档的48座池塘均通过原水域完整包含检查。预处理不含17.4秒原地形生成，也不含烘焙、正常同步、查询或主动等待物理帧；岸距每塘均衡抽样最多64个最终顶点，是抽样最大值而非全轮廓极值证明。第二阶段选择基准、1/32格和1/16格进行同图烘焙、通行差分及1080P对照。这些补偿会扩大实际退让，全部仍是实验参数，尚未获准用于正式游戏；不改变建造读取原始水顶点的规则。完整输入与结果保存在 `build/issue110-simplification/shape-summary.json`。

### 原生端点投影与第二阶段限制（2026-10-14）

修正水内外分类后，正式种子0完成工作中心认证和八条远端路线，但真实连续位置 `(185, 210.95731608072916)` 的反向接续仍被拒绝。它距水约2.05格，`MapGetClosestPoint` 返回原位置；`QueryPath` 首点却偏离约0.004367格，超过现有端点舍入限。三种路径后处理得到相同偏移，问题发生在查询起终点的三角形投影阶段，不能归因于Funnel，也不能通过放宽脚底余量解释。[固定查询源码](https://github.com/godotengine/godot/blob/ed1daf0bf/modules/navigation_2d/2d/nav_mesh_queries_2d.cpp#L217-L252)

单个真实细长三角形已复现：原生三角形包含检查为真，投影却远离原位置。源码使用 `a*c-b*b` 等Gram表达式；单精度在两条长近共线边下明显抵消。仅循环轮换该三角形的顶点起始索引，保留坐标、朝向及三条边，两个轮换档均恢复零偏移；平移和等比缩放无效。该证据支持进一步验证数值输入整理，不授权扩大端点容忍或改变搜索算法。[固定三角形投影实现](https://github.com/godotengine/godot/blob/ed1daf0bf/modules/navigation_2d/triangle2.cpp#L41-L106)

随后对保存的完整原生网格只循环轮换面索引，使每个三角形以其最短边开始。147,501个面调整了起始索引；坐标、朝向、面几何和有向边的前后SHA-256均一致。捕获点恢复零首尾偏移，近景自身行进3格后接续、100次连续取消及八处远端查询均通过原端点门限。该输入整理已接入正式实现并通过独立只读审查。随后正式种子0、1、110及重复种子0均通过完整创建、全部合法工作中心连通、固定远端路线、真实连续起点和各100次取消检查；同种子的水格、面积及路径保持确定性，完整创建31.9～37.1秒，证据为 `build/issue110-recovery/terrain-cyclic-formal.log`。此结论仅覆盖实际证据，不声称对任意极端三角形都建立了数值保证；[Eberly原实现说明](https://github.com/davideberly/GeometricTools/blob/master/GTE/Mathematics/DistPointTriangle.h#L48-L51)也指出此类二次表达式在浮点舍入下可能不准确，没有将最短边轮换宣称为该作者的通用算法建议。

保留原始三角形顺序的全图基准烘焙14.5秒；全部128,914个合法3×3工作中心在同一原生分量中，近景与四角固定路线完整且圆净空通过。但同组39个连续位置只有18个满足完整端点要求，自身路线行进3格后接续也失败。1/32和1/16格的双Round实验均约4秒烘焙，但同步出现重复完整边，不能认证连通性；这两档不能作为已通过的性能方案。失败记录也保存在 `build/issue110-simplification/`。下一项仅在实验中比较一次Round总外扩，完整检查其覆盖基准脚底障碍，避免在补偿圆弧上再次产生细碎边；正式参数保持原值。

### 单次Round与视觉离岸实验（2026-10-14）

后续实验统一采用上述三角形起始索引整理。原生RDP之后，只执行一次Round总外扩，将脚底半径、原数值余量、RDP补偿及可选视觉退让相加；用原生 `ClipPolygons(基准脚底障碍, 候选障碍)` 为空验证完整覆盖。只整理实际出现的完全相等连续浮点顶点，前后有向非零边哈希相同、双向Clipper差集为空；没有删面、扩大顶点合并格或增加自交轮廓兜底。

| 同图种子0实验 | 原始岸线基准 | RDP 1/32格 | RDP 1/32格＋额外离岸1/8格 |
| --- | ---: | ---: | ---: |
| 最终障碍顶点 | 286,125 | 14,137 | 17,166 |
| 576块烘焙（秒） | 14.536 | 3.962 | 4.161 |
| 近景完整查询（毫秒） | 20.702 | 1.537 | 2.053 |
| 近景路径点数 | 1,534 | 79 | 96 |
| 近景距原岸最小值（格） | 0.128668 | 0.145584 | 0.270584 |
| 开局同分量合法工作中心 | 128,914 / 128,914 | 128,914 / 128,914 | 128,914 / 128,914 |
| 自身路线100次取消接续 | 100 / 100 | 100 / 100 | 100 / 100 |
| 同组基准连续起点 | 39 / 39 | 28 / 39 | 26 / 39 |

两档候选的近景、四角固定路线均完整到达并通过原岸扫圆检查。RDP 1/32格档的名义中心退让为0.160400390625格，额外离岸档为0.285400390625格，均包含0.125格脚底、1/256格数值余量及1/32＋1/4096格简化补偿。表内基准连续起点的11或13处拒绝，是旧贴岸路径进入新增退让范围的真实差分；候选自身路线上的100次接续均通过，不能混淆两组起点。实际水域、含水格和完整陆地建造规则没有改变。1/16格单次Round仍出现原生重复完整边，未通过连通认证，未列作可采用候选。

首轮小通道探针仅等待两帧，基准0.26和0.30格空路径来自导航尚未同步，旧记录保留且不作为通行结论。改为等待当前地图的 `MapChanged`、实际区域归属和非零迭代后，限定直线夹具得到：原始档0.251格拒绝、0.26格通；1/32档0.30格拒绝、0.33格通；额外离岸档0.55格拒绝、0.58格通。拒绝均为正常同步后的部分路径，不表示完整到达；这些是夹具结果，不是任意形状通道的保证。证据为 `small-gap-retreat-synchronized-result.json`。

三档均采集了同图、同镜头、暂停的1920×1080真实Godot画面。额外离岸档在放大近景中更容易看出脚底与岸边的间隔；默认镜头保持自然绕行。截图中的人物取实际查询路径上的固定距离位置，用于视觉选择，不冒充经营运行验收。输入、矩阵和画面均位于 `build/issue110-simplification/`，以 `full-cyclic-retreat-summary.json` 和 `visual-retreat/captures.json` 为本轮依据。

原始输入在实验中复用，仅用于同图对照，不是游戏磁盘缓存；表内烘焙时间也不等于完整新局创建时间。两档简化及额外视觉退让仍待用户选择，正式脚底、数值余量和岸线处理未据实验自行放宽。后续正式多种子、全套测试及图形性能须在选择接入后重新验收。


关联 [#110](https://github.com/Fotally/Farm-Exchange/issues/110)，研究日期：2026-10-13。本文是成熟引擎能力与限制的证据，不授权替换[已确认实施方案](../project/water-terrain-proposal.md)，也不将近似结果当作原严格通道规则通过。正式代码未在本调研中修改。

## 判断

Godot 已提供本项目所需的常见导航链路：静态多边形导航区域、A* 搜索和 Funnel 路径收紧。后者返回所选通道内的短路径，不会要求人物持续沿湖边逐点行走。可以直接复用引擎，不需自造搜索算法。它保证的是所选多边形通道内的优化，并非所有可能绕湖方向上的全局最短路。[官方路径查询参数](https://docs.godotengine.org/en/stable/classes/class_navigationpathqueryparameters2d.html)

直接令 `NavigationPolygon.AgentRadius = 0.125` 不能等价满足决策前的“精确圆脚底、任意略宽通道都保留、不额外退让”约束：本地引擎对应源码使用 **Miter 折角偏移**，而非圆弧偏移，且存在固定小数精度。这些差异促成了已确认的 Round 预处理与1/256格余量调整；旧精确通道承诺已撤销，不将两套规则称作完全等价。[Godot 烘焙实现](https://github.com/godotengine/godot/blob/ed1daf0bf/modules/navigation_2d/2d/nav_mesh_generator_2d.cpp#L365-L375)、[Clipper2 默认精度](https://github.com/godotengine/godot/blob/ed1daf0bf/thirdparty/clipper2/include/clipper2/clipper.h#L129-L143)

## 核验版本和内置能力

本地执行指定引擎 `--version` 得到 `4.7.2.stable.mono.official.ed1daf0bf`。已核对该提交源码，以及引擎目录 `GodotSharp/Api/Debug/GodotSharp.xml` 中的 C# 绑定。下述代码链接固定在该提交；在线 stable 文档会随版本更新，不能替代本地验证。

| 需求 | 原生能力及使用边界 |
| --- | --- |
| 直接使用权威岸线 | `NavigationPolygon.AddOutline` 放地图外边界；`NavigationMeshSourceGeometryData2D.AddObstructionOutline` 放水域多边形。不必读取场景节点、图片或物理碰撞体 |
| 建局烘焙 | `NavigationServer2D.BakeFromSourceGeometryData` 是同步烘焙入口；另有显式异步版本 |
| 经营时间独立推进 | `QueryPath` 返回点列；之后 `WorkerScheduler` 自行按经营秒消费点列。无需 `NavigationAgent2D`、刚体或避让来决定工人位置 |
| 岸格干地允许走 | 多边形导航不按含水格整格封闭，因此表示层面支持岸格部分干地；是否保留某条细缝还受烘焙精度和顶点合并影响 |
| 避免无谓贴岸 | 使用 `PathPostProcessing.Corridorfunnel`；保持 `SimplifyPath=false`，不再引入另一轮可能改变贴障边界的曲线抽稀 |
| 多局隔离 | 每局独立地图 RID 和区域 RID，结束释放；资源身份不当作持久业务 ID |
| 完整到达判断 | 引擎可能返回通向最近可达点的部分路径，不能以非空点列宣称到达；还须确认起点未被投影、终点确实到达请求目标 |

这些能力由 [NavigationServer2D](https://docs.godotengine.org/en/stable/classes/class_navigationserver2d.html)、[源几何容器](https://docs.godotengine.org/en/stable/classes/class_navigationmeshsourcegeometrydata2d.html)和[路径查询参数](https://docs.godotengine.org/en/stable/classes/class_navigationpathqueryparameters2d.html)提供。查询应显式设 `PathSearchMaxPolygons=0` 才不会把默认搜索预算导致的部分路径误当完整连通性结果；静态地形只在任务目标变动时求路，不逐帧重建导航。

## 圆脚底与数值语义的差异

1. `NavigationPolygon.AgentRadius` 在烘焙时内缩可行走表面；`NavigationAgent2D.Radius` 是避让半径，不会让已有路径获得脚底净空。为三名可互穿的工人接入避让，不解决本 issue 的岸线问题。[导航多边形](https://docs.godotengine.org/en/stable/classes/class_navigationpolygon.html#class-navigationpolygon-property-agent-radius)、[导航代理半径](https://docs.godotengine.org/en/stable/classes/class_navigationagent2d.html#class-navigationagent2d-property-radius)
2. 该引擎提交的二维烘焙将陆地减去障碍，再调用 `InflatePaths(..., -agent_radius, JoinType::Miter, EndType::Polygon)`。以水域方角为例，真正圆脚底的中心禁区在凸角外形成圆弧；Miter 连接形成外延折角，会排除一些圆脚底本来能站立的位置。这是几何形状差异，不只是不够多的小数位。[固定版本实现](https://github.com/godotengine/godot/blob/ed1daf0bf/modules/navigation_2d/2d/nav_mesh_generator_2d.cpp#L365-L375)
3. 所调用的 Clipper2 双精度路径包装默认 `precision=2`，内部转成缩放整数后运算。偏移包装按 `10²` 缩放，布尔运算的 `ClipperD` 构造器另将缩放调到二进制幂，此处为 `128`；因此不能简单把整个烘焙链描述为统一 `0.01` 网格。将所有坐标、半径和 `CellSize` 一致放大可减小换回基础格后的量化间隔，但不能消除浮点表示、顶点合并和 Miter 形状差异，也不能据此承诺“任何正宽度细缝都不丢失”。[偏移包装](https://github.com/godotengine/godot/blob/ed1daf0bf/thirdparty/clipper2/include/clipper2/clipper.h#L129-L143)、[布尔运算缩放](https://github.com/godotengine/godot/blob/ed1daf0bf/thirdparty/clipper2/include/clipper2/clipper.engine.h#L498-L505)
4. `CellSize` 须与地图一致；它影响导航顶点归并。较小值不是对圆几何或连通性的数学证明。跨区域边缘连接还会按容差连接边，因此研究工具使用单区域并关闭额外边缘连接，避免把窄缝结果混入另一种自动连接效果。[导航多边形属性](https://docs.godotengine.org/en/stable/classes/class_navigationpolygon.html#class-navigationpolygon-property-cell-size)、[官方性能说明](https://docs.godotengine.org/en/stable/tutorials/navigation/navigation_optimizing_performance.html#performance-problems-with-navigation-map-synchronization)

上述差异意味着：“与真实岸线不冲突的一条路径”只证明这条路径安全；不能证明每个原规则允许的圆脚底位置和通道都被保留。用安全验证拒掉引擎结果也无法补回被内缩或合并掉的通道。

### 现成圆角预处理能否替代内置 Miter

可进一步验证 `Geometry2D.OffsetPolygon(..., JoinType.Round)`：用现成引擎几何操作预膨胀水域、内缩地图外框，再把 `NavigationPolygon.AgentRadius` 设为 `0`，避免再次 Miter 偏移。这仍复用原生算法，不需要自己实现偏移、三角化或 A*。

但它也不是精确圆解决方案。固定版本 `Geometry2D` 实现使用 `precision=5` 和固定 `arc_tolerance=0.25` 导航单位，圆角输出是有限线段；后续导航烘焙仍会经历默认精度的布尔运算。倍率会改变换算回基础格的圆弧误差，圆弧弦和再量化还可能低估真实半径。`AgentRadius=0` 的官方文档也明确提示重叠和精度错误不能由半径处理消解。[圆角实现](https://github.com/godotengine/godot/blob/ed1daf0bf/core/math/geometry_2d.cpp#L290-L344)、[导航半径说明](https://docs.godotengine.org/en/stable/classes/class_navigationpolygon.html#class-navigationpolygon-property-agent-radius)

该组合已追加最小矩形实验，结果见下文：圆角改善了合法角点保留，但零余量路线仍略微侵入真实圆净空；加入1/256格余量后所测路径满足半径，同时关闭一条旧规则允许的细缝。用户已据此确认有限余量方案；这保留了实验反例，不再以旧规则阻止当前接入。

## 同步创建与生命周期

同步烘焙和导航地图同步是两件事。`BakeFromSourceGeometryData` 返回后可以拿到导航多边形，但将区域装入新地图的命令通常在物理帧同步阶段生效。正常接入应在开局准备阶段等待地图首次同步，再发布可玩的局；后续静态地图查询不需要每个任务再等待一帧。[官方服务器说明](https://docs.godotengine.org/en/stable/classes/class_navigationserver2d.html)

`MapForceUpdate` 能强制立即更新，但会冲刷整个 NavigationServer 命令队列；当前官方文档将其标记为与异步更新不兼容、仅单线程风险自负的废弃入口。本地 C# 绑定仍有该方法，不等于没有全局副作用。本次实验不调用它。[官方强制同步说明](https://docs.godotengine.org/en/stable/classes/class_navigationserver2d.html#class-navigationserver2d-method-map-force-update)

决策前的同步 `TerrainMap.Create` 契约已由确认后的 `CreateAsync` 替代：调用方等待完整建局，Module 内部等待正常物理帧同步，完成前不推进经营。同步入口只接受显式纯陆地，复用完整创建流程且不建立无用导航资源。工人随后仍按经营秒消费静态路线；不使用强刷全局队列满足同步签名。

## 地图规模与平台

384×384 格、48 座静态池塘和 3 名工人，在结构上适合一次生成导航网格、按任务查询。Godot 官方指出路径搜索成本主要随多边形和边数变化，而非地图坐标范围；因此不应为每一基础格创建一个导航区域。这是适配判断，尚不是本项目完整岸线的性能验收。[官方性能说明](https://docs.godotengine.org/en/stable/tutorials/navigation/navigation_optimizing_performance.html#performance-problems-with-the-actual-path-search)

Godot 导航是引擎模块，不需给 Windows/macOS 分别加另一套导航插件。项目已选用 .NET 双平台导出，官方 macOS 模板提供 Universal 2；本研究只实际运行 Windows 指定引擎，不能称作已经验证 macOS 路径完全一致或导出成功。[macOS 官方导出说明](https://docs.godotengine.org/en/stable/tutorials/export/exporting_for_macos.html)

## 本地最小实验

研究工具保存在忽略目录 `build/issue110-navigation-probe/godot/navigation_probe.gd`。它在现有项目下调用原生 API，不创建另一份项目或源码树，不接入正式经营。运行命令：

```powershell
& 'E:/Godot/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe' --headless --path 'E:/Godot/Farm Exchange' --script 'res://build/issue110-navigation-probe/godot/navigation_probe.gd'
```

实验采用半径 `0.125`、基础格口径 `CellSize=0.001`，分别以 1 和 1024 的单位倍率输入引擎；倍率同时作用于所有坐标、半径及 CellSize，不依赖节点视觉缩放。两矩形阻挡形成宽 `0.24/0.25/0.251/0.26/0.3/0.5` 的唯一通道；另以方形水域检查绕湖拐点和圆脚底合法角点 `(3.9,3.9)` 是否被投影。只在两次物理帧信号后查询，不调用强制全局同步。

输出 `report.json` 保存完整烘焙顶点、路径、目标是否到达、烘焙和查询耗时。路径到矩形岸线的最小距离使用引擎现成 `Geometry2D.GetClosestPointsBetweenSegments` 统计；它是研究诊断，不是另一套寻路算法。等宽和窄于直径用例须检查是否真的拒绝到达，不能把返回部分路径算作通过。

主 Agent 使用指定引擎串行执行，2026-10-13 两次均退出 `0`。最新含净空报告为 `report.json`，日志为 `run-clearance.log`，首次报告保留为 `report-first.json`。实际结果如下，表中“到达”只说明引擎确实返回请求目标，不代表全部 issue 验收通过：

| 用例 | 原圆规则 | 单位倍率 1 | 单位倍率 1024 |
| --- | --- | --- | --- |
| 通道宽 0.24 | 不通 | 未到达 | 未到达 |
| 通道宽 0.25 | 不通 | 未到达 | 未到达 |
| 通道宽 0.251 | 直通道可通 | **未到达** | 到达；最小净空约 0.1255002 |
| 通道宽 0.26 | 直通道可通 | 到达 | 到达 |
| 通道宽 0.3 | 直通道可通 | 到达 | 到达 |
| 通道宽 0.5 | 直通道可通 | 到达 | 到达 |
| 方池塘绕行 | 每段净空至少 0.125 | 到达，但最小净空 **0.1171875，小于脚底半径** | 到达，最小净空 0.125 |
| 方池塘外 `(3.9,3.9)` | 距原角点约 0.141421，可站立 | 投影至约 `(3.8828125,3.9)` | 投影至约 `(3.875,3.9)` |

圆脚底可站立的角点在两种倍率下都被移出，证实 Miter 区域不是原圆脚底区域。倍率 1 更同时出现合法细缝丢失和绕湖净空不足，说明原生半径参数本身不能担保实际路径满足本项目半径规则；倍率 1024 改善所列直通道和方池塘路径，并未消除上述形状差异。

14 张新图的建局同帧 `MapGetIterationId` 全为 `0`，两次物理帧后能够查询；本实验未测试精确最少所需帧数，不能将“两帧后就绪”改写成“一帧必就绪”。每例同步烘焙加登记为 31～183 微秒，查询为 2～20 微秒；这是很小的矩形用例、一次运行的观测，不含等待首次地图同步的时长，也不是全图 48 池塘性能指标。

后续按已确认的新精度及生命周期约定，验证真实岸线、多种子 48 池塘、连续取消改派、批量经营一致性与双平台导出。有限矩形实验不能替代这些验收。

### Round 预处理与明确数值余量的候选实验

后续工具 `build/issue110-navigation-probe/godot/round_navigation_probe.gd` 复用上面的诊断入口，正式默认半径仍是 `0.125`；只在研究工具中测试额外余量 `0` 和 `1/256=0.00390625` 格。统一坐标倍率为 `1024`，地图和多边形的 CellSize 均为 `1/4096` 格；以 `Geometry2D.OffsetPolygon(Round)` 按“半径＋研究余量”膨胀水域并内缩外框，随后以 `NavigationPolygon.AgentRadius=0` 烘焙，使用原生 A* 和 Funnel 查询。没有自行实现圆角、搜索或路径收紧算法。

主 Agent 串行执行时，首轮因两个临时向量未显式标注类型而解析失败；修正研究脚本的类型后重跑退出 `0`。有效报告为 `report-round.json`、日志为 `run-round-second.log`。报告保留每一条路径段的真实矩形岸线净空，以及起终点和指定角点的投影距离。

| 用例 | Round 零余量 | Round 额外 `1/256` 格余量 |
| --- | --- | --- |
| 通道宽 0.24 | 未到达 | 未到达 |
| 通道宽 0.25 | 未到达 | 未到达 |
| 通道宽 0.251 | 到达，最小净空约 0.1255002 | **未到达，原规则允许的通道被关闭** |
| 通道宽 0.26 | 到达，最小净空约 0.1300001 | 到达，最小净空约 0.1300001 |
| 通道宽 0.3 | 到达，最小净空约 0.1500001 | 到达，最小净空约 0.1500001 |
| 通道宽 0.5 | 到达，最小净空 0.25 | 到达，最小净空 0.25 |
| 方池塘绕行 | 到达，净空 **0.1247551367＜0.125** | 到达，净空 **0.1286599636＞0.125** |
| 方池塘外圆合法角点 `(3.9,3.9)` | 投影距离 0，保留该点 | 投影距离 0，保留该点 |

成功到达用例中，起点和终点仍有普通浮点级变化，最大观测起点偏移约 `0.00000182` 格；不能写成每一个端点位元精确相同。不成功用例返回的是停在通道前的部分路径，不是完整目标路径。零余量下宽 `0.24` 用例的部分路线也出现约 `0.1247842` 的最小净空，进一步说明“没有到达目标”不代表返回的部分路径本身满足圆脚底安全。

这组证据支持并已确认的方案是：**Godot 现成 Round 预处理＋原生 A*／Funnel＋明确的有限数值余量＋开局等待首次地图同步**。`1/256` 已确认为数值安全余量；小型实验尚未证明它在全部真实岸线和地图位置都足够，仍须完成实际场景验收。它将理想直通道门槛从 `0.25` 提高到 `0.2578125` 格，再叠加离散误差；这一可达范围变化已经明确确认，不能只称作完全等价的性能优化或缺陷修复。

### 显式强制同步实验

已选 Godot Round 方案的方塘绕行在两档余量下都返回 **34 个路径点**。这与 DotRecast 对照图中的 4／6 点不是同一条路线，也不能用该图代替 Godot 的表现证据。部分点位于接近共线的路段，仍需在真实工人移动中核对角点、方向切换和是否出现明显多余转弯；本轮小型探针未完成这项游戏画面验收。

Round 脚本还在任何其他实验地图创建前，为唯一一张独立地图设置 `MapSetUseAsyncIterations(false)`，显式调用一次 `MapForceUpdate`。同步编号由 `0` 变为 `1`，调用耗时约 `920` 微秒；但立即查询仍返回空路径，最近点查询为 `(0,0)`。相同地图在正常两次物理帧后才返回通道前的有效部分路径。

因此本实验没有验证“在 SceneTree 初始化同帧强制更新后即可安全查询”；尤其不能仅检查 iteration 变为 `1` 就宣布导航数据全部就绪。该实验也不证明其他引擎生命周期时点绝不可能同步使用。结合官方关于冲刷全服务器队列及异步限制的说明，本候选继续采用明确的开局准备与正常同步时序，不把强制更新塞进正式 `Create` 隐藏副作用。

## 道路偏好的原生可行性实验（2026-10-14）

用户新增“有道路时尽量沿道路行走”。本节仅记录实验，不把尚未确认的成本与道路建拆刷新时机写成现行玩法。实验使用实际 Godot 4.7.2 `ed1daf0bf`，外部 GDScript，不加载正式 Main；完整输入、源码和报告保存在 `build/issue110-road-preference/`，尚未修改正式道路实现。

[官方区域说明](https://docs.godotengine.org/en/stable/classes/class_navigationregion2d.html)提供区域距离倍率 `travel_cost`；区域必须真实共边，不能在完整土地导航上叠加道路多边形。倍率只参与选路，不改变工人的经营移动速度。成本属于 region，不能给同一 region 的不同三角面分别设成本。

20×11 小地图以互不重叠的三角面验证六种道路布局、三档成本、两个出口及两种原生后处理，共 72 次查询均完整。普通地面 2、道路 1 时，靠近目标的一条道路使路线由 17.32 格直行变为总长 18.62 格，其中 15 格实际位于道路内部；接近两端的 U 形道路也会被选用。但离起终点各约三格的平行道路仍未被选用，普通地面改成 4 也相同。因此只能承诺原生的最佳努力偏好，不能宣称按权重全局最优。[固定版本源码](https://github.com/godotengine/godot/blob/ed1daf0bf/modules/navigation_2d/2d/nav_mesh_queries_2d.cpp#L292-L308)显示剩余距离启发值乘的是当前邻区域成本，而非全地图最小成本；Funnel 又按已选走廊优化几何，可能沿道路边界走。上述源码与观测一致，但不是对所有选路行为的完整因果证明。

为避免每次建拆重新烘焙，另在 16×16 含水小图验证了静态面预分区。先将旧烘焙三角面逐格裁剪再三角化，会产生 12 条不属于原始外边界的单边，即使 64 次查询成功也不满足严格共边要求，已否决，未加入手工接缝修补。改成直接用每个游戏地块矩形减同一水障碍，再用原生 `Geometry2D.TriangulatePolygon`，得到 528 面，256 次裁剪约 3.89 ms；严格审计内部单边及超过两个面共用的边均为 0。普通／道路两个 region 在新增、移动、删除道路时仅重新分组已有面，四轮共 64/64 跨图查询完整，不调用烘焙；重分组加正常服务器同步约 15～33 ms，包含物理帧等待。

这一小图结果支持继续评估“按既有地块分割连续 navmesh，原生 A* 仍是唯一搜索，每块各设普通／道路 region”。它没有建立第二套格搜索或连通表。正式接入仍需验证全地图面数量与启动耗时、地图收缩外框及多塘裁剪、所有相邻块的完整共边、完整合法工作中心，以及道路变更后的实际同步。当前纯陆地同步构造不持有原生 map，也必须在正式设计中明确处理，不能只验证含水局后宣布完成。

道路建拆还有真实的调用契约问题：目前同步建路成功后，同一个宿主回调可以立即同步 `AdvanceTicks`，工人也同步查询路线；独立局的批量循环未必让出任何物理帧。若新领取必须采用新道路，就需要成功建拆后等待导航准备、在就绪前暂停完整经营 tick，主场景和两种开发流程均遵守同一异步准备约定。同步推进只接受就绪状态，不能阻塞物理线程、将未就绪冒充不可达，或静默忽略纯陆地道路。若改为允许同步前的新领取读取旧快照，则是另一项需要明确接受的可见规则，而且独立批量仍须主动让出同步时机。实验没有代用户选择，也没有实施这项契约变化。
