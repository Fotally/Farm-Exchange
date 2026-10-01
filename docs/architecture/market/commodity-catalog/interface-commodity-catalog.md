# CommodityCatalog 对外接口

对应 `FarmExchange.Market.CommodityCatalog`、`CommodityDefinition`，实现位于 `scripts/market/CommodityCatalog.cs` 与 `CommodityDefinition.cs`。来源为 [#71](https://github.com/Fotally/Farm-Exchange/issues/71)。商品标识复用库存模块的 `CommodityId(Crop, Kind)`，`Kind` 为 `Raw` 或 `Product`。

| 成员 | 调用约定 |
| --- | --- |
| `All` | 独立只读目录，恰好十四项；按七种作物既有顺序，每种先原料再成品。每项包含 `Id`、中文 `Name`、整数分 `InitialPriceCents`。 |
| `IsDefined(CommodityId)` | 检查作物和商品类别均有效，不修改状态。 |
| `Get(CommodityId)` | 返回对应商品定义；无效标识抛出 `ArgumentOutOfRangeException`。 |

目录唯一维护商品初价，具体数值见[独立商品报价](../../../gameplay/trading/market-quotes.md)。中文名称从现有作物原料名和成品名组装，加上“原料”或“加工品”后缀，使选择商品时明确类别。目录不维护库存、当前报价或交易进度。

市场、经营与界面共用这一目录；新增商品时应同步定义、报价和必要测试，不添加第二份初价表。测试见 `tests/unit/TestMarketQuotes.cs`。
