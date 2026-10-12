# 五材质基础道路来源

关联 [#143](https://github.com/Fotally/Farm-Exchange/issues/143)。来源包为本地只读 `assets/FarmExchange-Roads-Complete.zip`，包 ID=`farm_exchange_roads_complete`，包根 `FarmExchange-Roads-Complete/`，ZIP SHA-256=`17fd66b4546e9afdd05aa5d17f532df79f8db35df47916c73a9d682ac4ee69f6`。权威入口为根 `manifest.json`，路径、区域与哈希按其提取；未执行包内生成器、独立项目或验证代码。

只采用五张基础道路图集，代表五材质×16 mask×4变体共320张原单图；没有重复复制单图。PNG字节保持原样；`atlas_regions.json`只从manifest的道路条目提取material/mask/variant/atlas_region作为运行映射，没有按文件排序推断。

| 包内路径 | 项目文件 | SHA-256 |
| --- | --- | --- |
| assets/atlases/dirt.png | road_dirt_atlas.png | `8fa3ba47b98a77c9e1fba89e066dbff3470b742006bf0fd8e88ae38ae75d7dd8` |
| assets/atlases/gravel.png | road_gravel_atlas.png | `389cdefd562aaeeda31720163ab47386fcab6651e98102bfd9cb55a0629065d1` |
| assets/atlases/cobble.png | road_cobble_atlas.png | `d6e0dee56bbe17f3fb49da945ea9446f65bed7564546e132aad70a82009fbfce` |
| assets/atlases/brick.png | road_brick_atlas.png | `e1cc890e57f4935aa513a8cc6e5b7693ce4824212bf4aab946c27ca56a2823de` |
| assets/atlases/slab.png | road_slab_atlas.png | `1a496de0cfc484e0d803c5ea800d262ab55edb7197e00e78b03f67ec45840665` |

图集544×288，8列、padding=2、stride=(68,36)，实际区域64×32 RGBA且pivot=(32,16)。WorldMap使用Nearest；五份PNG导入配置明确Lossless（compress/mode=0）与mipmaps/generate=false。AtlasTexture直接使用清单区域，保留透明画布与二值alpha，不裁切像素、不旋转PNG伪造连接。

N/E/S/W mask位为1/2/4/8，对应屏幕右上/右下/左下/左上；不是地形四角编码。mask=0为存在的孤立路。稳定变体混合格坐标与固定视觉种子0x143，取0～3；同格跨预览、连接变化及材质选取保持相同变体。

包内 `SOURCES_AND_LICENSES.md` 说明道路为此前向用户交付、已确认的确定性绘制资源，本包重新组织与交付而未新增外部道路下载或重绘；基础道路来源为历史 `FarmExchange-Bright-Roads-v2.zip`，图集属于组织派生形式。继续依据项目维护者2026-10-07明确公开已提供素材的授权记录，见[素材来源总览](../../../../docs/project/asset-sources.md)。不将全部包内资产声明为CC0。原文件的中文文本已有乱码，保留原包不改写；预览字体及复用农田/人物不随道路引入，也不扩大第三方授权。

硬分界、自然过渡、入口、中心细节、额外图集及静态预览均未复制为正式运行依赖。原ZIP仍只读保留原位置；正式导出沿用已有ZIP排除约定。素材核对不代表Godot导入、真实场景、性能或导出验收通过，结果由本单完整验收记录维护。
