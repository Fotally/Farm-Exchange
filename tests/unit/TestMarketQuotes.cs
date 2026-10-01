using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using FarmExchange.Farming;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
using FarmExchange.Market;
using FarmExchange.Time;

public partial class TestMarketQuotes : Node
{
    public override void _Ready()
    {
        bool passed = RunChecks();
        if (passed)
            GD.Print("独立商品报价、排期与真实消息检查通过");
        GetTree().Quit(passed ? 0 : 1);
    }

    public static bool RunChecks() => CheckCatalog() && CheckScheduleAndPause() &&
        CheckFactorsAndPrices() && CheckReplayAndReadOnly() && CheckTimeLimit();

    private static bool CheckCatalog()
    {
        int[] prices = { 250, 500, 250, 500, 300, 600, 200, 400, 400, 800, 500, 1000, 25, 50 };
        var market = new MarketQuotes(12345);
        MarketSnapshot snapshot = market.GetSnapshot();
        if (CommodityCatalog.All.Count != 14 || snapshot.Quotes.Count != 14 || snapshot.News != null ||
            snapshot.LastQuoteDate != GameCalendar.GetDate(0) || snapshot.NextQuoteDate != GameCalendar.GetDate(14))
            return Fail("初始报价日期、商品数或首日消息错误");
        for (int index = 0; index < prices.Length; index++)
        {
            CommodityDefinition definition = CommodityCatalog.All[index];
            MarketQuoteSnapshot quote = market.GetQuote(definition.Id);
            if (definition.Id != new CommodityId((CropKind)(index / 2), (CommodityKind)(index % 2)) ||
                definition.InitialPriceCents != prices[index] || definition.Name.Length == 0 ||
                quote.PriceCents != prices[index] || quote.PreviousPriceCents != prices[index] || quote.ChangePercent != 0 ||
                CommodityCatalog.Get(definition.Id) != definition)
                return Fail("商品目录顺序、初价或初始涨跌错误");
        }
        var invalid = new CommodityId((CropKind)7, CommodityKind.Raw);
        var invalidKind = new CommodityId(CropKind.Wheat, (CommodityKind)2);
        if (CommodityCatalog.IsDefined(invalid) || CommodityCatalog.IsDefined(invalidKind) ||
            !Throws<ArgumentOutOfRangeException>(() => CommodityCatalog.Get(invalid)) ||
            !Throws<ArgumentOutOfRangeException>(() => market.GetQuote(invalidKind)))
            return Fail("无效商品没有被拒绝");
        return true;
    }

    private static bool CheckScheduleAndPause()
    {
        // 覆盖普通跨月、换季、跨年以及第二年的固定节日；下一锚点始终不漂移。
        (uint Actual, uint Next)[] dates = { (14, 28), (28, 42), (84, 99), (99, 112),
            (181, 196), (336, 350), (435, 448), (517, 532) };
        foreach ((uint actual, uint next) in dates)
        {
            var market = new MarketQuotes(24680, actual - 2);
            MarketSnapshot before = market.GetSnapshot();
            market.Advance(CalendarAt(actual - 1));
            MarketSnapshot announced = market.GetSnapshot();
            if (!before.Quotes.SequenceEqual(announced.Quotes) || announced.News is not MarketNewsSnapshot news ||
                news.PublishedDate != GameCalendar.GetDate(actual - 1) || news.QuoteDate != GameCalendar.GetDate(actual) ||
                announced.NextQuoteDate != GameCalendar.GetDate(actual))
                return Fail($"经过 {actual} 日的报价没有在实际前一日公告或提前变价");
            market.Advance(CalendarAt(actual));
            MarketSnapshot applied = market.GetSnapshot();
            if (applied.LastQuoteDate != GameCalendar.GetDate(actual) || applied.NextQuoteDate != GameCalendar.GetDate(next) ||
                applied.News is not MarketNewsSnapshot appliedNews || !news.Lines.SequenceEqual(appliedNews.Lines))
                return Fail("节日排期漂移、跨年重置或报价时替换公告因素");
            string? expectedHoliday = actual % 336 == 99 ? "夏庆日调整：原定 4 月 15 日报价延后至 4 月 16 日" :
                actual % 336 == 181 ? "丰收日调整：原定 7 月 15 日报价提前至 7 月 14 日" : null;
            if (news.Lines.Count != (expectedHoliday == null ? 15 : 16) ||
                expectedHoliday != null && !news.Lines[^1].StartsWith(expectedHoliday, StringComparison.Ordinal))
                return Fail("节日公告未说明本期实际提前或延后原因");
            market.Advance(CalendarAt(actual + 1));
            if (!SameSnapshot(applied, market.GetSnapshot()))
                return Fail("非报价日修改了行情");
        }
        var paused = new MarketQuotes(13579, 12);
        MarketSnapshot original = paused.GetSnapshot();
        paused.Advance(CalendarAt(13, true));
        if (!SameSnapshot(original, paused.GetSnapshot()))
            return Fail("暂停推进产生了公告或报价");
        paused.Advance(CalendarAt(13));
        MarketSnapshot newsSnapshot = paused.GetSnapshot();
        paused.Advance(CalendarAt(14, true));
        if (!SameSnapshot(newsSnapshot, paused.GetSnapshot()))
            return Fail("暂停时应用了待生效报价");
        paused.Advance(CalendarAt(14));
        if (paused.GetSnapshot().LastQuoteDate.ElapsedDays != 14 ||
            !Throws<ArgumentOutOfRangeException>(() => paused.Advance(CalendarAt(13))))
            return Fail("恢复未按原进度报价或允许行情倒退");
        return true;
    }

