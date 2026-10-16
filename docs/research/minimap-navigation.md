# 大小地图：局部查看与镜头导航调研

调研日期：2026-10-12。关联 [issue #153](https://github.com/Fotally/Farm-Exchange/issues/153)。本文记录来源、候选方案和待确认项，不代表功能已经实施；现行镜头规则仍见[地图与镜头](../gameplay/world/map-and-camera.md)，范围依据见[系统规划](../project/roadmap.md)。

## 计划入口与调研适用范围

本阶段已采用左下角固定全图小地图与 B 全屏大地图分工，去掉区域标签，左键短按定位、按住拖动浏览；打开大地图不暂停经营，关闭重开保留本局倍率与查看位置，新开一局重置。完整确认范围、原型假设和遗漏检查以[大小地图规划](../project/map-navigation-proposal.md)为唯一计划入口，键鼠映射见[登记表](../project/input-bindings.md)。2026-10-15 已明确实施规则，正式接入与验收进度见规划页。

以下是外部实现的研究证据，不能把其右键浏览、窗口尺寸或倍率参数直接当成本项目规则。

## 来源对比

| 参考 | 已核实行为 | 对本项目的价值与边界 |
| --- | --- | --- |
| OpenTTD：SmallMap，开源 | 独立小地图有分档缩放；滚轮以鼠标位置为锚，缩放按钮以面板中心为锚；左键点击/拖动移动主视口，右键拖动平移小地图底图 | 与“局部缩放＋导航”最接近；其窗口构造会初始化倍率，不能直接作为重开记忆倍率的依据 |
| OpenRA：RadarWidget，开源 | 非行动按钮按下与持续移动重定位主镜头，释放结束；地形、单位等图层与主视口框分别绘制 | 适合参考镜头导航、坐标转换和视口框；所查控件按全图适配面板，没有用户独立缩放分支 |
| 《帝国时代 II：决定版》，官方教程 | 右下角实时小地图，点击立即移动主视野，有显示过滤 | 证明经典策略游戏的全图导航方式；该教程未证明拖动或小地图独立缩放 |
| Factorio 2.1.7，官方发布说明 | 侧栏小地图支持 Shift＋滚轮缩放 | 证明常驻小地图可以独立缩放；该说明未证明 RTS 式点击、拖动主镜头，不能把它与远程地图混为一谈 |

### OpenTTD：最接近所需交互的完整参考

核实提交：`e52a422015e8ed89413ff841ecf878afcf833d38`。

1. `SmallMapWindow` 自己维护倍率和偏移。`SetZoomLevel` 使用 `{1, 2, 4, 6, 8}` 采样档位，数值越大看得越远；改变倍率前后换算同一个锚点，并补偿偏移。[倍率与锚点实现，第 1213—1258 行](https://github.com/OpenTTD/OpenTTD/blob/e52a422015e8ed89413ff841ecf878afcf833d38/src/smallmap_gui.cpp#L1213-L1258)
2. 左键捕获后，将小地图坐标转成世界坐标并调用主窗口滚动；窗口框架在按住移动时继续派发点击，松开解除捕获。[导航入口，第 1705—1718 行](https://github.com/OpenTTD/OpenTTD/blob/e52a422015e8ed89413ff841ecf878afcf833d38/src/smallmap_gui.cpp#L1705-L1718)、[持续拖动派发，第 2412—2438 行](https://github.com/OpenTTD/OpenTTD/blob/e52a422015e8ed89413ff841ecf878afcf833d38/src/window.cpp#L2412-L2438)
3. 加减按钮锚定面板中心；滚轮在相应设置允许时锚定鼠标点。右键滚动改变小地图偏移，区别于移动主镜头。[按钮，第 1721—1725 行](https://github.com/OpenTTD/OpenTTD/blob/e52a422015e8ed89413ff841ecf878afcf833d38/src/smallmap_gui.cpp#L1721-L1725)、[滚轮与底图平移，第 1857—1886 行](https://github.com/OpenTTD/OpenTTD/blob/e52a422015e8ed89413ff841ecf878afcf833d38/src/smallmap_gui.cpp#L1857-L1886)
4. 新建该窗口时初始化倍率并对准当前主视口，因此“重开恢复倍率”需要本项目另行明确。[构造与定位，第 1463—1495 行](https://github.com/OpenTTD/OpenTTD/blob/e52a422015e8ed89413ff841ecf878afcf833d38/src/smallmap_gui.cpp#L1463-L1495)

### OpenRA：导航与简化图层参考

核实提交：`e180e3225f3930272dd451917467631618f8976f`。

1. `RadarWidget` 将输入点转换为世界坐标，导航按钮按下或保持拖动时调用 `Viewport.Center`，抬起清除移动状态。按钮取决于鼠标控制配置，并非所有配置都固定左键。[输入处理，第 326—367 行](https://github.com/OpenRA/OpenRA/blob/e180e3225f3930272dd451917467631618f8976f/OpenRA.Mods.Common/Widgets/RadarWidget.cs#L326-L367)
2. `previewScale` 由地图范围与控件尺寸取较小适配比例，输入处理没有缩放分支；不要将主镜头缩放描述为此控件支持局部缩放。[全图适配，第 225—233 行](https://github.com/OpenRA/OpenRA/blob/e180e3225f3930272dd451917467631618f8976f/OpenRA.Mods.Common/Widgets/RadarWidget.cs#L225-L233)
3. 地形变化更新对应格子颜色；绘制时依次叠加地形、实体、迷雾和主视口矩形，导航不需要再渲染一份完整世界场景。[格子更新，第 95—103 行](https://github.com/OpenRA/OpenRA/blob/e180e3225f3930272dd451917467631618f8976f/OpenRA.Mods.Common/Widgets/RadarWidget.cs#L95-L103)、[绘制，第 370—395 行](https://github.com/OpenRA/OpenRA/blob/e180e3225f3930272dd451917467631618f8976f/OpenRA.Mods.Common/Widgets/RadarWidget.cs#L370-L395)

### 商业游戏的官方交互依据

《帝国时代 II：决定版》的官方教程 Lesson 7 明确说明点击小地图将主视野移动到对应地点，并展示经济/军事等过滤方式。本次只借鉴清晰的全图导航和信息分层；教程未说明的能力不作推断。[官方教程：The Mini-Map Explained](https://www.ageofempires.com/learn-to-play/control-resources-aoe2/)

Factorio 官方账号 FactorioBot 发布的 2.1.7 更新说明（2026-06-23）明确加入侧栏小地图的 Shift＋滚轮缩放。本项采用这一具体版本的证据，不把其他版本控制表中的不同组合键混写为统一现行规则；也不据此宣称该小地图支持自由主镜头导航。[官方 2.1.7 发布说明](https://forums.factorio.com/134095)

## 本项目采用与未定内容

当前采用的 B 全屏方向及左键短按／拖动分工已记录到[大小地图规划](../project/map-navigation-proposal.md)。OpenTTD 为独立缩放、浏览与主镜头导航提供参考，OpenRA 为简化图层与视野框提供参考；本项目按已确认的左拖操作组织交互，不照搬案例键位。

[1080P 原型](../architecture/ui/main/prototype-maps.md)保留 A／C 布局供对照，B 为本阶段基线。原型浏览器检查已经完成，但只使用演示数据；倍率与查看位置记忆、新局重置和打开地图不暂停已确认，缩放上限、窗口共存及正式数据刷新等规则已在规划页明确；本研究记录不替代正式游戏验收。
