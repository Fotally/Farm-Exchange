# UiIcons 接口

对应 `scripts/ui/UiIcons.cs`。这是 UI 内部图标入口，统一复用 [界面原型](../main/prototype-visual.html) 的 15 个线条 symbol，不使用 Unicode 方格、锤子或暂停字符替代图形。

`UiIcons.Create(UiIcon, float size = 21, Color? color = null)` 返回忽略鼠标的 `TextureRect`：图形保持正方形框中的原比例，尺寸使用 `CustomMinimumSize`，默认调色为深棕正文色。`UiIcons.Texture(UiIcon)` 返回同一资源的缓存纹理，供原生按钮使用。`UiIcon` 包含种芽、建造工具、库存箱、商店、日历、金币、工人、暂停、继续、关闭、箭头、水滴、小麦、磨坊与回中心；命名仅在 UI 内部使用。

图标从 `assets/ui/icons/*.svg` 加载，SVG 路径和描边直接来自本项目 HTML，白色描边通过控件调色；来源、处理和用途见 [素材说明](../../../../assets/ui/source_notes.md)。缓存只保存纹理，不保存经营数据或控件实例。

图标尺寸由整体 UI 倍率调整，字体倍率不改变图标；所有控件依照共享 `UiScaling` 与统一主题参与真实排版。按钮使用图标时应通过 `icon_max_width` 主题常量明确限定图宽、允许展开并保留原比例，不能根据当前字体大小重新推算图标尺寸。原型内的设施插画由 [FacilityPreview](../facility-preview/interface-facility-preview.md) 处理。
