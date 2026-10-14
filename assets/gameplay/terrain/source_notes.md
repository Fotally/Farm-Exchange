# 清亮 v2 世界静态素材来源

已接入 #106，#110 接入真实水域编码与水纹，入口为 `scripts/world/WorldMap.cs`；地形三变体不是建筑 q0～q3 朝向。来源 `assets/FarmExchange-Bright-Complete-v2.zip`，包根 `FarmExchange-Bright-Complete-v2/`，内部版本 2.0.0，ZIP SHA-256 为 `6e9375c1db032e80cc01d228e67973b39b1d81c0cbbaa1d106b7efdb1a751f85`。权威清单为 `default/static/manifest.json` 与 `default/static/terrain/manifest.json`。

PNG 字节保持原样，仅复制并按项目用途重命名；不裁透明边、不缩放、不补底色、不旋转。WorldMap 设置 nearest，Godot 纹理导入保持 `mipmaps/generate=false` 默认值。主验收 1920×1080，另检查整数像素倍率。

静态建筑/农田基于项目此前使用 OpenAI imagegen 生成的图，地形为程序化生成；以原包 `docs/SOURCES_AND_LICENSES.md` 为来源记录，不为第三方 NPC 扩大许可。原 ZIP 与独立示例不作运行依赖，四个导出预设显式排除 `assets/*.zip`、`assets/source/*`、`assets/npc/*`。保留用户原始资料在原位置。
## 路径与规格

`default/static/terrain/terrain_81_mesh.png` → `assets/gameplay/terrain/terrain_81_mesh.png`。

64×32菱形基础格，图集九列，padding=2，stride=68×36。四角编码 `top+right*3+bottom*9+left*27`，water=0、soil=1、grass=2；读取本局权威地形编码。变体取0～2（稳定格坐标散布），图集索引为 `code+81*variant`。草／土／水组合共用原 mesh atlas，水纹只做表现，不参与地形、供水或通行判断。

#143新增五材质基础道路放在`roads/`，使用独立道路包的四邻mask（不是地形四角编码）。五atlas、320区域映射、哈希、导入与授权见[道路来源说明](roads/source_notes.md)。原草地图集保持原样。

## #110 水 mask 与水纹

同一只读来源包按原字节提取两个实际使用文件：

| 包内路径（相对包根） | 项目路径 | SHA-256 |
| --- | --- | --- |
| `default/static/terrain/masks/water_81.png` | `assets/gameplay/terrain/water_81.png` | `10164916104339c19a394c028455947b831e013c4bb1b01f5bdb8d1fb4361833` |
| `default/static/terrain/ripples.png` | `assets/gameplay/terrain/ripples.png` | `491d6817e479f7e73fd0a9b3e2fc05a57a76090ae1a575a3c55b43da03c2909c` |

mask 为 612×324，81 组合共用同一岸线；mesh atlas 为 612×972，对应三变体。shader 将 atlas 的纵向 UV 映射到同一 mask，只在水内混合水纹。水纹原图 96×24，四块 24×24 是独立纹理片，不作为四帧动画解释。表现按世界坐标稳定选择纹理片，以六经营秒一轮的透明度变化显示细小波光；该周期与密度是表现内部参数，不是玩法配置。暂停冻结，所有倍率读取本局唯一驱动；各地图块共用时间和坐标相位。

入口为 `WorldMap` 与 `scripts/world/water_ripples.gdshader`。图片未裁切、缩放、旋转、改色或重绘；nearest 与关闭 mipmaps 沿用像素素材约定。原 ZIP 不变，不复制原包示例代码；授权与本目录已接入地形图集相同。

## #110 权威逻辑岸线模板

`shoreline_templates.json` 是仓库生成的有理逻辑岸线资源，由 `tools/Generate-TerrainTemplates.py` 依据原包 `default/static/tools/terrain_authoring/generate_ternary.py` 的作者公式独立提取并认证，不从 PNG 像素反推。包含 14 种混合水角及全水／全陆空轮廓，坐标分母为 2²⁴，同一组合三变体共用；原 PNG 保持不变。校准 Hausdorff 上界 1/2500 格仅描述素材曲线与模板的误差，不能作为导航 epsilon 或额外岸边退让。模板 SHA-256 为 `87ecce248a4bd0f5db9b65673c45a06a86ca61061dd3f9eb698550073bd1bb4d`，算法与认证依据见[静态地形实现](../../../docs/architecture/terrain/implementation-static-terrain.md)。
