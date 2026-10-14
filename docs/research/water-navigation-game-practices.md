# 水域导航：经典游戏实践与本项目取舍

> 2026-10-14 决策更新：采用 Godot 原生导航、1/32 格保守简化、额外离岸 1/8 格及原有 1/256 格数值余量；道路优先也已纳入本期。现行范围见[水域实施方案](../project/water-terrain-proposal.md)，道路规则见[道路玩法](../gameplay/land/roads.md)。下文保留研究当时的范围与实验，不代表最终版本已通过验收。


关联 [#110](https://github.com/Fotally/Farm-Exchange/issues/110)，研究日期：2026-10-13。本文回应“沿安全外围绕行会不会呆板、是否只沿湖边走”，记录一手证据及已确认选型的依据；现行规则以[水域实施方案](../project/water-terrain-proposal.md)为准。

## 结论

固定选一侧、逐点沿包络走，确实可能造成多余绕路和连续小转弯；确定性本身不会让动作呆板，问题在于选择了什么路线、哪些拐点被保留。正常绕水也可能有一段贴近障碍：目标在湖后面时，绕过遮挡才能直达。应避免的是“已经能安全直达，仍继续绕岸”，而非为了观感强制所有路线远离岸边。这是几何与表现上的分析，不是声称某款游戏采用本项目的岸线模型。

用户随后明确要求采用成熟业界实现，不自行创造算法。前述自研可见图优化或外围绕行不再作为推荐路线。C# 候选 DotRecast 可以直接提供建网、寻路与路径拉直；已完成的 39 例探针同时发现窄道误放行和方湖转角侵水，当前参数不满足 #110 的原严格连续通道契约。依据这些反例与原生引擎实验，现已确认 Godot 原生导航、圆角偏移、1/256 格数值安全余量和开局等待同步；完整地图与表现仍须验证，不能把“库成熟”当作规则一致的证明。

## 已核实的一手实践

| 来源 | 核实内容 | 对本项目的启发与边界 |
| --- | --- | --- |
| Factorio 官方 [FFF #117](https://factorio.com/blog/post/fff-117)，2015-12-18 | 开发者先统计请求，发现大量短程查询；短程移动尝试直达及指定一侧绕障，失败再寻路。还针对单位阻挡引入让路行为 | 这是特定短程优化，不能作为完整导航必然正确的依据；本项目工人可互穿，无需引入让路 |
| Factorio 官方 [FFF #121](https://factorio.com/blog/post/fff-121)，2016-01-15 | 明确说明简单移动可能困在角落，只用于短程。改善重点还包括复用已求路径的中段及缓存不可达结果 | 不能把指定侧绕行单独当作一般寻路；本项目仅三工人，暂不据此增加复杂路径缓存系统 |
| Factorio 官方 [FFF #317](https://factorio.com/blog/post/fff-317)，2019-10-18 | 针对大湖绕行，把区块内陆地连通分量抽象成节点；抽象搜索提供更好的启发式，基础 A* 仍决定真实路线。抽象忽略实体，基础搜索能偏离建议路线避开实体 | “搜索空间简化”不等于“角色被迫沿抽象边界走”。其整格水陆模型与本项目连续岸线不同，不能照抄通行结果 |
| 《帝国时代》程序员 Dave Pottinger 的署名文章 [Coordinated Unit Movement](https://www.gamedeveloper.com/programming/coordinated-unit-movement)，1999-01-22 | 将求路与执行路线分开，讨论转弯限制、连续移动碰撞和状态保留；作者指出逐帧丢失既有决策会导致来回反复 | 路线安全、路线执行与观感应分别验收。文章是作者的方法与经验介绍，不代表所有示例都已用于该游戏；本期无需编队、加减速或单位避让 |
| Recast/Detour 官方 [查询接口](https://recastnav.com/classdtNavMeshQuery.html)与[实现源码](https://github.com/recastnavigation/recastnavigation/blob/main/Detour/Source/DetourNavMeshQuery.cpp) | `findPath` 得到多边形通道，`findStraightPath` 在通道内执行 string pulling；实现维护左右 portal 与 apex，留下必要转角 | 可借鉴“找到可达走廊后再拉直”的职责划分。它是导航库实践，不据此宣称 Factorio、帝国时代或 StarCraft II 使用该库 |

### StarCraft II 资料的核实边界

已核对 [GDC 2011 官方会话页](https://www.gdcvault.com/play/1014514/AI-Navigation-It-s-Not)：会话名为 “AI Navigation: It's Not a Solved Problem - Yet”，讲者包含 Blizzard 的 James Anhalt，以及 Alexander Kring、Nathan Sturtevant。可访问页面未提供可核读的完整讲稿；[配套资料页](https://www.gdcvault.com/play/1014515/AI-Navigation-It-s-Not)嵌入的是 Nathan Sturtevant 命名的 Keynote 压缩资料。本轮未完成 James Anhalt 演讲内容核验，因此不把网络转述的 StarCraft II 三角剖分、A*、funnel 或 steering 细节写成已核实事实。

## 对 #110 的适配边界

本项目有三名工人，默认 48 座静态池塘；工人可以穿越建筑和其他工人，不需要动态拥堵、编队或道路偏好。脚底圆半径默认1/8格，导航另加已确认的1/256格数值余量。整段路线必须容纳完整脚底，但不承诺任意略宽于直径的通道必通；现行约定见[实施方案](../project/water-terrain-proposal.md)。

| 候选做法 | 玩家可见结果 | 本期判断 |
| --- | --- | --- |
| 按确定方向逐点跟随安全外围 | 能绕开障碍，但可能走长边、保留许多小折点或离目标方向过远 | 不建议仅凭运算次数少就直接替换；每座池塘仅处理一次也不能自动证明所有组合路径完整 |
| 保留当前自研可见图并继续优化 | 可保持既有输出，但继续承担几何与搜索实现维护 | 历史候选，已不符合用户本轮要求的实施方向 |
| 自研较少节点搜索及删拐点 | 理论上可减少折点，但仍需自行解决完整性 | 历史候选，不继续作为当前推荐 |
| 成熟导航网格模块＋库内路径拉直 | 查询收紧所选通道内的路线，能直达时无需继续沿岸；实际点数和动作仍须验证 | 已选择 Godot 原生导航与 Funnel；仅接入本期需要的建网和查询，不增加动态避让 |

路线收紧交给原生 Funnel，项目不另写删点算法；返回后检查脚底圆沿整段扫过的范围，而不是只验端点、点射线或整格陆地。Funnel 不保证换到全局更短的绕湖一侧，安全检查也不证明被烘焙封闭的细缝仍能通行。

Funnel 依赖正确的可行走通道。已确认方案显式采用有限精度和数值余量，等宽及零宽连接不通，完整返回路线按同一权威岸线核验圆净空；不再要求证明任意略宽通道都被保留。

不要直接给路径套样条曲线：曲线可能切过湖角，改变真实距离和到达时间。当前更小的做法是保留安全折线，按真实分段路程推进经营并插值展示；需要额外的圆弧转弯或转向速度时，应作为显式行为讨论、重新验证完整轨迹，不让表现层独自改变脚底位置。

## 成熟 C# 实现候选：DotRecast

### 版本与接入证据

DotRecast 是 Recast/Detour 的 C# 移植；模块明确分为 Core、Recast 建网、Detour 查询，以及可选的 Crowd、TileCache、Dynamic。直接引用现成包能够复用核心实现，不应把库算法翻译一遍再维护为本项目自研代码。[官方仓库](https://github.com/ikpil/DotRecast)

截至本次核验，NuGet 最新稳定 `DotRecast.Detour` 为 `2026.3.1`，更新日期 2026-08-02；包包含 `net10.0`，依赖同版本起的 `DotRecast.Core`。[官方 NuGet 包](https://www.nuget.org/packages/DotRecast.Detour/2026.3.1) 对应源码标签指向 `16976369bebab455b59bcb93c799c775a91d816d`；仓库主线最近提交为 2026-10-03，说明项目仍有维护，但实验应固定发布版，不能把主线与发布包混用。[版本标签](https://github.com/ikpil/DotRecast/tree/2026.3.1)、[已核验主线提交](https://github.com/ikpil/DotRecast/commit/6fce4ac0d59f4d4efdf9de0a9e5beae5784bca12)

发布版 Core、Recast、Detour 均为普通 C# 项目，目标框架包含 `net10.0`；后两者只引用 Core。由此推断本项目可按普通托管程序集在 Windows/macOS Godot .NET 导出中集成，无需为这三个模块自编 C++ 桥接。此判断仍须实际双平台构建和启动核验，不能把 NuGet 兼容标记当作本项目导出已通过。[Core 项目](https://github.com/ikpil/DotRecast/blob/2026.3.1/src/DotRecast.Core/DotRecast.Core.csproj)、[Recast 项目](https://github.com/ikpil/DotRecast/blob/2026.3.1/src/DotRecast.Recast/DotRecast.Recast.csproj)、[Detour 项目](https://github.com/ikpil/DotRecast/blob/2026.3.1/src/DotRecast.Detour/DotRecast.Detour.csproj)

许可证为 Zlib：允许商业使用、修改及分发，要求不冒称原作者、标明修改版，并保留源码分发中的许可证声明。正式纳入时应保存依赖来源与许可证记录；不需要把 Demo 及其图形依赖带入游戏。[许可证原文](https://github.com/ikpil/DotRecast/blob/2026.3.1/LICENSE.txt)

### 可以直接复用什么

| 环节 | 直接复用 | 项目只需承担的适配 |
| --- | --- | --- |
| 建局准备 | DotRecast.Recast 的栅格化、侵蚀、区域及多边形生成 | 将已有权威陆地几何作为输入，不从截图重建岸线；按确认后的精度参数创建一次静态网格 |
| 路线查询 | `DtNavMeshQuery.FindPath` | 提供起终点所属多边形和统一查询过滤；检查真实终点及返回状态 |
| 转角整理 | `DtNavMeshQuery.FindStraightPath` | 使用库内 string pulling 结果，不重写 funnel，不额外套会切水的样条 |
| 行走与作业 | 继续由现有 WorkerScheduler 推进 | 将库返回点转换为经营路线；工速、到达时间、播种浇水、暂停和批量推进保持业务归属 |

建网可从官方 [RecastSoloMeshTest](https://github.com/ikpil/DotRecast/blob/2026.3.1/test/DotRecast.Recast.Test/RecastSoloMeshTest.cs) 使用的正式入口开始；求路和结果约定参照官方 [FindPathTest](https://github.com/ikpil/DotRecast/blob/2026.3.1/test/DotRecast.Detour.Test/FindPathTest.cs)。查询实现可返回部分路径或缓冲不足；成功状态不能单独代表已到真实终点。`FindStraightPath` 会约束端点到通道边界，因此不能静默接受“最近可达点”代替原工作中心。[发布版查询源码](https://github.com/ikpil/DotRecast/blob/2026.3.1/src/DotRecast.Detour/DtNavMeshQuery.cs)

本项目二维基础格平面可以适配为 `(gridX, 0, gridY)`；查询得到的 X/Z 再映射回基础格。Y 仅作平面高度，不把等距屏幕坐标送入导航。依据是 Recast 的水平单元尺寸定义在 XZ 平面、Y 为高度。[RcConfig 源码](https://github.com/ikpil/DotRecast/blob/2026.3.1/src/DotRecast.Recast/RcConfig.cs)

库查询不依赖本项目经营帧；建议建局固定输入与网格，工人接任务时按稳定顺序同步求完整路线，同一条路线由累计经营路程推进。单秒与批量等价由 WorkerScheduler 负责，不交给 Crowd 的逐帧速度积分。跨 Windows/macOS 的浮点结果是否完全一致尚未得到库承诺，需要固定版本及重复实验验证，不能仅凭“C#”宣称跨平台位级确定性。

### 圆脚底与严格通道的实际限制

Recast 使用建网时的可行走区域侵蚀处理角色半径：发布版将世界半径除以 `Cs` 后向上取整为整数 `WalkableRadius`。这不是查询时对原岸线执行精确圆扫掠。[RcConfig 源码](https://github.com/ikpil/DotRecast/blob/2026.3.1/src/DotRecast.Recast/RcConfig.cs) Detour 的 `FindPath` 和 `FindStraightPath` 没有传入本次角色半径的参数；不能误以为仅填写网格头部的 `walkableRadius` 就会在运行查询时精确避岸。[网格创建参数](https://github.com/ikpil/DotRecast/blob/2026.3.1/src/DotRecast.Detour/DtNavMeshCreateParams.cs)、[查询源码](https://github.com/ikpil/DotRecast/blob/2026.3.1/src/DotRecast.Detour/DtNavMeshQuery.cs)

官方配置说明还明确存在体素尺寸、轮廓简化误差和小区域删除。由这些机制推断：有限分辨率建网不能天然保证任意微小 `ε > 0` 的 `2r+ε` 通道都保留，也不能只靠较小的 `Cs` 声称与连续圆几何完全等价。[Recast 配置说明](https://recastnav.com/structrcConfig.html)

保留原岸线扫圆作为验收可检出不安全路线，却无法补回建网阶段已经删除的可通行窄道。把半径设零、另加一点临时退让、最近点吸附或失败切回自研算法，都不能作为未经确认的解决方案。自行构造“精确侵蚀网格”再喂 Detour 也会重新引入本轮要求避免的核心几何研发；本次尚未核实一条现成库组合能够无条件满足全部精确契约。

### 最小集成实验与决策点

实验使用固定 `2026.3.1` 的 Core/Recast/Detour；不改正式经营入口，不引入 Crowd、动态障碍或流场。先在小范围权威岸线输入上复用官方建网和查询入口，记录输入、参数、完整路径、时间和内存，比较以下情况：

1. 空地直达、单湖两侧、连续两湖，以及起终点位于合法含水岸格干地。
2. 宽度低于、恰好等于、高于 `2r` 的直通道及转角；对有限几档逐渐缩小的正余量记录失真边界，不能把有限测试解释为所有正余量的证明。
3. 重复同局查询、同种子重建、单秒与批量消费同路线，以及 Windows/macOS 发布程序集的结果。
4. 小范围通过后再测试 384×384 全图、48 池塘；按真实内存和时间决定是否需要库已有分块建网。默认 `r=0.125`，若取 `Cs=r/2`，完整平面已有约 3775 万水平采样格，仅为规模估算，不能先假定单块全图烘焙足够快。

实验结果只覆盖这一组输入与参数。已据具体窄道和净空差异确认采用 Godot 原生导航、1/256 格数值安全余量与开局等待同步；旧的任意微小宽差均须保留的数学承诺已被替代。正式接入仍须验证完整地图、实际脚底安全和表现。

### 已完成的 39 例实际探针

2026-10-13 已完成独立 `net10.0` 探针的还原、Release 编译和执行；工具及原始 JSON 位于 `build/issue110-navigation-probe/dotrecast/`。它直接调用固定 `2026.3.1` 的 Recast 建网、Detour `FindPath` 与 `FindStraightPath`，没有重写寻路或路径拉直。原图为 8×8 的有限平面，原始水域由已知矩形定义；轮廓简化误差设 0，不删除小区域。39 例均在运行器 60 秒上限内完成。

探针把“库报告完整到达”与“原圆脚底不侵水”分开。后者按返回折线的每一段到原始矩形的标准点段／段段距离计算，不只检查折点，不修改库的输出。诊断仅适用于本组矩形输入，不是新增正式导航算法。

三种采样尺寸 `0.0625 / 0.03125 / 0.015625` 的直通道结果如下，除特别说明外三种尺寸结论相同：

| 原始通道 | 对齐方式 | 库返回完整到达 | 原规则检查 |
| --- | --- | --- | --- |
| 宽 0.24 | 中心对齐 | 否 | 正确拒绝 |
| 宽 0.25 | 中心对齐 | 否 | 正确拒绝恰好等宽 |
| 宽 0.251 / 0.26 / 0.3 / 0.5 | 中心对齐 | 是 | 这些有限直通道的净空满足半径要求 |
| 宽 0.24 | 横移 0.01 或半个采样格 | 是 | **错误放行**；最小净空约 0.12，小于半径 0.125 |
| 宽 0.25 | 横移半个采样格 | 是 | **错误放行**；实际宽度仍精确为 0.25，仅有等宽通道 |
| 宽 0.251 | 横移 0.01 或半个采样格 | 是 | 这些有限直通道的净空满足半径要求 |

横移时地图边界与网格原点固定，只移动通道和端点，确实改变采样对齐。名义宽度 0.25 横移 0.01 后，float 岸线相减得到 `0.2500000074505806`；JSON 按实际输入几何记录为略宽，因此不将这三例当作原“恰好等宽”规则已被验证，也不能用其通过掩盖输入量化改变。半格横移案例不存在这一歧义。

单湖原始水域为 `[-1,1]×[-1,1]`，起点 `(-3,0)`、终点 `(3,0)`。三次都完整到达，真实全段净空却不同：

| 采样尺寸 | 输出点数（含起终点） | 原始岸线最小净空 | 与半径 0.125 比较 |
| --- | --- | --- | --- |
| 0.0625 | 4 | 0.125000000 | 此例不侵水 |
| 0.03125 | 4 | 0.124034735 | **侵水** |
| 0.015625 | 6 | 0.118791111 | **侵水** |

这也证明不能仅检查最靠近湖角的折点：线段内部可能更接近水域。更小采样格在这三个特定输入上并未单调提高净空，不能据此预填某个安全误差上限。路线确实呈现空地直走、必要处转向、最后直达的行为，但这一观感成立不等于原脚底约束成立。

本机有限用例的最大建网时间约 146 ms，最大查询时间约 19 ms（含首次调用）；这不是全图或游戏帧性能验收，不据此保证 384×384 地图的构建成本。研究用真实折线图保存在同目录 `route-comparison-1920x1080.png`；不平滑、不美化路线，不作为游戏截图。

**实测结论：本轮 DotRecast 参数不能直接作为满足 #110 原硬约束的正式实现。** 现已根据[Godot 原生导航研究](godot-water-navigation.md)确认原生导航和有限安全余量，后续按这一方案接入并验证。不能把“先侵水再由验收拒绝”当作最终方案，也不能以最近点吸附、额外暗加退让或失败改走自研路线遮蔽差异。本研究只交付独立测试工具与证据；正式接入按已确认调整另行实施，脚底半径保持不变，完整验收尚未完成。

## 2026-10-13：针对完整地图烘焙成本的补充研究

本节回应用户要求参考经典游戏优化。当前要解决的是高细节岸线输入带来的原生建网成本，不是为三个工人重新设计搜索算法。本节只核读一手资料；实现组的整图和分块探针仍在验收，不将局部烘焙完成当作整图连通已通过。

### 成熟项目把哪些数据分开

0 A.D. 的 `Pathfinding.h` 为寻路单独定义 navcell 和按单位通行类别预计算的位标志；导航格与地形格通过明确比例换算，并不以渲染细节直接充当搜索状态。值得借鉴的是“按通行需求选择数据精度”，不能据此宣称它使用本项目的精确圆岸线；该源码明确另有保守扩张。[固定源码](https://github.com/0ad/0ad/blob/61a3b9507d974084e6badb88a0826bd89a6d5b8b/source/simulation2/helpers/Pathfinding.h#L123-L197)

同项目的 `CCmpPathfinder` 保存 `m_TerrainOnlyGrid`，未发生地形和障碍变化时直接结束更新；发生变化时区分整图与脏格更新，再通知长距离和层级寻路器。它说明静态通行数据可以预计算并复用。对 #110 的适用推论是：水域本局不变，建局后保留同一导航资源；道路、农田和工人既不阻挡通行，就不应触发水域建网重算。本期没有必要移植其动态障碍系统。[固定源码](https://github.com/0ad/0ad/blob/61a3b9507d974084e6badb88a0826bd89a6d5b8b/source/simulation2/components/CCmpPathfinder.cpp#L447-L529)

0 A.D. 的层级模块将地图拆成固定块，在块内提取连通区域，再连接相邻块的可通行区域，用全局区域编号回答连通性。其 `MakeGoalReachable` 还会改写不可达目标；这一行为与 #110 必须到达真实工作中心的约定不同，不可照搬。这里只借鉴“把区域连通与详细几何区分”，不新增自研层级图。[固定源码](https://github.com/0ad/0ad/blob/61a3b9507d974084e6badb88a0826bd89a6d5b8b/source/simulation2/helpers/HierarchicalPathfinder.h#L27-L128)

Godot 官方建议导航输入使用简化几何，查询成本取决于多边形和边数；不可达搜索还可能遍历远多于正常路线的多边形。查询应在目标或地图真正变化时进行，避免每帧先检查可达、随后再求路。对本项目，优先分别测建网、同步和实际任务查询；不先引入面向大量单位的流场或错帧调度。[官方性能说明](https://docs.godotengine.org/en/stable/tutorials/navigation/navigation_optimizing_performance.html)

视觉与导航数据分离不意味着可以偷偷改岸线。当前岸线仍是安全核验依据；如果拟把素材细碎边缘改为更粗的轮廓、增加简化误差或再扩大禁行带，应先展示最大偏差、窄道与路径对比，再确认规则。普通 RDP 简化本身没有“只向水外保守扩张”的承诺，不能仅凭点数下降宣称安全。保持现有 Round 输入的原生分块，属于优先验证的内部组织优化。

### 原生分块如何保留连接

Recast 官方分块示例把输入范围扩大到邻块，给半径处理预留边框，最终不在边框区生成多边形；它还按扩大后的范围筛选源三角形。这样块内处理可看到邻域障碍。Detour 的 `connectExtLinks` 则由库识别带方向的边界 portal 并计算重叠范围；不是将两块中心连一条直线。这里用于交叉印证分块方法，不作为改用 Recast 的建议。[Recast 建块源码](https://github.com/recastnavigation/recastnavigation/blob/main/RecastDemo/Source/Sample_TileMesh.cpp#L758-L899)、[Detour 边界连接源码](https://github.com/recastnavigation/recastnavigation/blob/main/Detour/Source/DetourNavMesh.cpp#L286-L432)

Godot 官方 2D 示例已经提供对应入口：统一源几何，`baking_rect` 向邻块扩大，`border_size` 使用同样距离使成品回到目标块；成品顶点按地图单元的十分之一吸附，关闭额外 edge margin 连接。文档要求烘焙范围包含足够邻域，避免半径处理使块界出现缝隙。示例的绝对尺寸不是本项目参数，应一致换算导航单位。[固定示例](https://github.com/godotengine/godot-demo-projects/blob/b761b4cd5718a7118f2c95eb55f181d0a5f004f1/2d/navigation_mesh_chunks/navmesh_chunks_demo_2d.gd#L18-L83)、[官方分块说明](https://docs.godotengine.org/en/stable/tutorials/navigation/navigation_using_navigationmeshes.html#baking-navigation-mesh-chunks-for-large-worlds)

但“看起来共边”不等于引擎已经连接。指定引擎的 `EdgeKey` 由两个端点的格键组成，无向排序后整体比较；关闭 margin 后，只合并完整键相等的边。由此推断，一侧为 A—C、另一侧为 A—B 与 B—C 时，仅共线不能保证连接。吸附处理量化，不能自动统一这种分段差异。应在真实就绪后检查跨块失败处的两侧边端点与键，而不是先加人工 link。[边键定义](https://github.com/godotengine/godot/blob/ed1daf0bf/modules/navigation_2d/nav_utils_2d.h#L42-L69)、[边匹配实现](https://github.com/godotengine/godot/blob/ed1daf0bf/modules/navigation_2d/2d/nav_map_builder_2d.cpp#L94-L171)

额外 edge margin 会对剩余自由边做双重遍历，且可以跨间隙连接；它不是免费修复分块的开关。保持关闭并验证正常共边，有助于同时保留安全约定和控制同步成本。[固定实现](https://github.com/godotengine/godot/blob/ed1daf0bf/modules/navigation_2d/2d/nav_map_builder_2d.cpp#L172-L241)

烘焙完成、区域同步、地图发布是三个步骤。指定源码中区域可在后台构建，完成后才增加自己的 iteration，并请求地图更新；地图自己的 iteration 非零，只说明曾发布过某次快照。因此固定等两帧或只看地图 ID，不能证明所有新区域已进入最新地图。应观察全部非空区域的同步事实，再验证地图中这些区域的真实几何可查询，整个过程仍等待正常物理帧并支持取消。[区域同步源码](https://github.com/godotengine/godot/blob/ed1daf0bf/modules/navigation_2d/nav_region_2d.cpp#L182-L276)、[地图快照构建](https://github.com/godotengine/godot/blob/ed1daf0bf/modules/navigation_2d/nav_map_2d.cpp#L265-L348)、[区域 iteration API](https://docs.godotengine.org/en/stable/classes/class_navigationserver2d.html#class-navigationserver2d-method-region-get-iteration-id)

实现组后续探针已报告：576 个区域在第 5 个物理帧全部完成同步，随后近景和四角跨块路线完整到达且通过原岸线净空检查。首轮全部空路线来自过早查询，而非已证实的共边故障。这支持等待真实同步的方向，尚不替代正式集成和全量验收；共边分段保留为源码明确的诊断项，不作为本次失败原因。

### 安全核验先筛空间候选，不降低几何精度

分块建网之后仍有另一个独立成本：若原生路线包含大量细小线段，对每段扫描整个池塘甚至全图岸段，原岸线安全核验会重复大量无关距离运算。减少这部分成本不必改变路线或岸线。

0 A.D. 的 `CCmpObstructionManager::TestLine` 先取整段的轴对齐包围盒，并向四侧扩大本次半径，再向空间分区查询候选，最后对候选执行具体形状测试。这是可以直接借鉴的宽阶段筛选；不能照搬它的方形障碍测试来替代本项目的圆脚底扫掠。[固定实现](https://github.com/0ad/0ad/blob/61a3b9507d974084e6badb88a0826bd89a6d5b8b/source/simulation2/components/CCmpObstructionManager.cpp#L786-L829)

其 `SpatialSubdivision.GetInRange` 只读取查询矩形覆盖的桶，按稳定 ID 排序去重；坐标转换专门处理落在分区边界上的对象，避免边界漏查。Detour 也使用包围盒查询与块内 BVTree 剔除不相交候选，说明候选筛选应在精确运算之前；本项目不必因此引入第二套导航依赖。[0 A.D. 空间分区源码](https://github.com/0ad/0ad/blob/61a3b9507d974084e6badb88a0826bd89a6d5b8b/source/simulation2/helpers/Spatial.h#L226-L281)、[Detour 包围盒查询](https://github.com/recastnavigation/recastnavigation/blob/main/Detour/Source/DetourNavMesh.cpp#L769-L835)

对 #110 的建议是先检查是否已有池塘、格子或岸段包围盒索引可以复用；否则只建立一次静态固定网格索引，保存原始岸段 ID。每条路线段以检查半径扩张包围盒，仅枚举可能相交的原岸段，继续执行原距离判断。半径、1/256 格建网余量、原生 A*／Funnel 和经营路线均不变，空间桶大小仅影响候选数量。该建议是常规几何宽阶段，不产生路线、不修补失败路径。

正确性验证须将索引结果与原完整扫描作差分比较，覆盖桶边界、横竖线、斜线、极短线、跨多桶线、近岸相切和完全落水端点；不得只记录平均耗时。包围盒筛选须保守包含边界，不能漏掉恰好在半径阈值附近的岸段。原端点／内部判定继续保留，因为完全处于水中的线段可能不与任何岸线相交。记录每路线总段数、候选岸段数、实际距离计算次数和耗时，才能区分原生查询与项目安全核验的开销。

视觉微扰与导航精度解耦仍是另一项决策：它可能同时降低原生搜索和路线点数，但会改变近岸可行走区域。空间索引先消除重复扫描，保留目前几何；只有确实测得原生路线复杂度仍影响经营或观感时，再提出有误差界限的几何调整并确认，不能以“成熟游戏都简化”替代本项目的安全证明。

### 对 #110 的执行顺序与确认边界

| 次序 | 工作与验证 | 对已确认方案的影响 |
| --- | --- | --- |
| 1 | 同一 Round 障碍输入使用 Godot 分块 API；分别记录预处理、各块烘焙、区域同步、地图发布与首次查询耗时 | 保持原生导航、同一岸线和现有安全余量；不改寻路规则 |
| 2 | 同块两点、干地跨单边、跨角、湖岸穿块界、整图对角线依次验证；保留真实起终点与全段扫圆检查 | 先区分未就绪、分块断链和不安全路线，不用部分路径冒充到达 |
| 3 | 在同一输入上比较块尺寸，记录总多边形、边数、内存及最大单块耗时；查询含可达和真实不可达用例 | 选择数据支持的尺寸，避免“块越小越快”的假设；最终仍需完整经营和图形验收 |
| 4 | 静态资源每局只建一次；安全核验先筛空间候选并与全扫描差分验证；每次任务取得一次完整路线后按累计路程消费 | 保持现有几何、任务与时间契约；无需道路代价、Crowd、动态避让 |
| 5 | 若仍需降低岸线细节，再提出明确误差上限及保守性证据，用同组测试比较 | 会影响目前几何契约的部分必须先确认；不默认批准更大退让、窄道删除或自研 portal |

离线保存整局导航只能加速重复相同几何的加载，不能免除新种子首次建网；正式地图依种子和配置生成，本轮不凭“经典游戏预烘焙”增加未经需求验证的磁盘缓存系统。以上建议优先复用引擎 API，完整路径与安全测试通过前不宣告分块优化完成。
