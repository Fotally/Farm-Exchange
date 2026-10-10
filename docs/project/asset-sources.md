# 素材来源记录

本页记录已纳入仓库的素材来源。素材加入不代表已接入经营或画面；实际使用入口以对应场景和架构文档为准。

## 当前接入基准：清亮完整 v2

- **文件与版本**：本地 `assets/FarmExchange-Bright-Complete-v2.zip`，内部版本 `2.0.0`，原包保持未跟踪。首批运行资源已接入；后续水域、旋转、采收搬运和加工岗位仍为设计范围。
- **校验与覆盖**：ZIP SHA-256 为 `6e9375c1db032e80cc01d228e67973b39b1d81c0cbbaa1d106b7efdb1a751f85`；1,072 条文件校验与 846 张 PNG 检查通过，覆盖与 v1 差别见[清亮 v2 分析](../research/bright-complete-v2.md)。
- **权威入口**：包内 `bundle_index.json`、`catalog/runtime_assets.json`，以及 `default/static/`、`default/npcs/`、`default/motion/` 各自清单；整景预览是当前资源副本。
- **来源与许可**：以包内 `docs/SOURCES_AND_LICENSES.md` 和子清单为准；来源包原先记录NPC为私人评估，具体第三方许可证未核实。2026-10-07，项目维护者明确允许公开所有已提供素材，包含本次NPC派生资源，授权纳入公开仓库与游戏分发；来源包原始许可说明仍保留。

已提取默认 q0 的 32 张静态 PNG，分别为一张草地图集、两张干湿土、播种层与七作物三档、七加工设施。逐项映射与画布约定见 [terrain](../../assets/gameplay/terrain/source_notes.md)、[soil](../../assets/gameplay/soil/source_notes.md)、[crops](../../assets/gameplay/crops/source_notes.md)、[buildings](../../assets/gameplay/buildings/source_notes.md)。运行图与候选共用同一资源；四个导出预设排除本地原 ZIP、`assets/source/` 与旧独立 `assets/npc/` 示例，未移动原始资料。

已提取20名角色的待机、走、跑、播种、浇水，共100张合成 PNG；20个 SpriteFrames 按逐角色元数据重建，保留帧时长，播种与浇水设为单次。主场景采用001、002、005，预览可检查全部角色；映射、脚根与本次公开授权见 [NPC 来源说明](../../assets/gameplay/npc/source_notes.md)。本次公开提交依据上述维护者明确授权。

当前采用四张建筑活动层与九张带透明边的单株图：[建筑动效来源](../../assets/gameplay/buildings/motion/source_notes.md)、[植株动效来源](../../assets/gameplay/crops/motion/source_notes.md)。蒸汽、收获及数量为程序绘制，来源与实际参数见 [FX 说明](../../assets/gameplay/fx/source_notes.md)，不再叠加人物已有的工具/水滴。已提取32类 q0 装饰，实际目录、pivot、自然散布与受控陈设见 [装饰来源](../../assets/gameplay/decor/source_notes.md)。原包、示例工程及未采用动作均不作为运行入口。

## 历史本地待评估：Farm Exchange 美术总包 v1

- **文件**：用户提供的 `assets/FarmExchange-Art-Agent-Bundle-v1.zip`，当前未跟踪，内部版本为 `1.0.2`；本次只分析，不接入或公开上传。
- **校验与范围**：ZIP SHA-256、1,283 个文件校验条目、默认与参考覆盖及使用方法见[素材包分析](../archive/research/art-agent-bundle-v1.md)。包内 NPC 的 20 张原图与现有正式图集哈希一致，派生动作仍是新增评估内容。
- **来源记录**：包内 `docs/SOURCES_AND_LICENSES.md`、各模块来源清单和字体许可证。NPC 的具体公开及商用许可未核实，随包范围为私人评估；不能由现有角色的历史入库许可推导为整包公开授权。
- **目录管理**：原始交付、正式运行资源、参考工程的归属见[assets 组织规则](asset-organization.md)，规则入口仍为 `assets/AGENTS.md`。

## 原型 UI 图标与缩略

`assets/ui/` 的 34 个 SVG 来自本项目已确认的离线视觉原型：15 个线条图标与 19 个作物/设施缩略。提取范围、颜色及使用入口见[素材说明](../../assets/ui/source_notes.md)。它们只用于 UI，不替换世界地图和人物；详情缩略是示意插画，坐标、状态和进度仍读取经营快照。

## FrameRonin 免费角色动画图

- **适用文件**：`assets/gameplay/npc/npc_animation_*.png`，共 20 张；保留来源编号 001、002、005～007、009～023。各文件只从用户提供的同编号 `character_animation_*.png` 复制并重命名，没有修改像素。
- **来源**：[FrameRonin 免费角色素材页](https://frameronin.com/free-character-library/)；用户提供的下载包说明为“古代三国角色包 · Godot 4 可运行示例”。
- **规格**：原包说明每帧为 64×64 像素，每行动画 6 帧；第 8、9、10 行分别作为下、左、上移动动画，向右时可镜像左移动。
- **授权记录**：来源页标示免费角色包并提供下载，当前页面与随包说明未写明具体许可证或再分发条款。2026-09-27，用户明确允许将这批素材放入下一次 PR。本记录不将“免费下载”解释为 CC0 或商业使用授权。
- **使用状态**：可复用角色与独立预览曾使用这些图集；清亮 v2 接入后角色入口改用上述清亮 v2 五动作图集，旧 PNG 保留历史文件，不参与默认加载。随包独立 Godot 示例项目与 GDScript 未纳入仓库。
