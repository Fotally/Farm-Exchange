# T09 独立行情与即时交易：已确认实施方案

关联 [市场 #25](https://github.com/Fotally/Farm-Exchange/issues/25)、[日历排期 #33](https://github.com/Fotally/Farm-Exchange/issues/33)。2026-10-01 用户明确答复“提案没有问题，可继续开发”，原提案的价格与事件、节日排期、即时交易三组规则及初值全部采用。

## 规则唯一入口

- [独立商品报价与市场消息](../gameplay/trading/market-quotes.md)：十四商品初价、界限、供需/季节/成本公式、三种事件、整数分取整与限幅、固定双周锚点、两节日改期及前一日消息。
- [即时买卖与出售](../gameplay/trading/sales.md)：同价零费、正整数数量、完整结算与容量失败零修改、共用公共库存、买入后的加工领取时点、暂停交易及旧出售快捷行为。

具体数值只在上述专题维护，本文件保留用户确认来源与实施范围。作物、加工时长和免费加工仍见[作物与选种](../gameplay/production/crop-growth.md)。未宣称初值已经完成整体经济平衡，后续调优需另行确认。

## 设计与分工

总工程师先按 codebase-design 协调小 Interface，再分配三个完整模块：

| 实施 issue | 所有权 | 调用方只需知道 |
| --- | --- | --- |
| [#71](https://github.com/Fotally/Farm-Exchange/issues/71) | 商品目录、报价排期、真实因素和消息、纯日期查询及核心单元测试 | 日历推进、当前报价及独立只读快照 |
| [#72](https://github.com/Fotally/Farm-Exchange/issues/72) | 统一商品标识、唯一公共库存、完整买卖与经营接入、兼容测试 | 一次买卖或全部出售命令及实际结果 |
| [#73](https://github.com/Fotally/Farm-Exchange/issues/73) | 固定十四商品表、单一数量输入、窗口意图与场景分发、界面回归 | 读取经营快照、表达玩家意图，刷新保留草稿与焦点 |

报价算法留在 MarketQuotes 内部，交易检查与完整提交留在 TradingService 内部。库存只有现有原料和加工品两类公共存储；钱包保持现有整数分容量，交易先用更宽中间值检查全部资源与容量，再同步提交。UI、动画、生产不会各自计算行情或拼装扣款步骤。

## 验收状态

本批已完成集中审查与修复复查、指定引擎 Debug/Release 编译、完整场景测试、逐模块覆盖率、图形性能、Windows Release 导出及实际程序启动。真实市场截图已核对，完整数据见[测试验收](testing.md)与[执行计划前部记录](../archive/farm-exchange-system-design-and-execution-plan.md)。整体交付已通过 [PR #70](https://github.com/Fotally/Farm-Exchange/pull/70) 于北京时间 2026-10-02 00:17 人工合并。

基础格细分属于独立 [#74](https://github.com/Fotally/Farm-Exchange/issues/74) 调研，不改变本批报价和交易规则。天气、后续扩员、委托、复杂历史图与持仓成本模型继续留待后续。
