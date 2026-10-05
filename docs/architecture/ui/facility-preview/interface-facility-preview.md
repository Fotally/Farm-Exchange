# FacilityPreview 接口

对应 `scripts/ui/FacilityPreview.cs`。`FacilityPreview` 是详情中的设施 UI 示意：107 像素基准高度的纸色画布、等距设施图与底部占地／真实锚点标签。标签按主稿 1080 高度归一使用 13 字号、横向 12 内距，底部 7 像素留白、21 像素基准标签区域，关闭自动折行并显示完整锚点。它不绘制地图、不决定设施占地，也不保存作物、加工批次或示例经营状态。

`Refresh(BuildingSpaceSnapshot, CropKind, CropStage)` 接受同一已选实例的空间、作物种类和本轮阶段，更新已有图与标签。农田未播种显示空田，待水显示苗点，生长中显示所选作物形状；成熟度始终由详情的真实剩余与进度条表达，不把静态作物图当作精确完成度。加工场地按原型区分小麦磨坊与共享工坊形状，道路使用灰色路面示意；占地与锚点来自正式空间快照。

`FarmDetailsPanel.RefreshCultivation` 读取已选农田快照后调用；`ProcessorDetailsPanel.RefreshPlacement` 和 `RoadDetailsPanel.RefreshPlacement` 接受 `Main` 已选实例的空间快照。点击任一子格显示同一实际锚点，不从点击位置虚构第二个设施。

内部 `Texture(BuildingKind, CropKind, CropStage)` 与 `CropTexture(CropKind)` 让目录和选种卡复用同一批 SVG。纹理按资源名称缓存，刷新不重建控件；资源在 `assets/ui/facilities/`，几何来源为原型的 `fieldArt`、`plant`、`wheat`、`buildingShape` 与道路目录，详见 [来源说明](../../../../assets/ui/source_notes.md)。这些图形只在 UI 使用，当前地图及人物素材保持。

该模块使用普通 Godot 纹理与标签，不自行绘制文字，不建立另一套缩放算法。整体 UI 倍率改变几何与插画大小；字体倍率只影响占地标签，统一由共享 `UiScaling` 处理。
