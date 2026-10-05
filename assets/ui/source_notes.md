# UI 图标与设施示意来源

本目录仅用于 UI，地图与人物继续使用原素材。所有图形来自本项目已确认的 [#94 像素田园 HTML 原型](../../docs/architecture/ui/main/prototype-visual.html)，没有外部字体、下载素材或第三方授权依赖。

- `icons/*.svg`：逐一提取原型 `i-leaf` 到 `i-reset` 的 15 个内嵌 symbol；保留 24×24 viewBox、1.7 线宽和圆端点，改为白色描边供 Godot 控件调色。资源尺寸为 96×96，显示尺寸由 `UiIcons.Create` 或按钮图标宽度控制。
- `facilities/field_*.svg`：复用 `fieldArt` 的 175×110 等距田地与 `plant`、像素版 `wheat`；七作物颜色及形状沿用原型。`field_empty` 去除作物，`field_seeded` 仅显示苗点，分别对应真实未播种与已播种待水阶段。生长示意只表达本轮作物种类，实际进度始终以快照的进度条与剩余天数显示。
- `facilities/crop_*.svg`：使用原型七种作物形状，供选种卡片展示；不成为第二份作物定义或生产数据。
- `facilities/mill.svg`、`workshop.svg` 与 `road.svg`：复用原型 `buildingShape`、`millArt` 和道路目录示意。加工场地中的小麦为风车磨坊，其余为原型共享工坊外形；不新增地图建筑图集。

`FacilityPreview` 只读取本次传入的空间、作物和阶段快照，显示实际占地与锚点；静态 SVG 不含报价、进度、库存、日期或其他示例经营状态。图形几何随整体 UI 倍率调整，字体倍率只影响标签。
