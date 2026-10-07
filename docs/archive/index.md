# 历史文档归档

归档日期：2026-10-07。整理任务 [#123](https://github.com/Fotally/Farm-Exchange/issues/123)。现行文档从[文档导航](../README.md)进入。

## 目录约定

`docs/<分类>/<路径>`归档到`docs/archive/<分类>/<路径>`，只创建实际需要的目录；archive本身不再嵌套。原型、截图等历史附件按自身原路径镜像迁移，现行文档共用的附件保留原位。原先直接放在archive根目录的系统计划已归入project。

正文保留当时的设计、讨论与验收证据，页首标明关联任务和现行入口。历史的“待合并”不代表当前状态：PR #70、#81、#84、#95、#102、#122均已合并；#105仍作为后续设计总入口开放。归档不会让未确认的建议变为已实施功能，也不意味着取消待办。

## 文档索引

| 历史材料 | 现行入口 | 关联与状态 |
| --- | --- | --- |
| [开发者工具与 dev / release 双构建设计草案](project/developer-tools-proposal.md) | [现行说明](../project/development-build.md) | #97历史设计；未选用候选见后续功能计划 |
| [开发者工具的 C# 源码组织讨论](project/developer-tools-csharp-organization.md) | [现行说明](../project/development-build.md) | #97历史设计；未选用候选见后续功能计划 |
| [跨模块自动调试流程设计补充](project/developer-tools-cross-module-debugging.md) | [现行说明](../project/development-build.md) | #97历史设计；未选用候选见后续功能计划 |
| [T09 独立行情与即时交易：已确认实施方案](project/market-proposal.md) | [现行说明](../gameplay/trading/market-quotes.md) | #25、#71～#73；PR #70已合并 |
| [多因素委托与自动交易：已确认实施方案](project/advanced-trading-proposal.md) | [现行说明](../gameplay/trading/orders.md) | #36；PR #81已合并 |
| [季节耕作表：已确认方案与交付范围](project/seasonal-cultivation-proposal.md) | [现行说明](../gameplay/production/seasonal-cultivation.md) | #42、#86；PR #84已合并 |
| [地图内容放置预览设计方案](project/build-placement-preview-proposal.md) | [现行说明](../gameplay/land/opening-and-building.md) | #83、#87；PR #84已合并 |
| [跨 tick 等价批量经营推进方案](project/batched-simulation-proposal.md) | [现行说明](../architecture/game-state/farm-game/implementation-batched-simulation.md) | #100；PR #102已合并 |
| [固定流程与可变参数的开发测试设计](project/developer-tools-parameterized-tests.md) | [现行说明](../project/parameterized-tests-usage.md) | #100、#101；PR #102已合并 |
| [点击倍率与分类流程配置界面设计](project/developer-tools-flow-editor-design.md) | [现行说明](../architecture/ui/developer-tools-window/interface-developer-tools-window.md) | #103、#104；PR #102已合并 |
| [Farm Exchange 美术总包 v1 分析](research/art-agent-bundle-v1.md) | [现行说明](../research/bright-complete-v2.md) | #105；首批PR #122已合并，后续设计仍开放 |
| [Farm Exchange 系统组织与深接口设计：已完成计划归档](project/farm-exchange-system-design-and-execution-plan.md) | [现行说明](../project/roadmap.md) | #76、#75；PR #70已合并 |
| [历史自动化测试与性能验收](project/testing.md) | [现行说明](../project/testing.md) | #36、#42、#75、#78、#86、#87、#94、#100、#101、#105、#116；#116已验收，本地dev交付状态见议题 |
| [UI 原型与早期验收历史](project/ui-visual-prototype.md) | [现行说明](../project/ui-visual-prototype.md) | #94；PR #95已合并 |
| [系统规划的阶段决策与交付历史](project/roadmap.md) | [现行说明](../project/roadmap.md) | #74～#113的阶段记录；交付状态见归档索引 |
| [后续功能计划历史](project/deferred-features-plan.md) | [现行说明](../project/deferred-features-plan.md) | #36、#42、#86；PR #81、#84已合并；#43、#44仍待确认 |

## 历史视觉附件

[architecture/ui/main](architecture/ui/main/)保存#94未采用的A/C候选、1280主稿与上一轮Godot截图；来源和对应画面见[UI历史记录](project/ui-visual-prototype.md)。当前采用的HTML主稿、1080P基准及现行接口使用的图片仍在正式文档目录。本次保持图片原字节，不删除历史证据；移动目录不宣称减少Git历史体积。
