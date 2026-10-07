# 清亮 v2 世界静态素材来源

已接入 #106，入口为 `scripts/world/WorldMap.cs`；仅采用固定 q0，其他朝向仍留在原包。来源 `assets/FarmExchange-Bright-Complete-v2.zip`，包根 `FarmExchange-Bright-Complete-v2/`，内部版本 2.0.0，ZIP SHA-256 为 `6e9375c1db032e80cc01d228e67973b39b1d81c0cbbaa1d106b7efdb1a751f85`。权威清单为 `default/static/manifest.json` 与 `default/static/terrain/manifest.json`。

PNG 字节保持原样，仅复制并按项目用途重命名；不裁透明边、不缩放、不补底色、不旋转。WorldMap 设置 nearest，Godot 纹理导入保持 `mipmaps/generate=false` 默认值。主验收 1920×1080，另检查整数像素倍率。

静态建筑/农田基于项目此前使用 OpenAI imagegen 生成的图，地形为程序化生成；以原包 `docs/SOURCES_AND_LICENSES.md` 为来源记录，不为第三方 NPC 扩大许可。原 ZIP 与独立示例不作运行依赖，四个导出预设显式排除 `assets/*.zip`、`assets/source/*`、`assets/npc/*`。保留用户原始资料在原位置。
## 路径与规格

`default/static/terrain/terrain_81_mesh.png` → `assets/gameplay/terrain/terrain_81_mesh.png`。

64×32菱形基础格，图集九列，padding=2，stride=68×36。四角编码 `top+right*3+bottom*9+left*27`，grass=2，因此现有全可经营地图只用code=80。变体取0或1（稳定格坐标散布），图集索引为 `code+81*variant`。未启用水面、岸线或另一套水域经营规则。
