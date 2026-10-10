using System;
using System.Collections.Generic;
using System.Globalization;
using FarmExchange.Farming;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
using FarmExchange.Logging;
using FarmExchange.Time;

namespace FarmExchange.Market;

internal sealed class MarketQuotes
{
    private const uint QuoteIntervalDays = 14;
    private readonly Random _random;
    private readonly int[] _initialPrices = new int[CommodityCatalog.All.Count];
    private readonly int[] _prices = new int[CommodityCatalog.All.Count];
    private readonly int[] _previousPrices = new int[CommodityCatalog.All.Count];
    private readonly int[] _pendingPrices = new int[CommodityCatalog.All.Count];
    private readonly int[] _supply = new int[CommodityCatalog.All.Count];
    private readonly int[] _demand = new int[CommodityCatalog.All.Count];
    private uint _advancedDay;
    private uint _lastQuoteDay;
    private uint _nextAnchorDay = QuoteIntervalDays;
    private uint? _newsQuoteDay;
    private bool _prepared;
    private MarketEvent? _activeEvent;
    private MarketEvent? _announcedEvent;
    private bool _eventContinues;

    internal MarketQuotes(int seed, uint elapsedDays = 0)
    {
        _random = new Random(seed);
        for (int index = 0; index < _prices.Length; index++)
            _initialPrices[index] = _prices[index] = _previousPrices[index] =
                CommodityCatalog.All[index].InitialPriceCents;
        AdvanceTo(elapsedDays);
    }

    /**
     * <summary>按真实日历依次准备公告并提交报价，暂停保持行情。</summary>
     * <param name="calendar">不早于已推进日期的日历快照。</param>
     * <param name="log">本局行情观察入口；省略时不记录，构造器历史回放不传入。</param>
     */
    internal void Advance(CalendarSnapshot calendar, MarketLog? log = null)
    {
        if (calendar.IsPaused)
            return;
        if (calendar.ElapsedDays < _advancedDay)
            throw new ArgumentOutOfRangeException(nameof(calendar), "行情不能倒退日期");
        AdvanceTo(calendar.ElapsedDays, log);
    }

    /**
     * <summary>查询下一公告准备或正式报价的绝对游戏日。</summary>
     * <returns>当前排期的下一个真实行情事件日。</returns>
     */
    internal uint NextEventDay => ActualQuoteDay(_nextAnchorDay) - (_prepared ? 0u : 1u);

    internal MarketQuoteSnapshot GetQuote(CommodityId commodity)
    {
        CommodityDefinition definition = CommodityCatalog.Get(commodity);
        int index = (int)definition.Id.Crop * 2 + (int)definition.Id.Kind;
        int price = _prices[index];
        int previous = _previousPrices[index];
        return new MarketQuoteSnapshot(commodity, price, previous, (price - previous) * 100m / previous);
    }

    internal MarketSnapshot GetSnapshot()
    {
        var quotes = new MarketQuoteSnapshot[_prices.Length];
        for (int index = 0; index < quotes.Length; index++)
            quotes[index] = GetQuote(CommodityCatalog.All[index].Id);
        MarketNewsSnapshot? news = _newsQuoteDay is uint day
            ? new MarketNewsSnapshot(GameCalendar.GetDate(day - 1), GameCalendar.GetDate(day), BuildNewsLines(day))
            : null;
        return new MarketSnapshot(GameCalendar.GetDate(_lastQuoteDay),
            GameCalendar.GetDate(ActualQuoteDay(_nextAnchorDay)), Array.AsReadOnly(quotes), news);
    }

