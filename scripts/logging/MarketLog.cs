using System;
using System.Collections.Generic;
using FarmExchange.Market;
using FarmExchange.Inventory;
using Microsoft.Extensions.Logging;
using FarmExchange.Time;

namespace FarmExchange.Logging;

/**
 * <summary>行情领域 Adapter，投影真实公告和正式报价，不维护行情或执行价格计算。</summary>
 */
public sealed class MarketLog
{
    private const string Source = "FarmExchange.Market.MarketQuotes";
    private static readonly LogEventDescriptor NewsEvent = new(30, "NewsPublished", Source);
    private static readonly LogEventDescriptor QuoteEvent = new(31, "QuoteUpdated", Source);
    private readonly GameLog _context;

    internal MarketLog(GameLog context) => _context = context;

    /**
     * <summary>在公告准备完成后记录实际公开内容。</summary>
     * <param name="readNews">同步读取本轮已锁定公告的查询，不执行行情推进。</param>
     * <remarks>只读快照构造放在隔离的观察中，结束采集后不再查询。</remarks>
     */
    internal void NewsPublished(Func<MarketNewsSnapshot> readNews) => _context.Observe(() =>
    {
        MarketNewsSnapshot news = readNews();
        var fields = _context.Context("Calendar");
        fields["PublishedDate"] = Date(news.PublishedDate);
        fields["QuoteDate"] = Date(news.QuoteDate);
        fields["NewsLines"] = news.Lines;
        _context.Output.Submit(NewsEvent, "市场公告已发布", fields);
    });

    /**
     * <summary>记录已经正式提交的单商品报价与该轮真实因素。</summary>
     * <param name="quote">提交后的真实商品报价。</param>
     * <param name="quoteDate">本轮实际报价日。</param>
     * <param name="nextQuoteDate">下一轮实际报价日。</param>
     * <param name="factors">本轮已锁定供需、事件与适用的季节或原料参考价。</param>
     */
    internal void QuoteUpdated(MarketQuoteSnapshot quote, GameDate quoteDate,
        GameDate nextQuoteDate, MarketPriceFactors factors) => _context.Observe(() =>
    {
        var fields = _context.Context("Calendar");
        fields["Commodity"] = quote.Id.Crop + "." + quote.Id.Kind;
        fields["PreviousPriceCents"] = quote.PreviousPriceCents;
        fields["PriceCents"] = quote.PriceCents;
        fields["QuoteDate"] = Date(quoteDate);
        fields["NextQuoteDate"] = Date(nextQuoteDate);
        var priceFactors = new Dictionary<string, object?>
        {
            ["SupplyFactor"] = factors.SupplyFactor,
            ["DemandFactor"] = factors.DemandFactor,
            ["SupplyDemandPercent"] = factors.SupplyDemandPercent,
            ["EventPercent"] = factors.EventPercent,
        };
        if (factors.SeasonPercent.HasValue) priceFactors["SeasonPercent"] = factors.SeasonPercent.Value;
        if (factors.RawReferencePriceCents.HasValue)
            priceFactors["RawReferencePriceCents"] = factors.RawReferencePriceCents.Value;
        fields["PriceFactors"] = priceFactors;
        _context.Output.Submit(QuoteEvent, "商品正式报价已更新", fields);
    });

    /**
     * <summary>在构造行情明细前检查显式局级事件选择和采集预算。</summary>
     * <returns>本次可以采集时为 true。</returns>
     */
    internal bool ShouldCaptureCalculation() => _context.Diagnostics.ShouldCapture("QuoteCalculated");

    /**
     * <summary>观察本次已经算出的待发布价、整数中间值与原因素。</summary>
     * <param name="commodity">实际计算商品。</param>
     * <param name="quoteDate">计算面向的实际报价日。</param>
     * <param name="factors">原计算使用的真实因素。</param>
     * <param name="calculation">原分子、分母及实际舍入和限幅结果。</param>
     * <remarks>调用方先通过 ShouldCaptureCalculation，不在日志中重算。</remarks>
     */
    internal void QuoteCalculated(CommodityId commodity, GameDate quoteDate, MarketPriceFactors factors,
        MarketCalculation calculation) => _context.Observe(() =>
    {
        var fields = _context.Context("Calendar");
        fields["Commodity"] = commodity.Crop + "." + commodity.Kind;
        fields["QuoteDate"] = Date(quoteDate);
        fields["SupplyFactor"] = factors.SupplyFactor;
        fields["DemandFactor"] = factors.DemandFactor;
        var inputs = new Dictionary<string, object?>
        {
            ["InitialPriceCents"] = calculation.InitialPriceCents,
            ["OldPriceCents"] = calculation.PreviousPriceCents,
            ["Formula"] = commodity.Kind == CommodityKind.Raw ? "RawFormula" : "ProductFormula",
            ["SupplyDemandPercent"] = factors.SupplyDemandPercent,
            ["EventPercent"] = factors.EventPercent,
            ["Numerator"] = calculation.Numerator,
            ["Denominator"] = calculation.Denominator,
            ["RoundedPriceCents"] = calculation.RoundedPriceCents,
            ["MinimumPriceCents"] = calculation.MinimumPriceCents,
            ["MaximumPriceCents"] = calculation.MaximumPriceCents,
            ["FinalPriceCents"] = calculation.FinalPriceCents,
        };
        if (factors.SeasonPercent.HasValue) inputs["SeasonPercent"] = factors.SeasonPercent.Value;
        if (factors.RawReferencePriceCents.HasValue)
        {
            inputs["RawInitialPriceCents"] = calculation.RawInitialPriceCents;
            inputs["PendingRawPriceCents"] = factors.RawReferencePriceCents.Value;
        }
        fields["CalculationInputs"] = inputs;
        _context.Diagnostics.Capture(CalculationEvent, "本轮商品待发布报价计算完成", fields);
    });

    private static readonly LogEventDescriptor CalculationEvent = new(105, "QuoteCalculated", Source, LogLevel.Debug, false);

    private static Dictionary<string, object?> Date(GameDate date) => new()
    {
        ["Year"] = date.Year,
        ["Month"] = date.Month,
        ["Day"] = date.Day,
    };
}

internal readonly record struct MarketPriceFactors(int SupplyFactor, int DemandFactor,
    int SupplyDemandPercent, int EventPercent, int? SeasonPercent, int? RawReferencePriceCents);

internal readonly record struct MarketCalculation(int InitialPriceCents, int PreviousPriceCents,
    long Numerator, long Denominator, int RoundedPriceCents, int MinimumPriceCents,
    int MaximumPriceCents, int FinalPriceCents, int? RawInitialPriceCents = null);
