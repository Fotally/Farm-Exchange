# 参数化开发测试使用说明

关联 [#101](https://github.com/Fotally/Farm-Exchange/issues/101)。固定流程与配置协议见[设计文档](developer-tools-parameterized-tests.md)，调用约定见[模块接口](../architecture/development/interface-parameterized-scenario.md)。工具仅在编辑器Debug与本地dev导出可用，release不包含开发窗口、流程或配置资源。

## 选择配置并运行

从游戏开发测试窗口选择持久JSON文件，然后启动。窗口显示运行对象、固定步骤进度、实际完成经营tick以及最终执行和报告结果；中止会保留已有买入、生产与订单。独立局创建新种子数据对象，当前地图继续显示当前局；现场使用当前真实对象，遵守暂停、资金、底线、冻结和其他玩家操作。

长期用例位于 [tests/scenario-configs/buy-process-sell](../../tests/scenario-configs/buy-process-sell/)：

| 文件 | 用途 |
| --- | --- |
| `wheat-basic-r1.json` | 独立局小麦Q=1，2×，真实付费准备与完整成交 |
| `radish-three-r1.json` | 独立局萝卜Q=3，2×到01-01-02后20×，加工79 tick与订单1 tick的精确预算 |
| `current-radish-r1.json` | 现场萝卜Q=1，1×；需要真实萝卜加工场地锚点(0,0)，不自动建造或改底线 |

现场用例应按照实际场地调整锚点并保存新修订，不能假定默认开局已有(0,0)加工场。加载后本次参数固定；文件修改下次启动生效。dev包不自带tests目录，用文件选择器加载仓库或个人持久配置库中的文件，不从报告目录加载副本。

## 字段约定

首版只接受 `schemaVersion=1` 和 `flow=buy-process-sell`。`caseId` 不能为空，`revision` 为正int32；改动长期用例时新增修订并保留原文件。

`run.target` 为 `independent` 或 `current`。独立局必填int32 `seed`；现场禁止seed字段。`execution.timePlan` 为非空列表，每段只包含 `endDate` 和 `rate`。游戏日期严格使用 `yy-MM-dd`（每月28日），终点严格递增，首个终点必须晚于实际游戏时点。终点是该日期起点首次达到的完整tick，最后日期限制最大推进范围，提前完成会提前结束。

倍率接受0.5、1、2或有限正整数；5、10、20为快捷项，非整数1.5等拒绝。倍率只改变现实等待速度，不改变tick预算或加工时长。玩家主动改速立即中止整个自动流程并保留玩家选择；不会恢复旧倍率或自动续跑。

`parameters` 必须完整提供：七种合法 `Crop.Raw` 之一的 `rawCommodity`、正int32 `quantity`、整数基础格锚点 `processorAnchor.x/y`、正uint32经营tick预算 `processingWaitLimitTicks` 与 `orderWaitLimitTicks`。数量Q统一用于买入、产品目标及一次卖单。目标产品按同一作物确定，不另填产品或加工时长。未知/重复字段、类型错误、缺失值和非法日期均在业务操作前明确拒绝；工具不补默认值。

加工观察与等待Q份产品共用第一个预算，订单等待使用第二个预算。暂停零推进，不消耗预算；不自动解除现场暂停。最后日期已有本单在该tick成交允许完成，但不会在最后日期创建新订单。

## 阅读结果与报告

独立局只有全部必需检查通过才显示严格通过。现场完成可以显示“完成但证据不足”：同步买入、建单冻结和本单LastFill可检查，已有库存、其他生产及同期操作使精确生产和整段资金归因不足。证据不足不会被忽略，也不会显示严格全通过。

配置错误、前置条件拒绝、正式操作拒绝、等待超限、时间范围耗尽、检查失败和中止分别反馈。流程失败不会补金币、库存、改底线或重建被玩家修改的订单；已有结果不回滚。

报告唯一文件为 `report.json`，默认位置如下：

- 编辑器/仓库：`build/test-runs/<run-id>/report.json`。
- Windows dev包：exe所在目录的 `reports/test-runs/<run-id>/report.json`。
- macOS dev包：`.app`父目录的 `reports/test-runs/<run-id>/report.json`，不写入Contents。

运行前检查输出位置可写；写入失败明确显示输出错误并保留内存中的执行结果，不更换路径。执行通过与报告保存成功是两个结果。报告引用原参数路径、caseId、revision和加载时原始UTF-8字节SHA-256，不生成配置副本或嵌入完整配置。历史文件缺失或摘要不符时，不能使用当前文件冒充本次输入。

报告中的区间是真实检查点之间的完成tick和真实产出；订单时间是首次观察到LastFill的时点。终止快照在停止检查点捕获，写报告期间现场继续经营也不改变这些证据。
