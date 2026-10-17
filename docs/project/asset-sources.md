# 素材来源与接入入口

本页维护来源包及授权历史，并提供当前运行资源的入口。各目录的 `source_notes.md` 唯一维护该组资源的采用状态、路径映射、方向、数量和导入约定；随包提供不等于已接入。目录归属见[素材管理约定](asset-organization.md)，当前使用限制见[清亮 v2 使用基准](../research/bright-complete-v2.md)。

## 当前接入入口

| 用途 | 权威来源说明 | 当前范围摘要 |
| --- | --- | --- |
| 地表与水域 | [地形](../../assets/gameplay/terrain/source_notes.md) | 已接入真实水域编码、mask 与水纹；地形变体不是设施朝向 |
| 农田底层与作物 | [土层](../../assets/gameplay/soil/source_notes.md)、[作物](../../assets/gameplay/crops/source_notes.md) | 正式实例与候选读取对应世界朝向静态图 |
| 加工设施 | [建筑](../../assets/gameplay/buildings/source_notes.md) | 四向静态已接入；局部动效替换范围另见下一项 |
| 局部动效与产出效果 | [建筑活动层](../../assets/gameplay/buildings/motion/source_notes.md)、[根点植株](../../assets/gameplay/crops/motion/source_notes.md)、[短效果](../../assets/gameplay/fx/source_notes.md) | 建筑与植株局部动效仅 q0；收获及加工成功短效果按真实结果显示，不受该静态替换范围限制 |
| 环境装饰 | [装饰](../../assets/gameplay/decor/source_notes.md) | 四向随机选图已接入，保持纯环境与成功占地清除规则 |
| 人物 | [NPC](../../assets/gameplay/npc/source_notes.md) | 已启用动作和未采用动作分别记录，不由整包动作数量推断工人能力 |
| 基础道路 | [道路](../../assets/gameplay/terrain/roads/source_notes.md) | 使用独立道路包的正式图集与区域映射 |
| UI 图标与缩略 | [UI](../../assets/ui/source_notes.md) | 七加工和五道路使用正式 PNG 派生缩略；农田与选种继续使用 SVG；旧加工／道路 SVG 保留历史文件 |

