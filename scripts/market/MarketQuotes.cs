using System;
using System.Collections.Generic;
using System.Globalization;
using FarmExchange.Farming;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
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

    internal void Advance(CalendarSnapshot calendar)
    {
        if (calendar.IsPaused)
            return;
        if (calendar.ElapsedDays < _advancedDay)
            throw new ArgumentOutOfRangeException(nameof(calendar), "行情不能倒退日期");
        AdvanceTo(calendar.ElapsedDays);
    }

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

    private void AdvanceTo(uint day)
    {
        uint quoteDay = ActualQuoteDay(_nextAnchorDay);
        while (day >= quoteDay - 1)
        {
            if (!_prepared)
                PrepareQuote(quoteDay);
            if (day < quoteDay)
                break;
            Array.Copy(_prices, _previousPrices, _prices.Length);
            Array.Copy(_pendingPrices, _prices, _prices.Length);
            _lastQuoteDay = quoteDay;
            _activeEvent = _announcedEvent is MarketEvent marketEvent && marketEvent.RemainingQuotes > 1
                ? marketEvent with { RemainingQuotes = marketEvent.RemainingQuotes - 1 } : null;
            _nextAnchorDay += QuoteIntervalDays;
            _prepared = false;
            quoteDay = ActualQuoteDay(_nextAnchorDay);
        }
        _advancedDay = day;
    }

    private void PrepareQuote(uint quoteDay)
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
            int percent = 100 + SupplyDemandPercent(rawIndex) + SeasonPercent(crop, season) + EventPercent(rawIndex);
            long numerator = (long)_prices[rawIndex] * 100 + (long)_initialPrices[rawIndex] * percent;
            _pendingPrices[rawIndex] = RoundAndClamp(rawIndex, numerator, 200);
        }
        for (int crop = 0; crop < CropCatalog.Crops.Count; crop++)
        {
            int rawIndex = crop * 2;
            int productIndex = rawIndex + 1;
            int rawInitial = _initialPrices[rawIndex];
            int percent = 100 + SupplyDemandPercent(productIndex) + EventPercent(productIndex);
            long numerator = (long)_prices[productIndex] * 100 * rawInitial +
                (long)_initialPrices[productIndex] * (percent * rawInitial +
                    25 * (_pendingPrices[rawIndex] - rawInitial));
            _pendingPrices[productIndex] = RoundAndClamp(productIndex, numerator, 200L * rawInitial);
        }
        _newsQuoteDay = quoteDay;
        _prepared = true;
    }

    private int RoundAndClamp(int index, long numerator, long denominator)
    {
        int rounded = (int)((numerator + denominator / 2) / denominator);
        int minimum = Math.Max((_initialPrices[index] * 20 + 99) / 100, (_prices[index] * 80 + 99) / 100);
        int maximum = Math.Min(_initialPrices[index] * 4, _prices[index] * 120 / 100);
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
