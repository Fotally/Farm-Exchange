# 清亮 v2 世界静态素材来源

已接入 #106，入口为 `scripts/world/WorldMap.cs`；仅采用固定 q0，其他朝向仍留在原包。来源 `assets/FarmExchange-Bright-Complete-v2.zip`，包根 `FarmExchange-Bright-Complete-v2/`，内部版本 2.0.0，ZIP SHA-256 为 `6e9375c1db032e80cc01d228e67973b39b1d81c0cbbaa1d106b7efdb1a751f85`。权威清单为 `default/static/manifest.json` 与 `default/static/terrain/manifest.json`。

PNG 字节保持原样，仅复制并按项目用途重命名；不裁透明边、不缩放、不补底色、不旋转。WorldMap 设置 nearest，Godot 纹理导入保持 `mipmaps/generate=false` 默认值。主验收 1920×1080，另检查整数像素倍率。

静态建筑/农田基于项目此前使用 OpenAI imagegen 生成的图，地形为程序化生成；以原包 `docs/SOURCES_AND_LICENSES.md` 为来源记录，不为第三方 NPC 扩大许可。原 ZIP 与独立示例不作运行依赖，四个导出预设显式排除 `assets/*.zip`、`assets/source/*`、`assets/npc/*`。保留用户原始资料在原位置。
## 路径与规格

`default/static/farm/assets/q0/field_{dry,wet}.png` → `assets/gameplay/soil/field_{dry,wet}_q0.png`，共2张。

画布224×192，pivot=(112,128)对齐3×3农田工作中心。土层固定在草地/道路之上、全部建筑/作物/人物之下；湿润由实际 `HasWater` 决定，空田也可保持湿润。