    private static bool CheckFactorsAndPrices()
    {
        bool halfCent = false;
        bool limited = false;
        bool costChanged = false;
        bool rawProductRatioChanged = false;
        bool productPricesDiffered = false;
        bool continuedAcrossSeason = false;
        var eventNames = new HashSet<string>();
        for (int seed = 0; seed < 96; seed++)
        {
            var market = new MarketQuotes(seed);
            string? continuingName = null;
            Season previousSeason = Season.Spring;
            for (int period = 0; period < 48; period++)
            {
                MarketSnapshot before = market.GetSnapshot();
                uint day = before.NextQuoteDate.ElapsedDays;
                market.Advance(CalendarAt(day - 1));
                MarketNewsSnapshot news = market.GetSnapshot().News!.Value;
                Season season = GameCalendar.GetDate(day).Season;
                if (news.Lines.Count != (day % 336 is 99 or 181 ? 16 : 15))
                    return Fail("公告遗漏商品因素");
                string headline = news.Lines[0];
                bool continues = continuingName != null;
                if (continues)
                {
                    if (!headline.StartsWith(continuingName!, StringComparison.Ordinal) ||
                        !headline.Contains("继续生效，含本期还影响 1 次报价", StringComparison.Ordinal))
                        return Fail("持续歉收第二期重抽或改变对象");
                    if (season != previousSeason)
                        continuedAcrossSeason = true;
                }
                continuingName = headline.Contains("还影响 2 次报价", StringComparison.Ordinal)
                    ? headline.Split('，')[0] : null;
                previousSeason = season;
                eventNames.Add(headline.Split('：')[0]);
                market.Advance(CalendarAt(day));
                MarketSnapshot after = market.GetSnapshot();
                rawProductRatioChanged |= after.Quotes[0].PriceCents * 2 != after.Quotes[1].PriceCents;
                productPricesDiffered |= after.Quotes[1].PriceCents != after.Quotes[3].PriceCents;
                int eventTargets = 0;
                for (int index = 0; index < 14; index++)
                {
                    CommodityDefinition definition = CommodityCatalog.All[index];
                    MarketQuoteSnapshot oldQuote = before.Quotes[index];
                    MarketQuoteSnapshot quote = after.Quotes[index];
                    string line = news.Lines[index + 1];
                    Match factors = Regex.Match(line, @"供给 (-?\d+)，需求 (-?\d+)；供需 ([+-]?\d+)%，事件 ([+-]?\d+)%");
                    if (!factors.Success || !line.StartsWith(definition.Name + "：", StringComparison.Ordinal))
                        return Fail("商品公告缺少可观察的实际供需与事件因素");
                    int supply = int.Parse(factors.Groups[1].Value, CultureInfo.InvariantCulture);
                    int demand = int.Parse(factors.Groups[2].Value, CultureInfo.InvariantCulture);
                    int supplyDemand = int.Parse(factors.Groups[3].Value, CultureInfo.InvariantCulture);
                    int eventPercent = int.Parse(factors.Groups[4].Value, CultureInfo.InvariantCulture);
                    if (supply < -2 || supply > 2 || demand < -2 || demand > 2 || supplyDemand != 4 * (demand - supply))
                        return Fail("公告供需指数或实际因素不符合规则");
                    bool suitable = (FarmGame.Crops[index / 2].GrowingSeasons & (GrowingSeasons)(1 << (int)season)) != 0;
                    if (eventPercent != 0)
                    {
                        eventTargets++;
                        if (!headline.Contains(definition.Name, StringComparison.Ordinal) ||
                            (headline.StartsWith("外部产区丰产", StringComparison.Ordinal) && eventPercent != -20) ||
                            (!headline.StartsWith("外部产区丰产", StringComparison.Ordinal) && eventPercent != 20) ||
                            (headline.StartsWith("消费热潮", StringComparison.Ordinal) != (index % 2 == 1)) ||
                            (index % 2 == 0 && !continues && !suitable))
                            return Fail("事件强度、商品对象或初次适季资格错误");
                    }
                    decimal targetCents = definition.InitialPriceCents * (1 + (supplyDemand + eventPercent) / 100m);
                    if (index % 2 == 0)
                    {
                        int seasonPercent = suitable ? -10 : 10;
                        Match seasonal = Regex.Match(line, @"报价日季节 [春夏秋冬] ([+-]?\d+)%");
                        if (!seasonal.Success || int.Parse(seasonal.Groups[1].Value, CultureInfo.InvariantCulture) != seasonPercent)
                            return Fail("季节因素未按实际报价日计算");
                        targetCents += definition.InitialPriceCents * seasonPercent / 100m;
                    }
                    else
                    {
                        int rawInitial = CommodityCatalog.All[index - 1].InitialPriceCents;
                        int rawDifference = after.Quotes[index - 1].PriceCents - rawInitial;
                        targetCents += (decimal)definition.InitialPriceCents * rawDifference / (4 * rawInitial);
                        Match cost = Regex.Match(line, @"本期原料成本因素 ([+-]?[\d.]+)%");
                        if (!cost.Success || Math.Abs(decimal.Parse(cost.Groups[1].Value, CultureInfo.InvariantCulture) -
                            25m * rawDifference / rawInitial) > 0.00005m)
                            return Fail("成品公告成本因素不对应本期原料正式新价");
                        costChanged |= rawDifference != 0;
                    }
                    decimal candidate = (oldQuote.PriceCents + targetCents) / 2;
                    int rounded = (int)decimal.Round(candidate, 0, MidpointRounding.AwayFromZero);
                    int minimum = (int)decimal.Ceiling(Math.Max(definition.InitialPriceCents * 0.2m, oldQuote.PriceCents * 0.8m));
                    int maximum = (int)decimal.Floor(Math.Min(definition.InitialPriceCents * 4m, oldQuote.PriceCents * 1.2m));
                    int expected = Math.Clamp(rounded, minimum, maximum);
                    halfCent |= candidate % 1 == 0.5m && rounded >= minimum && rounded <= maximum;
                    limited |= rounded != expected;
                    if (quote.PriceCents != expected || quote.PreviousPriceCents != oldQuote.PriceCents ||
                        quote.ChangePercent != (expected - oldQuote.PriceCents) * 100m / oldQuote.PriceCents ||
                        quote.PriceCents < minimum || quote.PriceCents > maximum)
                        return Fail($"{definition.Name}报价未使用已公告因素、本期原料新价或整数分边界");
                }
                if (eventTargets != (headline == "本期无市场事件" ? 0 : 1))
                    return Fail("每期事件没有恰好作用于一个目标商品");
            }
        }
        if (!halfCent || !limited || !costChanged || !rawProductRatioChanged || !productPricesDiffered ||
            !continuedAcrossSeason || eventNames.Count != 4)
            return Fail("固定样本未覆盖半分向上、限幅、成本变化、三类事件、无事件及跨季持续");
        return true;
    }