    private void AdvanceTo(uint day, MarketLog? log = null)
    {
        uint quoteDay = ActualQuoteDay(_nextAnchorDay);
        while (day >= quoteDay - 1)
        {
            if (!_prepared)
                PrepareQuote(quoteDay, log);
            if (day < quoteDay)
                break;
            Array.Copy(_prices, _previousPrices, _prices.Length);
            Array.Copy(_pendingPrices, _prices, _prices.Length);
            _lastQuoteDay = quoteDay;
            _activeEvent = _announcedEvent is MarketEvent marketEvent && marketEvent.RemainingQuotes > 1
                ? marketEvent with { RemainingQuotes = marketEvent.RemainingQuotes - 1 } : null;
            _nextAnchorDay += QuoteIntervalDays;
            _prepared = false;
            if (log != null)
            {
                GameDate quoteDate = GameCalendar.GetDate(quoteDay);
                GameDate nextQuoteDate = GameCalendar.GetDate(ActualQuoteDay(_nextAnchorDay));
                for (int index = 0; index < _prices.Length; index++)
                    log.QuoteUpdated(GetQuote(CommodityCatalog.All[index].Id), quoteDate, nextQuoteDate,
                        new MarketPriceFactors(_supply[index], _demand[index], SupplyDemandPercent(index),
                            EventPercent(index), index % 2 == 0 ? SeasonPercent(index / 2, quoteDate.Season) : null,
                            index % 2 == 1 ? _pendingPrices[index - 1] : null));
            }
            quoteDay = ActualQuoteDay(_nextAnchorDay);
        }
        _advancedDay = day;
    }

    private void PrepareQuote(uint quoteDay, MarketLog? log)
    {
        Season season = GameCalendar.GetDate(quoteDay).Season;
        _eventContinues = _activeEvent != null;
        _announcedEvent = _activeEvent ?? SelectEvent(season);
        for (int index = 0; index < _prices.Length; index++)
        {
            _supply[index] = _random.Next(-2, 3);
            _demand[index] = _random.Next(-2, 3);
        }
        for (int crop = 0; crop < CropCatalog.Crops.Count; crop++)
        {
            int rawIndex = crop * 2;
            int supplyDemandPercent = SupplyDemandPercent(rawIndex);
            int seasonPercent = SeasonPercent(crop, season);
            int eventPercent = EventPercent(rawIndex);
            int percent = 100 + supplyDemandPercent + seasonPercent + eventPercent;
            long numerator = (long)_prices[rawIndex] * 100 + (long)_initialPrices[rawIndex] * percent;
            _pendingPrices[rawIndex] = RoundAndClamp(rawIndex, numerator, 200, out int rounded, out int minimum, out int maximum);
            if (log?.ShouldCaptureCalculation() == true)
                log.QuoteCalculated(CommodityCatalog.All[rawIndex].Id, GameCalendar.GetDate(quoteDay),
                    new(_supply[rawIndex], _demand[rawIndex], supplyDemandPercent, eventPercent, seasonPercent, null),
                    new(_initialPrices[rawIndex], _prices[rawIndex], numerator, 200, rounded, minimum, maximum, _pendingPrices[rawIndex]));
        }
        for (int crop = 0; crop < CropCatalog.Crops.Count; crop++)
        {
            int rawIndex = crop * 2;
            int productIndex = rawIndex + 1;
            int rawInitial = _initialPrices[rawIndex];
            int supplyDemandPercent = SupplyDemandPercent(productIndex);
            int eventPercent = EventPercent(productIndex);
            int percent = 100 + supplyDemandPercent + eventPercent;
            long numerator = (long)_prices[productIndex] * 100 * rawInitial +
                (long)_initialPrices[productIndex] * (percent * rawInitial +
                    25 * (_pendingPrices[rawIndex] - rawInitial));
            long denominator = 200L * rawInitial;
            _pendingPrices[productIndex] = RoundAndClamp(productIndex, numerator, denominator, out int rounded, out int minimum, out int maximum);
            if (log?.ShouldCaptureCalculation() == true)
                log.QuoteCalculated(CommodityCatalog.All[productIndex].Id, GameCalendar.GetDate(quoteDay),
                    new(_supply[productIndex], _demand[productIndex], supplyDemandPercent, eventPercent, null, _pendingPrices[rawIndex]),
                    new(_initialPrices[productIndex], _prices[productIndex], numerator, denominator, rounded, minimum, maximum, _pendingPrices[productIndex], rawInitial));
        }
        _newsQuoteDay = quoteDay;
        _prepared = true;
        log?.NewsPublished(() => new MarketNewsSnapshot(GameCalendar.GetDate(quoteDay - 1),
            GameCalendar.GetDate(quoteDay), BuildNewsLines(quoteDay)));
    }

    private int RoundAndClamp(int index, long numerator, long denominator, out int rounded, out int minimum, out int maximum)
    {
        rounded = (int)((numerator + denominator / 2) / denominator);
        minimum = Math.Max((_initialPrices[index] * 20 + 99) / 100, (_prices[index] * 80 + 99) / 100);
        maximum = Math.Min(_initialPrices[index] * 4, _prices[index] * 120 / 100);
        return Math.Clamp(rounded, minimum, maximum);
    }

