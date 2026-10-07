# 清亮完整素材包 v2 分析与接入基准

本页的“本次实际检查”及v1/v2对比记录2026-10-06的素材核对，不代替后续运行验收。2026-10-07，#105首批已完成主游戏接入、独立审查、真实图形/性能、Windows导出启动及覆盖率验收，结果见[验收记录](../project/testing.md)；素材公开授权见文末。

2026-10-06，素材基准切换为 `assets/FarmExchange-Bright-Complete-v2.zip`，内部版本 **2.0.0**。接入规划由 [#105](https://github.com/Fotally/Farm-Exchange/issues/105) 及 #106～#113 维护。本记录用于替代接入方案中的旧 v1 资源选择；[v1 分析](art-agent-bundle-v1.md)保留为历史，不作为当前资源入口。

## 本次实际检查

| 检查项 | 结果 |
| --- | --- |
| ZIP 大小 | 12,835,502 字节，约 12.24 MiB |
| ZIP SHA-256 | `6e9375c1db032e80cc01d228e67973b39b1d81c0cbbaa1d106b7efdb1a751f85` |
| 文件数 / 解压大小 | 1,073 文件 / 27,127,618 字节，约 25.87 MiB |
| 文件校验锁 | 1,072 条全部一致；校验锁自身未列入自己的清单 |
| PNG 清单 | 846 张全部可解码，尺寸、文件 SHA-256、RGBA SHA-256 和 alpha SHA-256 均与清单一致 |
| PNG 角色 | 789 运行资源、4 技术 mask、33 当前重组输入、20 当前预览副本；数量不代表需全部导入游戏 |
| 默认静态 | 63 项、252 张方向图，实际核对为 210 张不同 RGBA；ID、画布、pivot 和占地与 v1 一致 |
| NPC | 20 人、180 合成图集、720 方向片段、5,280 帧格；与 v1 逐动作比对，帧尺寸、时长和合成图 alpha 一致 |
| 可选静态图集 | 建筑、农田、装饰三个 atlas 的 252 个区域逐像素与当前单图一致 |
| 整景预览 | 20 张副本逐字节匹配当前默认运行资源，不是另一套候选配色 |
| 项目静态检查 | 仅报告两个现有 ZIP 名称不符合小写 snake_case；本次保留原名和位置。文档路径、链接及其他检查未报告错误 |

本次直接读取 ZIP、清单与 PNG，并将少量样图提取到项目外临时目录查看；未执行随包代码、Godot 独立工程或主游戏导入。包内 `validation/release_validation.json` 记录交付者的重建、Godot 4.6.3 干净导入与真实图形检查，它们不是本次重新运行的项目验收。主项目后续仍用指定 Godot 4.7.2 Mono 验证。

## 当前唯一入口

以下路径相对于包根 `FarmExchange-Bright-Complete-v2/`，不是项目现有 `res://` 路径。

| 内容 | 入口 |
| --- | --- |
| 总包版本、默认项与配色 | `bundle_index.json`，schema_version=2，style=`bright_default_v2` |
| 跨模块运行清单 | `catalog/runtime_assets.json` |
| 每张实际 PNG 的角色、尺寸和哈希 | `catalog/png_inventory.json` |
| 七建筑、七作物三档、32 类装饰 | `default/static/manifest.json` |
| 草/土/水三元图集 | `default/static/terrain/manifest.json` |
| 草土二元兼容图集 | `default/static/terrain_binary_compat/atlas_manifest.json` |
| 可选建筑、农田、装饰合图 | `default/static/atlases/manifest.json` |
| 20 人动作 | `default/npcs/catalog.json`、各角色 `animations.json` 与 SpriteFrames |
| 两建筑、三作物及共享 FX | `default/motion/`，场景入口另列于运行清单 |
| 同一套资源的整景预览 | `previews/world/project.godot` |
| 来源与表现契约 | `docs/SOURCES_AND_LICENSES.md`、`docs/INTEGRATION_CONTRACT.md`、`docs/DIRECTION_AND_IMPORT.md` |

四个独立 Godot 工程仍分别拥有自己的 `res://`。正式提取继续按[assets 目录规则](../project/asset-organization.md)归入游戏用途目录，重写实际依赖；随包推荐的目录名不取代项目既有命名与职责。

## 与 v1 的实际差别

| 对比 | v1 | v2 |
| --- | --- | --- |
| 配色覆盖 | 默认静态与清亮 20 张 q0 参考分开 | 静态、地形、NPC 与已有动效均以完整清亮版为唯一默认 |
| 静态资源目录 | `default/static_full_v1/` | `default/static/` |
| 人物目录 | `default/npc_all_actions/` | `default/npcs/` |
| 动效目录 | `references/motion_q0/` | `default/motion/`，仍是独立表现样例 |
| 整景预览 | 独立清亮候选 | 当前默认资源的同步副本 |
| 静态 atlas | 未提供当前这组三张派生 atlas | 三张 atlas，252 个语义区域，与单图等价 |

实际对比的 252 张静态朝向与 180 张人物合成图集全部发生 RGBA 变化。所有人物合成图 alpha 保持一致；静态中 **10 张图的 alpha 改变**：

- `wheat_workshop_q0`、`sugarcane_workshop_q0`。
- 小麦末档、甘蔗末档各四向，共八张。

这与包内“结构整理、成熟植株聚簇”的限制记录一致。画布、pivot 与占地未变，不能把 alpha 改变误解释为设施尺寸变化，也不能把全部升级描述成纯色板替换。该次素材核对提出的像素边缘、遮挡和静态/动态切换检查，已在2026-10-07首批真实场景验收中完成，见[验收记录](../project/testing.md)。

## 规格和分期继续适用

- 基础格仍为 64×32，建筑为 256×240 / pivot=(128,176)，农田为 224×192 / pivot=(112,128)，两类设施逻辑占地仍为 3×3。NPC 单帧仍为 64×64 / 脚根=(32,60)。
- 人物按动作拆为四行图集，六帧与八帧动作并存；右向已烘焙镜像、时长仍按逐帧秒数。当前角色代码的旧大图切片方式仍不能直接用于新包。
- 三元地形仍是 81 组合 × 三变体，padding=2、stride=68×36、九列；二元图集是独立编码。
- 动态仍仅覆盖 q0 两建筑与三作物，不因“完整清亮”就变成七建筑、七作物全四向动效。
- 磨坊需采用当前塔身/叶片/轮毂分层，不能把活动叶片叠在仍含固定叶片的整图上。小麦/甘蔗末档与当前根点重组资料已配套；其他档与萝卜仍逐项读取根点契约，不自动宣称所有档与整田单图完全重合。
- 三档生长按实际进度等分，装饰为纯环境且成功建造后清除；动画只消费真实经营状态或结果。水域、旋转、采收搬运、工人加工仍归后续独立设计议题。
- 可选 atlas 是纹理组织选择，不要求新增动态扫描器、通用加载框架或可替换 Adapter。保持已确认的 Module、Interface 与状态归属规范。

## 使用与授权限制

v2 没有修改经营、碰撞、寻路、存档、角色任务或游戏导出。其 `docs/REPOSITORY_HANDOFF.md` 的 #105～#113 描述是随包背景，当前议题与仓库规则仍为实施依据。

来源包记录NPC原图及派生资源为私人评估、具体第三方许可证未核实，清亮化不改变原始记录。2026-10-07维护者已明确允许公开所有已提供素材，包含本次NPC派生资源；本次提交依据见[授权记录](../project/asset-sources.md)。字体仍为预览中文子集，只带入实际采用的字体与许可证。原ZIP与独立示例继续本地归档。

2026-10-06素材分析未搬动两个ZIP、未混入独立工程，也未修改游戏代码或导入设置。旧包保留历史来源；2026-10-07相应子议题已完成v2首批接入及Windows验收，原始ZIP与独立工程仍未纳入运行资源。macOS本机原生验收未执行，后续平台结果以实际CI记录为准。
