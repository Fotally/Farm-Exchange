using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FarmExchange.Gameplay;
using FarmExchange.Logging;
using FarmExchange.Market;
using FarmExchange.Time;
using Godot;
using static TestLogging;

public static class TestMarketLogging
{
    public static bool RunChecks()
    {
        try
        {
            CheckAnnouncementsAndFactors();
            CheckHistoryAndPause();
            CheckBatchAndFailureParity();
            return true;
        }
        catch (Exception error) { GD.PrintErr("行情日志检查失败：" + error); return false; }
    }

    private static void CheckAnnouncementsAndFactors()
    {
        using var output = new StringWriter();
        using var log = RuntimeLog.Capture(output);
        using var traced = new FarmGame(24680, log);
        using var plain = new FarmGame(24680);
        bool delayed = false, early = false, crossedYear = false;
        for (int period = 0; period < 26; period++)
        {
            MarketSnapshot before = traced.GetMarketSnapshot();
            uint quoteDay = before.NextQuoteDate.ElapsedDays;
            AdvanceToDay(traced, quoteDay - 1);
            AdvanceToDay(plain, quoteDay - 1);
            Require(SameMarket(traced.GetMarketSnapshot(), plain.GetMarketSnapshot()), "启用日志改变公告或随机序列");
            string[] atNews = MarketLines(output.ToString());
            Require(atNews.Count(line => HasEvent(line, "NewsPublished")) == period + 1 &&
                atNews.Count(line => HasEvent(line, "QuoteUpdated")) == period * 14, "公告提前变价或事件重复");
            MarketNewsSnapshot news = traced.GetMarketSnapshot().News!.Value;
            string published = atNews.Last();
            Require(HasEvent(published, "NewsPublished") && published.Contains(DateField("PublishedDate", news.PublishedDate)) &&
                published.Contains(DateField("QuoteDate", news.QuoteDate)) && published.Contains("NewsLines: [") &&
                news.Lines.All(line => published.Contains(line, StringComparison.Ordinal)), "实际公告内容或日期不一致");
            Require(traced.GetMarketSnapshot().Quotes.SequenceEqual(before.Quotes), "公告改变正式价");
            int length = output.ToString().Length;
            traced.GetMarketSnapshot();
            traced.GetQuote(CommodityCatalog.All[0].Id);
            traced.AdvanceTicks(0);
            traced.AdvanceTicks(1);
            Require(output.ToString().Length == length, "只读查询或零推进重复记行情");
            AdvanceToDay(traced, quoteDay);
            AdvanceToDay(plain, quoteDay);
            MarketSnapshot after = traced.GetMarketSnapshot();
            Require(SameMarket(after, plain.GetMarketSnapshot()), "启用日志改变正式价格");
            string[] updates = MarketLines(output.ToString()).Skip(atNews.Length).ToArray();
            Require(updates.Length == 14 && updates.All(line => HasEvent(line, "QuoteUpdated")), "正式提交没有逐商品唯一记录");
            for (int index = 0; index < updates.Length; index++)
            {
                string line = updates[index];
                MarketQuoteSnapshot quote = after.Quotes[index];
                Match factors = Regex.Match(news.Lines[index + 1], @"供给 (-?\d+)，需求 (-?\d+)；供需 ([+-]?\d+)%，事件 ([+-]?\d+)%");
                Require(factors.Success, "公告缺少真实因素");
                Require(line.Contains("Commodity: \"" + quote.Id.Crop + "." + quote.Id.Kind + "\"") &&
                    Field(line, "PreviousPriceCents") == before.Quotes[index].PriceCents.ToString(CultureInfo.InvariantCulture) &&
                    Field(line, "PriceCents") == quote.PriceCents.ToString(CultureInfo.InvariantCulture) &&
                    line.Contains(DateField("QuoteDate", after.LastQuoteDate)) &&
                    line.Contains(DateField("NextQuoteDate", after.NextQuoteDate)), "正式报价字段不对应提交结果");
                string[] names = { "SupplyFactor", "DemandFactor", "SupplyDemandPercent", "EventPercent" };
                for (int factor = 0; factor < names.Length; factor++)
                    Require(int.Parse(Field(line, names[factor]), CultureInfo.InvariantCulture) ==
                        int.Parse(factors.Groups[factor + 1].Value, CultureInfo.InvariantCulture), "因素来自错误期次");
                if (index % 2 == 0)
                {
                    Match season = Regex.Match(news.Lines[index + 1], @"报价日季节 [春夏秋冬] ([+-]?\d+)%");
                    Require(season.Success && int.Parse(Field(line, "SeasonPercent"), CultureInfo.InvariantCulture) ==
                        int.Parse(season.Groups[1].Value, CultureInfo.InvariantCulture) &&
                        !line.Contains("RawReferencePriceCents:"), "原料季节因素投影错误");
                }
                else
                    Require(Field(line, "RawReferencePriceCents") == after.Quotes[index - 1].PriceCents.ToString(CultureInfo.InvariantCulture) &&
                        !line.Contains("SeasonPercent:"), "加工品成本不是本期原料新价");
                Require(line.Contains("Phase: \"Calendar\"") && !line.Contains("ElapsedDays:") &&
                    !line.Contains("Numerator:") && !line.Contains("CommandId:"), "行情混入未登记日期、明细或命令字段");
            }
            delayed |= quoteDay == 99;
            early |= quoteDay == 181;
            crossedYear |= quoteDay >= 336;
        }
        Require(delayed && early && crossedYear, "样本未覆盖节日及跨年排期");
        AssertSequence(Lines(output.ToString()));
        Require(!output.ToString().Contains("EventName=QuoteCalculated"), "本阶段意外启用报价计算明细");
    }

