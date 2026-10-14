# #149 建造目录缩略图可丢弃原型

状态：仅预览，未修改生产代码；待用户确认 A / B、详情窗是否同步后再实施。

运行：直接打开 `thumbnail-prototype.html`；重建：`python build/issue149-prototype/make_prototype.py`。

同页以 `?variant=A`、`?variant=B`、`?variant=C` 切换；底部箭头及键盘左右键也可切换。分类支持 `category=all/farm/processor/road`。全部 14 项通过分类、滚动和搜索查看。卡片点击仅回显名称。

- A：正式七加工 q0 PNG＋三格右下短直道路，推荐。
- B：同一加工图＋单格孤立道路。
- C：当前 SVG 图源对照，不代表当前 Godot 排版精确截图。
- 790×620 原目录大小，最低 220×200 卡片，三列；建议展示区 145×108，PNG 非透明内容等比居中底对齐、最近邻，源 PNG 不改动。
- 七建筑源 `assets/gameplay/buildings/{crop}_workshop_q0.png`，原画布均 256×240，裁区见 `source-metrics.json`。实际磨坊、制糖坊动画使用拆分层，但静态 q0 完整图仍用于地图候选，适合 UI 缩略图。
- 五道路源 `assets/gameplay/terrain/roads/road_{material}_atlas.png`，按同目录 `atlas_regions.json` 的清单定位区域，variant 0。单格 mask 0；短路段 mask 2 / 10 / 8，依次偏移 (0,0)/(32,16)/(64,32)，完整组合为 128×64，再按非透明内容取图。
- 当前对照来自 `assets/ui/facilities/mill.svg`、`workshop.svg`、`road.svg`；农田沿用 `field_wheat.svg`。拆路角标为原型内简单工具图示，需用户确认后在现有 UI 图示体系中实现。
- 图源和主题来自实际项目资源、`BuildCatalogWindow.cs` 与 `UiElements.cs`。费用：设施每座 10.00，道路每格 1.00；拆除不退款。

确认事项：选择短路段或单格道路、认可建筑裁区及缩略比例、是否将加工/道路详情同步到正式图。农田与选种图不在本次替换范围。
