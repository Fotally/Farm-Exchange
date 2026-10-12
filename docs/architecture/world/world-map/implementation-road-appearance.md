# 道路连接与操作完成后外观

履行[WorldMap接口](interface-world-map.md)，关联[#143](https://github.com/Fotally/Farm-Exchange/issues/143)。道路继续是单格纯布局设施，经营与材质权威由土地快照提供；表现没有路段归属、移动收益、寻路、自动绕障碍或已建材质替换。

## 内部投影与拓扑

WorldMap内部RoadAppearance读取实际空间快照，仅保留道路格与材质投影。空间快照引用改变才同步，按新旧道路的格/材质差异产生失效格和四邻；设施状态变化不重建道路。每个MapChunk保存RoadTile（材质、mask、稳定variant、候选/待拆标记）；相等状态不触发QueueRedraw，屏外脏块进入视野再绘制。

四邻为N=(0,-1)、E=(1,0)、S=(0,1)、W=(-1,0)，bit分别1/2/4/8。不检查材质是否相同，所以异材质邻格同样相连。实际存在且无邻路得到mask=0；不存在的格不绘制道路。16拓扑无需旋转PNG或引用建筑q0～q3。

稳定变体采用格坐标与固定视觉种子：`h=(uint)x*73856093 XOR (uint)y*19349663 XOR 0x143`（溢出按uint环绕），再`h ^= h >> 13`、`variant=h & 3`。材质、连接、镜头、经营随机和候选生命周期都不参与变体选择。预览与正式道路走同一GetTile计算，不逐帧重新抽取随机数。

五张atlas及320个区域在SetGame时加载；每个AtlasTexture的Region直接来自[运行映射](../../../../assets/gameplay/terrain/roads/atlas_regions.json)的manifest原值。四个导出预设include_filter均显式包含这份JSON，使Windows/macOS的正式与开发包加载同一区域映射；PNG沿用Godot正式资源导入。道路中心沿现有MapCoordinates公式定位，完整64×32区域以pivot(32,16)对齐。资源及校验、许可见[道路来源](../../../../assets/gameplay/terrain/roads/source_notes.md)。

## 新建与拆除预览

Main传入完整道路候选或拆除投影路径，RoadAppearance只读查询实际占地。新建将地图内无设施占用的格加入有效候选；已有路、农田、加工和越界均只留下红格提示，不作为新路覆盖。因此旧路仍使用原材质，邻接有效新路时只变化mask。所有有效候选相互连接，暂时缺口不会隐含绕障碍。

新建与拆除预览都先使用 [PlacementRules](../../land/placement-rules/interface-placement-rules.md) 的内部纯判断 `IsValidRoadMaterial`，合法来源为材质枚举的真实定义，不维护固定数量或第二套列表。负值及未定义正值在预览状态修改前抛出 `ArgumentOutOfRangeException`，参数名仍为 `material`；拆除模式也保持原有检查。土地仍唯一拥有正式材质，外观只维护投影。

拆除仅将范围内实际道路列入待拆集合，空格和其他设施不标红。保留道路按“实际道路减待拆集合”计算连接；待拆道路显示原正式mask/材质的红色半透明图，红格提示说明操作范围。两种预览不登记占用、不改金币/库存/生产、不触碰EnvironmentDecorations。

每次候选更新同时撤销原范围并投影新范围，只刷新其并集的四邻；MapChunk最后状态相等则不重绘。取消、隐藏、拖动镜头/界面遮挡期间调用ClearRoadPreview，恢复真实mask；工具的当前段仍由Main持有。提交后SyncFromGame按真实快照差异刷新，清理当前预览后得到与有效候选相同mask/材质/variant。跨缓存块连接通过全地图道路投影查询，失效格再分发到各块。

道路纹理位于地面Z=-2、草地之上、土层与设施人物之下；有效候选alpha=.65，待拆图alpha=.55，旧路连接变化保持正式不透明材质。Z=11仅绘制候选颜色/边线提示，使冲突可读。未确认材质分界、自然过渡、门前入口和中心散石仍不启用。

## 验收入口

`TestRoadAppearance.CheckAllMasks`覆盖五材质×16拓扑、混材质四邻、存在孤路与空格区分、稳定四变体。`CheckPreview`经真实FarmGame/WorldMap验证跨8×8块新路内部与旧路连接、占用/越界排除、移动和隐藏清理、重复预览不重绘且资源/空间引用不变、新建和拆除提交与预览一致。现有`TestRoadMap`调用这些检查并继续核验原单格选择与拆除、生产设施跨块占地和选框，不再断言旧灰色路面。

独立场景`tests/integration/test_road_appearance.tscn`和原`test_road_map.tscn`可headless执行状态断言；有窗口执行实际PNG区域不透明像素对照并保存`coverage/road-connections-{build,remove}-{preview,after}.png`及原道路前后图。必须至少1920×1080，headless不作为图形像素验收。

原色与选框在各自实际状态核对：先选择道路并对完整金色外围取样，保存`road-map-before.png`；随后ClearSelection并等待真实渲染，再对两格道路的全部既定原图样点核对材质/mask/variant，保存`road-map-texture-before.png`。连接端部位于格边，Z=10金框正确覆盖该处，不能把它当作Z=-2路面原色；也不跳过端部样点或提高色容差。纹理失败输出格、材质、mask、variant、来源/屏幕坐标及expected/actual，并保存`coverage/road-texture-failure.png`。

道路FPS独立入口`tests/performance/test_road_fps.tscn`需要真实窗口：1920×1080真实Main、45格五材质横纵交汇路（跨8×8块），暂停经营但保留实际UI/地图；每秒切换移动L形新建/直线拆除预览并平移镜头。预热2秒、采样8秒，关闭VSync和FPS上限，保存`build/issue143-validation/road-performance/before.png`、`after.png`与`performance.json`，核对平均FPS≥60、P95≤16.67ms、两种预览均有采样以及正式45格/剩余5.00金币/暂停不变。它补充道路专项负载，不替代必跑满地图50tick和原满地图FPS前后同条件验收。

性能夹具完成实际道路建造后，通过真实PauseButton的Pressed信号一次暂停，由Main原有TogglePause同步刷新全部UI，再停止Main自动候选刷新并交由性能场景提供移动预览。不直接修改模型暂停后保留旧UI。采样开始/结束前同时断言实际可见金币文本/总余额冻结提示、0农田/0加工/45道路近况、日期及暂停状态文字、按钮“继续经营”提示；报告记录UiAtSampleStart/UiAtSampleEnd与UiConsistent。模型、界面与截图一致才满足性能验收，只有FPS达标不能记为通过。

该场景是暂停道路绘制专项：Main帧刷新关闭，地图、镜头、实际控件和当前道路预览继续渲染。报告明确记录MainProcessEnabled=false与IsPaused=true，不能将其FPS解释为活动经营含完整Main帧更新的性能；后者由原满地图FPS场景单独验收。
