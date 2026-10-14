# FacilityPreview 接口

对应 `scripts/ui/FacilityPreview.cs`，内部绘图控件为 `scripts/ui/FacilityThumbnail.cs`。`FacilityPreview` 是详情中的设施 UI 示意：107 像素最低基准高度的纸色画布、等距设施图与底部占地／真实锚点标签。标签按主稿 1080 高度归一使用 13 字号、横向 12 内距、底部 7 像素留白，关闭自动折行并显示完整锚点。它不绘制地图、不决定设施占地，也不保存作物、加工批次或示例经营状态。

`FacilityPreview(BuildingKind)` 在构造时指定详情设施类型，默认农田。农田保持原 SVG 与原画布排版；加工及道路在纵向容器中分配至少 64 像素的独立图片区和实际字号决定的标签区，区域间隔 4 像素。字号增大时容器自然增加最低高度，仍由详情外层滚动到达，不把文字覆盖到图片上。

`Refresh(BuildingSpaceSnapshot, CropKind, CropStage)` 接受同一已选实例的空间、作物种类和本轮阶段，更新已有图与标签；构造与刷新必须对应同一种设施。农田未播种显示空田，待水显示苗点，生长中显示所选作物形状；成熟度始终由详情的真实剩余与进度条表达，不把静态作物图当作精确完成度。七加工分别使用正式完整 q0 PNG，道路从空间快照的真实 `RoadMaterial` 选择同形三格短直道路，不按默认材质替代缺失字段；占地与锚点始终来自正式空间快照。

`FarmDetailsPanel.RefreshCultivation` 读取已选农田快照后调用；`ProcessorDetailsPanel.RefreshPlacement` 和 `RoadDetailsPanel.RefreshPlacement` 接受 `Main` 已选实例的空间快照。点击任一子格显示同一实际锚点，不从点击位置虚构第二个设施。

内部 `Texture(BuildingKind, CropKind, CropStage, RoadMaterial)` 按逻辑设施描述选择图；`RoadRemovalTexture()` 提供独立拆路工具示意。`CropTexture(CropKind)` 与农田分支继续加载 `assets/ui/facilities/` 原 SVG，不改变选种、农田及地图素材。目录和详情复用同一份只读纹理缓存，刷新不重建控件，调用方不得修改缓存纹理。

加工原图为 `assets/gameplay/buildings/{crop}_workshop_q0.png`，按非透明内容取图，再以最近邻等比缩放至 145×108 透明 UI 画布，水平居中、底部保留 4 像素；原图 256×240 画布、透明边、pivot 及导入设置保持。完整 q0 图包含建筑特征，UI 不播放地图生产动画，也不增加旋转操作。

道路读取 `assets/gameplay/terrain/roads/atlas_regions.json` 的正式区域，使用相同 variant 0、mask 2／10／8，以屏幕位移 (0,0)／(32,16)／(64,32) 合成三格右下短直路段，再适配同一 UI 画布。五材质分别使用各自正式图集；短路段只帮助辨认，不表示三格占地或改变逐格费用。拆路用土路三格短直路段加红色角标，复用现有 `UiIcon.Build` 工具图标。派生图仅在内存缓存，不新增正式 PNG 或改变地图道路连接规则。

纹理缓存和正式图源保持只读：`GetImage()` 的返回值不能直接当作可写独立副本。拆路背景写色与工具图缩放前，分别以完整 `GetRegion` 创建独立像素副本；所有 `FillRect`、`Resize` 和 `BlendRect` 只修改这些副本，不修改缓存土路、工具 SVG 或其他图源。该约定同时适用于有窗口和 headless 后端，不依赖特殊分支。

`FacilityThumbnail` 仅在加工／道路详情中读取缓存纹理，按控件实际大小等比居中底对齐，使用最近邻。图区和标签分别由容器排版，几何随统一整体倍率，文字随独立字体倍率。素材复用说明见 [来源说明](../../../../assets/ui/source_notes.md)。

该模块使用普通 Godot 纹理与标签，不自行绘制文字，不建立另一套缩放算法。整体 UI 倍率改变几何与插画大小；字体倍率只影响占地标签，统一由共享 `UiScaling` 处理。

`tests/e2e/TestFacilityThumbnails.cs` 在创建主场景／目录前，通过既有纹理入口快照土路与工具 SVG 的实际图像字节和尺寸，主动生成拆路图后检查两源完全不变；独立进程因此覆盖首次派生，不通过反射清除缓存。随后通过真实主场景验证七图对应正式源、最近邻色值、五材质区别及拆路角标，检查角标以外像素与原土路短直路段一致，并核对目录原操作、实际实例的占地／锚点和图文分区。截图夹具经真实暂停按钮刷新，并检查模型暂停、继续按钮图标／提示、顶部经营与工人暂停文字一致。独立场景为 `res://tests/e2e/test_facility_thumbnails.tscn`；有窗口运行时固定 1920×1080，截图写入 `build/issue149-validation/`。测试与截图入口存在不代表验收已通过，实际运行结果由议题完成记录维护。
