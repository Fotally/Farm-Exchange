# 有界市场曲线实现

对应[MarketPriceCurve 对外接口](interface-market-price-curve.md)。实现以三个不同周期的正弦项叠加小权重 FastNoiseLite SimplexSmooth 噪声，按种子生成相位，并反求使首日恰为 5.00 金币的长周期相位。噪声钳制到 [-1,1]，减去首日样本再除以 2，保持首日不变。

| 分量 | 权重 | 周期或频率 |
| --- | ---: | ---: |
| 长周期正弦 | 0.58 | 180 天 |
| 中周期正弦 | 0.28 | 61 天 |
| 短周期正弦 | 0.12 | 16 天 |
| 平滑噪声 | 0.02 | frequency 0.05 |

权重和为 1。归一化结果 q 位于 [-1,1]，再用 P(d) = L × (U / L)^((q(d) + 1) / 2) 映射到 L=1.00、U=20.00 金币，并按分四舍五入。参数给出的保守最大日差约 0.1159，对应未取整价格理论最大涨幅约 19.0%，为取整留余量。测试另以固定种子验证 10 万天的边界、20% 日幅和分类。

这里记录历史独立曲线的实现参数；[调研](../../../research/market-price-curve.md)保存候选模型、公式推导和资料来源。T09 后正式经营使用 [MarketQuotes](../market-quotes/interface-market-quotes.md)，不再使用本曲线的每日面粉基准或固定倍率；玩家现行独立行情规则见[独立商品报价](../../../gameplay/trading/market-quotes.md)，结算规则见[交易](../../../gameplay/trading/sales.md)。