    private static void CheckHistoryAndPause()
    {
        using var output = new StringWriter();
        using var log = RuntimeLog.Capture(output);
        using var first = new FarmGame(17, SecondsAtDay(100), log);
        using var second = new FarmGame(17, SecondsAtDay(100), log);
        Require(MarketLines(output.ToString()).Length == 0, "初始化补造历史行情事件");
        first.SetPaused(true);
        first.AdvanceTicks(1000);
        Require(MarketLines(output.ToString()).Length == 0, "暂停推进记录了行情");
        first.SetPaused(false);
        AdvanceToDay(first, 111);
        AdvanceToDay(second, 111);
        string[] news = MarketLines(output.ToString());
        Require(news.Length == 2 && news.All(line => HasEvent(line, "NewsPublished")) &&
            Field(news[0], "GameInstanceId") != Field(news[1], "GameInstanceId"), "两局同一期行情串联或历史重复");
        first.SetPaused(true);
        first.AdvanceTicks(1000);
        Require(MarketLines(output.ToString()).Length == 2, "暂停应用待生效价");
        first.SetPaused(false);
        AdvanceToDay(first, 112);
        Require(MarketLines(output.ToString()).Length == 16, "恢复后行情没有只提交一次");
        first.Dispose();
        int count = MarketLines(output.ToString()).Length;
        AdvanceToDay(first, 140);
        Require(MarketLines(output.ToString()).Length == count, "已结束局仍记录行情");
        using var prepared = new FarmGame(17, SecondsAtDay(111), log);
        Require(MarketLines(output.ToString()).Length == count, "初始化公告中间态补发过去公告");
        AdvanceToDay(prepared, 112);
        string[] preparedUpdates = MarketLines(output.ToString()).Skip(count).ToArray();
        Require(preparedUpdates.Length == 14 && preparedUpdates.All(line => HasEvent(line, "QuoteUpdated")),
            "初始化已准备报价未在未来真实提交或补造公告");
    }

    private static void CheckBatchAndFailureParity()
    {
        using var batchText = new StringWriter();
        using var steppedText = new StringWriter();
        using var failedText = new FailingWriter();
        using var batchLog = RuntimeLog.Capture(batchText);
        using var steppedLog = RuntimeLog.Capture(steppedText);
        using var failedLog = RuntimeLog.Capture(failedText, diagnostic: _ => { });
        using var batch = new FarmGame(29, batchLog);
        using var stepped = new FarmGame(29, steppedLog);
        using var failed = new FarmGame(29, failedLog);
        uint ticks = SecondsAtDay(28);
        batch.AdvanceTicks(ticks);
        failed.AdvanceTicks(ticks);
        for (uint tick = 0; tick < ticks; tick++) stepped.AdvanceTick();
        Require(SameMarket(batch.GetMarketSnapshot(), stepped.GetMarketSnapshot()) &&
            SameMarket(batch.GetMarketSnapshot(), failed.GetMarketSnapshot()), "批量或写入故障改变真实行情");
        string Normalize(string line) => Regex.Replace(line[(line.IndexOf(" EventName=", StringComparison.Ordinal) + 1)..],
            @"GameInstanceId: ""[^""]+""", "GameInstanceId: \"same\"");
        Require(MarketLines(batchText.ToString()).Select(Normalize).SequenceEqual(
            MarketLines(steppedText.ToString()).Select(Normalize)), "批量推进日志丢失中间公告、报价或时刻");
        Require(failedLog.Health.FailureCount > 0, "故障夹具未触发真实写入失败");
    }

    private static string[] MarketLines(string text) => Lines(text).Where(line =>
        HasEvent(line, "NewsPublished") || HasEvent(line, "QuoteUpdated")).ToArray();

    private static string DateField(string name, GameDate date) =>
        $"{name}: {{ Year: {date.Year}, Month: {date.Month}, Day: {date.Day} }}";

    private static uint SecondsAtDay(uint day) => checked((uint)(((ulong)day * 360 + 6) / 7));

    private static void AdvanceToDay(FarmGame game, uint day) =>
        game.AdvanceTicks(SecondsAtDay(day) - game.Calendar.ElapsedSeconds);

    private static bool SameMarket(MarketSnapshot first, MarketSnapshot second) =>
        first.LastQuoteDate == second.LastQuoteDate && first.NextQuoteDate == second.NextQuoteDate &&
        first.Quotes.SequenceEqual(second.Quotes) &&
        (first.News is null && second.News is null || first.News is MarketNewsSnapshot firstNews &&
            second.News is MarketNewsSnapshot secondNews && firstNews.PublishedDate == secondNews.PublishedDate &&
            firstNews.QuoteDate == secondNews.QuoteDate && firstNews.Lines.SequenceEqual(secondNews.Lines));

    private sealed class FailingWriter : StringWriter
    {
        public override void WriteLine(string? value) => throw new IOException("行情日志写入失败夹具");
    }
}