    private MarketEvent? SelectEvent(Season season)
    {
        if (_random.Next(2) == 0)
            return null;
        EventKind kind = (EventKind)_random.Next(3);
        int crop;
        if (kind == EventKind.ConsumerRush)
            crop = _random.Next(CropCatalog.Crops.Count);
        else
        {
            Span<int> eligible = stackalloc int[CropCatalog.Crops.Count];
            int count = 0;
            for (int index = 0; index < CropCatalog.Crops.Count; index++)
                if (SeasonPercent(index, season) < 0)
                    eligible[count++] = index;
            crop = eligible[_random.Next(count)];
        }
        return new MarketEvent(kind, crop * 2 + (kind == EventKind.ConsumerRush ? 1 : 0),
            kind == EventKind.PoorHarvest ? 2 : 1);
    }

    private int SupplyDemandPercent(int index) => 4 * (_demand[index] - _supply[index]);

    private static int SeasonPercent(int crop, Season season) =>
        (CropCatalog.Crops[crop].GrowingSeasons & (GrowingSeasons)(1 << (int)season)) != 0 ? -10 : 10;

    private int EventPercent(int index) => _announcedEvent is MarketEvent marketEvent && marketEvent.Index == index
        ? marketEvent.Kind == EventKind.GoodHarvest ? -20 : 20 : 0;

    private IReadOnlyList<string> BuildNewsLines(uint quoteDay)
    {
        GameDate date = GameCalendar.GetDate(quoteDay);
        string? holidayMessage = date.Month == 4 && date.Day == 16
            ? "夏庆日调整：原定 4 月 15 日报价延后至 4 月 16 日，下期恢复原双周锚点"
            : date.Month == 7 && date.Day == 14
                ? "丰收日调整：原定 7 月 15 日报价提前至 7 月 14 日，下期恢复原双周锚点"
                : null;
        var lines = new string[_prices.Length + 1 + (holidayMessage == null ? 0 : 1)];
        lines[0] = _announcedEvent is MarketEvent marketEvent
            ? $"{EventName(marketEvent.Kind)}：{CommodityCatalog.All[marketEvent.Index].Name}，" +
                $"{(_eventContinues ? "继续生效" : "开始生效")}，含本期还影响 {marketEvent.RemainingQuotes} 次报价"
            : "本期无市场事件";
        Season season = date.Season;
        for (int index = 0; index < _prices.Length; index++)
        {
            string factors = $"{CommodityCatalog.All[index].Name}：供给 {_supply[index]}，需求 {_demand[index]}；" +
                $"供需 {FormatPercent(SupplyDemandPercent(index))}，事件 {FormatPercent(EventPercent(index))}";
            if (index % 2 == 0)
                factors += $"，报价日季节 {SeasonName(season)} {FormatPercent(SeasonPercent(index / 2, season))}";
            else
            {
                int rawIndex = index - 1;
                decimal costPercent = 25m * (_pendingPrices[rawIndex] / (decimal)_initialPrices[rawIndex] - 1);
                factors += $"，本期原料成本因素 {FormatPercent(costPercent)}";
            }
            lines[index + 1] = factors;
        }
        if (holidayMessage != null)
            lines[^1] = holidayMessage;
        return Array.AsReadOnly(lines);
    }

    private static uint ActualQuoteDay(uint anchorDay) => (anchorDay % 336) switch
    {
        98 => anchorDay + 1,
        182 => anchorDay - 1,
        _ => anchorDay,
    };

    private static string FormatPercent(decimal percent) =>
        percent.ToString("+0.####;-0.####;0", CultureInfo.InvariantCulture) + "%";

    private static string EventName(EventKind kind) => kind switch
    {
        EventKind.GoodHarvest => "外部产区丰产",
        EventKind.PoorHarvest => "外部产区歉收",
        _ => "消费热潮",
    };

    private static string SeasonName(Season season) => season switch
    {
        Season.Spring => "春",
        Season.Summer => "夏",
        Season.Autumn => "秋",
        _ => "冬",
    };

    private enum EventKind { GoodHarvest, PoorHarvest, ConsumerRush }

    private readonly record struct MarketEvent(EventKind Kind, int Index, int RemainingQuotes);
}