    private static bool CheckReplayAndReadOnly()
    {
        var stepped = new MarketQuotes(24680);
        var queried = new MarketQuotes(24680);
        for (uint day = 0; day <= 1000; day++)
        {
            stepped.Advance(CalendarAt(day));
            queried.Advance(CalendarAt(day));
            queried.GetSnapshot();
            queried.GetQuote(new CommodityId(CropKind.Wheat, CommodityKind.Raw));
            if (!SameSnapshot(stepped.GetSnapshot(), queried.GetSnapshot()))
                return Fail("查询推进随机状态或相同种子没有复现");
        }
        if (!SameSnapshot(stepped.GetSnapshot(), new MarketQuotes(24680, 1000).GetSnapshot()) ||
            SameSnapshot(stepped.GetSnapshot(), new MarketQuotes(13579, 1000).GetSnapshot()))
            return Fail("初始日期回放、逐日推进或种子差异错误");
        MarketSnapshot held = queried.GetSnapshot();
        var quoteCopy = held.Quotes.ToArray();
        var newsCopy = held.News!.Value.Lines.ToArray();
        if (!Throws<NotSupportedException>(() => ((IList<MarketQuoteSnapshot>)held.Quotes)[0] = default) ||
            !Throws<NotSupportedException>(() => ((IList<string>)held.News.Value.Lines)[0] = "篡改"))
            return Fail("调用方能修改报价或消息快照");
        queried.Advance(CalendarAt(held.NextQuoteDate.ElapsedDays));
        if (!held.Quotes.SequenceEqual(quoteCopy) || !held.News.Value.Lines.SequenceEqual(newsCopy))
            return Fail("推进行情修改了先前返回的快照");
        return true;
    }

