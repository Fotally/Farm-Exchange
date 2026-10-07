# 清亮 v2 世界静态素材来源

已接入 #106，入口为 `scripts/world/WorldMap.cs`；仅采用固定 q0，其他朝向仍留在原包。来源 `assets/FarmExchange-Bright-Complete-v2.zip`，包根 `FarmExchange-Bright-Complete-v2/`，内部版本 2.0.0，ZIP SHA-256 为 `6e9375c1db032e80cc01d228e67973b39b1d81c0cbbaa1d106b7efdb1a751f85`。权威清单为 `default/static/manifest.json` 与 `default/static/terrain/manifest.json`。

PNG 字节保持原样，仅复制并按项目用途重命名；不裁透明边、不缩放、不补底色、不旋转。WorldMap 设置 nearest，Godot 纹理导入保持 `mipmaps/generate=false` 默认值。主验收 1920×1080，另检查整数像素倍率。

静态建筑/农田基于项目此前使用 OpenAI imagegen 生成的图，地形为程序化生成；以原包 `docs/SOURCES_AND_LICENSES.md` 为来源记录，不为第三方 NPC 扩大许可。原 ZIP 与独立示例不作运行依赖，四个导出预设显式排除 `assets/*.zip`、`assets/source/*`、`assets/npc/*`。保留用户原始资料在原位置。
## 路径与规格

`default/static/buildings/png/{crop}_workshop_q0.png` → `assets/gameplay/buildings/{crop}_workshop_q0.png`；crop为wheat、corn、rice、potato、sunflower、sugarcane、radish，共7张。

全部画布256×240，pivot=(128,176)，对齐3×3加工场地工作中心。每实例使用真实加工品种的一张q0图，透明画布不等于占地；屋顶超出锚点所属块时仍完整显示。建筑整图与整田作物/人物按工作中心或脚根统一YSort，局部部件尚由后续 #108 接入。

#108 已将实际磨坊和制糖坊换为子目录 motion/source_notes.md 记录的拆分底图/活动层；本组q0仍为对应候选图来源，不与动效底图叠绘。其余五种实际设施保持本组静态图。
