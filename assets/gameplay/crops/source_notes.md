# 清亮 v2 世界静态素材来源

已接入 #106，入口为 `scripts/world/WorldMap.cs`；仅采用固定 q0，其他朝向仍留在原包。来源 `assets/FarmExchange-Bright-Complete-v2.zip`，包根 `FarmExchange-Bright-Complete-v2/`，内部版本 2.0.0，ZIP SHA-256 为 `6e9375c1db032e80cc01d228e67973b39b1d81c0cbbaa1d106b7efdb1a751f85`。权威清单为 `default/static/manifest.json` 与 `default/static/terrain/manifest.json`。

PNG 字节保持原样，仅复制并按项目用途重命名；不裁透明边、不缩放、不补底色、不旋转。WorldMap 设置 nearest，Godot 纹理导入保持 `mipmaps/generate=false` 默认值。主验收 1920×1080，另检查整数像素倍率。

静态建筑/农田基于项目此前使用 OpenAI imagegen 生成的图，地形为程序化生成；以原包 `docs/SOURCES_AND_LICENSES.md` 为来源记录，不为第三方 NPC 扩大许可。原 ZIP 与独立示例不作运行依赖，四个导出预设显式排除 `assets/*.zip`、`assets/source/*`、`assets/npc/*`。保留用户原始资料在原位置。
## 路径与规格

`default/static/farm/assets/q0/field_seeded.png` → `assets/gameplay/crops/field_seeded_q0.png`。

`default/static/farm/assets/q0/{crop}_growing_{01,02,03}.png` → `assets/gameplay/crops/{crop}_growing_{01,02,03}_q0.png`；crop为wheat、corn、rice、potato、sunflower、sugarcane、radish，共21张。

全部画布224×192，pivot=(112,128)，每田一张层图。待水显示通用播种层，Growing按精确 `GrowthProgress` 的 `[0,1/3)`、`[1/3,2/3)`、`[2/3,1)`选图；None无作物图。自动收获/清理沿经营状态立刻清空旧图。此阶段整田图以工作中心参与共同深度排序，不拆像素模拟单株。

#108 已在小麦、甘蔗、萝卜Growing时接入原包独立植株动效，静态原图不与植株重叠绘制；来源与根点见子目录 motion/source_notes.md，其余四种作物仍使用本组整田原图。