    private static bool CheckTimeLimit()
    {
        CalendarSnapshot calendar = new GameCalendar(uint.MaxValue).Snapshot;
        var market = new MarketQuotes(12345, calendar.ElapsedDays);
        MarketSnapshot snapshot = market.GetSnapshot();
        if (snapshot.LastQuoteDate.ElapsedDays > calendar.ElapsedDays || snapshot.NextQuoteDate.ElapsedDays <= calendar.ElapsedDays ||
            snapshot.LastQuoteDate.Year != calendar.Year || snapshot.Quotes.Any(quote => quote.PriceCents <= 0))
            return Fail("接近 uint32 模拟秒上限时行情回放或日期回绕");
        market.Advance(calendar);
        if (!SameSnapshot(snapshot, market.GetSnapshot()))
            return Fail("同一上限日期重复推进改变了行情");
        return true;
    }

    private static CalendarSnapshot CalendarAt(uint elapsedDays, bool paused = false)
    {
        uint seconds = checked((uint)(((ulong)elapsedDays * 360 + 6) / 7));
        var calendar = new GameCalendar(seconds);
        calendar.SetPaused(paused);
        return calendar.Snapshot;
    }

    private static bool SameSnapshot(MarketSnapshot first, MarketSnapshot second) =>
        first.LastQuoteDate == second.LastQuoteDate && first.NextQuoteDate == second.NextQuoteDate &&
        first.Quotes.SequenceEqual(second.Quotes) &&
        (first.News == null && second.News == null || first.News is MarketNewsSnapshot firstNews &&
            second.News is MarketNewsSnapshot secondNews && firstNews.PublishedDate == secondNews.PublishedDate &&
            firstNews.QuoteDate == secondNews.QuoteDate && firstNews.Lines.SequenceEqual(secondNews.Lines));

    private static bool Throws<TException>(Action action) where TException : Exception
    {
        try { action(); return false; }
        catch (TException) { return true; }
    }

    private static bool Fail(string message)
    {
        GD.PushError(message);
        return false;
    }
}
