# UI 图标与设施示意来源

本目录仅用于 UI，地图与人物继续使用原素材。所有图形来自本项目已确认的 [#94 像素田园 HTML 原型](../../docs/architecture/ui/main/prototype-visual.html)，没有外部字体、下载素材或第三方授权依赖。

- `icons/*.svg`：逐一提取原型 `i-leaf` 到 `i-reset` 的 15 个内嵌 symbol；保留 24×24 viewBox、1.7 线宽和圆端点，改为白色描边供 Godot 控件调色。资源尺寸为 96×96，显示尺寸由 `UiIcons.Create` 或按钮图标宽度控制。
- `facilities/field_*.svg`：复用 `fieldArt` 的 175×110 等距田地与 `plant`、像素版 `wheat`；七作物颜色及形状沿用原型。`field_empty` 去除作物，`field_seeded` 仅显示苗点，分别对应真实未播种与已播种待水阶段。生长示意只表达本轮作物种类，实际进度始终以快照的进度条与剩余天数显示。
- `facilities/crop_*.svg`：使用原型七种作物形状，供选种卡片展示；不成为第二份作物定义或生产数据。
- `facilities/mill.svg`、`workshop.svg` 与 `road.svg`：复用原型 `buildingShape`、`millArt` 和道路目录示意。加工场地中的小麦为风车磨坊，其余为原型共享工坊外形；不新增地图建筑图集。

`FacilityPreview` 只读取本次传入的空间、作物和阶段快照，显示实际占地与锚点；静态 SVG 不含报价、进度、库存、日期或其他示例经营状态。图形几何随整体 UI 倍率调整，字体倍率只影响标签。

## #149：目录与加工／道路详情复用正式素材

七加工改为读取 `assets/gameplay/buildings/{crop}_workshop_q0.png` 的完整静态 q0 图，五道路读取 `assets/gameplay/terrain/roads/road_{material}_atlas.png` 和正式 `atlas_regions.json`；来源、原包与授权继续分别以 [建筑说明](../gameplay/buildings/source_notes.md) 和 [道路说明](../gameplay/terrain/roads/source_notes.md) 为准，不新增外部图像、复制正式 PNG 或改变导入设置。

`FacilityPreview` 在运行时按非透明内容取 UI 区域，以最近邻等比放入 145×108 透明画布，水平居中、底部留 4 像素，只缓存内存纹理。五道路统一 variant 0、mask 2／10／8，并以 (0,0)／(32,16)／(64,32) 合成三格右下短直路段。原 PNG 字节、透明画布、地图 pivot、材质连接与正式占地不变；三格示意不改变每格费用。

拆路复用土路三格短直路段与既有 `icons/build.svg` 工具图标，在 UI 派生画布上增加红色角标；没有新增工具 SVG。背景写色和工具图重采样前均通过完整 `GetRegion` 获取独立像素副本，不直接编辑 `GetImage()` 返回的缓存数据，源纹理及源 SVG 的像素和尺寸保持。加工／道路目录和详情共享纹理缓存，详情读取真实材质和锚点。农田 `field_*.svg` 与选种 `crop_*.svg` 保持原图及原行为，历史 `mill.svg`／`workshop.svg`／`road.svg` 仍保留，已不作为上述加工／道路正式 UI 图源。正式地图及人物读取原有世界资源和动效。