采收搬运、加工岗位及四季素材等未完成事项统一见[当前使用边界](../research/bright-complete-v2.md#尚未接入与待确认事项)。运行代码和资源映射由上述专题追溯，本页不另建资源明细表或实施阶段表。

## 清亮完整 v2：来源与授权历史

- **原交付路径与版本**：`assets/FarmExchange-Bright-Complete-v2.zip`，内部版本 `2.0.0`。此路径记录本地原始交付位置；原包未跟踪，不是正式运行依赖，也不保证每个 worktree 均存在该文件。
- **ZIP SHA-256**：`6e9375c1db032e80cc01d228e67973b39b1d81c0cbbaa1d106b7efdb1a751f85`。2026-10-06 的文件校验、PNG 检查、v1/v2 差异与包内清单路径保留在[原始分析归档](../archive/research/bright-complete-v2.md)。这些是历史核对结果，不是本轮重新运行的验收。
- **包内权威入口**：`bundle_index.json`、`catalog/runtime_assets.json` 与 `default/static/`、`default/npcs/`、`default/motion/` 各自清单；它们描述包内提供内容，不取代项目各目录的实际采用记录。整景预览是包内资源副本，不是另一套配色或运行入口。
- **授权原始记录**：包内 `docs/SOURCES_AND_LICENSES.md` 和子清单原先将 NPC 原图及派生资源列为私人评估，具体第三方许可证未核实。2026-10-07，项目维护者明确允许公开所有已提供素材，包含本次 NPC 派生资源，授权纳入公开仓库与游戏分发；来源包原始许可说明继续保留。不把该授权改写为 CC0，也不由此扩大第三方许可。

首批接入及公开授权的当时记录见[清亮 v2 历史分析](../archive/research/bright-complete-v2.md)，2026-10-07 的实际验收见[首批验收记录](../archive/project/testing.md#2026-10-07-105-清亮-v2-首批素材动作动效与环境验收)。后续接入以各目录来源说明为准；原包、独立工程、重建输入和未采用动作不作为正式运行入口。四个导出预设对原始资料和退役资源的排除及实际 PCK 检查见[运行资源验收](runtime-resources.md)。

## 基础道路包：来源与授权历史

[#143](https://github.com/Fotally/Farm-Exchange/issues/143) 采用原交付路径 `assets/FarmExchange-Roads-Complete.zip`，包 ID=`farm_exchange_roads_complete`，ZIP SHA-256=`17fd66b4546e9afdd05aa5d17f532df79f8db35df47916c73a9d682ac4ee69f6`。实际采用文件、包内路径、各图集哈希及区域映射见[道路来源说明](../../assets/gameplay/terrain/roads/source_notes.md)。原 ZIP 只读保留，不属于运行依赖。

包内 `SOURCES_AND_LICENSES.md` 记录基础路面为此前已确认的确定性绘制资源，源于历史 `FarmExchange-Bright-Roads-v2.zip`；本包重新组织为完整交付，没有新外部道路下载或像素重绘。公开纳入仍依据维护者 2026-10-07 对已提供素材的明确授权；不声明全部包内资源为 CC0，也不扩展字体、人物或整景预览的第三方许可。原文中文已有乱码，保留原包并如实记录。未采用内容见所属来源说明，不从完整交付包推断运行范围。

## 美术总包 v1：历史评估

- **原交付路径与版本**：`assets/FarmExchange-Art-Agent-Bundle-v1.zip`，内部版本 `1.0.2`，未跟踪；当时仅分析，没有接入或公开上传整个包。
- **ZIP SHA-256**：`5da2b4f8360d105af557ae104295e590582f2df660685d6348fba5c46bf6c3fc`。文件核对、默认与参考范围及使用方法见[v1 分析归档](../archive/research/art-agent-bundle-v1.md)。当时核对包内 NPC 原图与既有正式图集哈希相同，不能因此推导派生动作或整包授权。
- **历史许可**：以包内 `docs/SOURCES_AND_LICENSES.md`、各模块来源清单和字体许可证为准；当时 NPC 具体公开及商用许可未核实，随包范围为私人评估。该历史事实与后续维护者授权分别记录，不能互相改写。

## 原型 UI：来源历史与现行使用

最初 `assets/ui/` 的 34 个 SVG 来自本项目已确认的离线视觉原型：15 个线条图标、19 个作物／设施示意图。该数字记录最初提取范围，不表示当前全部 UI 图源。后续加工和道路缩略改为复用正式 PNG；仍使用的 SVG、退役示意图及真实加载入口统一见[UI 来源说明](../../assets/ui/source_notes.md)。UI 图形不决定坐标、状态、进度或费用，相关值读取经营快照。

## FrameRonin 免费角色图集：历史来源

- **适用文件**：`assets/gameplay/npc/npc_animation_*.png`，共 20 张；保留来源编号 001、002、005～007、009～023。各文件只从用户提供的同编号 `character_animation_*.png` 复制并重命名，没有修改像素。
- **来源**：[FrameRonin 免费角色素材页](https://frameronin.com/free-character-library/)；用户提供的下载包说明为“古代三国角色包 · Godot 4 可运行示例”。
- **原始规格**：原包说明每帧 64×64 像素，每行动画 6 帧；第 8、9、10 行分别作为下、左、上移动动画，向右时可镜像左移动。这是旧图集使用方式，不适用于清亮 v2 按动作拆分的图集。
- **授权记录**：来源页标示免费角色包并提供下载，当时页面与随包说明未写明具体许可证或再分发条款。2026-09-27，用户明确允许将这批素材放入下一次 PR。本记录不将“免费下载”解释为 CC0 或商业使用授权。
- **使用状态**：旧角色和独立预览曾使用这些图集；清亮 v2 接入后已不参与默认加载，保留历史文件。正式与 Dev 两平台预设均排除旧图集及导入纹理，实际 PCK 完整性规则见[运行资源验收](runtime-resources.md)。随包独立 Godot 示例项目与 GDScript 未纳入仓库。
