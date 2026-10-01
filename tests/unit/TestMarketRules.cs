using System;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
using FarmExchange.Market;

public partial class TestMarketRules : Node
{
    public override void _Ready()
    {
        bool passed = RunChecks();
        GetTree().Quit(passed ? 0 : 1);
    }

    public static bool RunChecks() => CheckMarketCurve() && CheckDayTiming() && CheckPause();

    private static bool CheckMarketCurve()
    {
        var curve = new MarketPriceCurve(24680);
        var sameCurve = new MarketPriceCurve(24680);
        var otherCurve = new MarketPriceCurve(13579);
        int previousPrice = curve.GetPriceCents(1);
        bool differentSeedChangedPrice = false;
        int smallChanges = 0;
        int mediumChanges = 0;
        int largeChanges = 0;
        if (previousPrice != MarketPriceCurve.InitialPriceCents)
            return Fail("市场曲线未从 5.00 金币开始");
        if (!Throws<ArgumentOutOfRangeException>(() => curve.GetPriceCents(0)))
            return Fail("市场曲线接受了第 0 天");
        var game = new FarmGame(12345);
        int[] initialPrices = { 500, 500, 600, 400, 800, 1000, 50 };
        int[] initialRawPrices = { 250, 250, 300, 200, 400, 500, 25 };
        foreach (CropDefinition crop in FarmGame.Crops)
            if (game.GetProductPriceCents(crop.Kind) != initialPrices[(int)crop.Kind] ||
                game.GetRawPriceCents(crop.Kind) != initialRawPrices[(int)crop.Kind] ||
                crop.RawPricePercent != 50)
                return Fail($"{crop.CropName}首日原料或加工品售价错误");

        for (int day = 2; day <= 100000; day++)
        {
            int price = curve.GetPriceCents(day);
            if (price < MarketPriceCurve.MinimumPriceCents || price > MarketPriceCurve.MaximumPriceCents)
                return Fail($"第 {day} 天价格超出 1.00～20.00 金币范围");
            if (price != sameCurve.GetPriceCents(day))
                return Fail("相同市场种子没有生成相同价格");
            if (price != otherCurve.GetPriceCents(day))
                differentSeedChangedPrice = true;
            double change = System.Math.Abs(price - previousPrice) * 100.0 / previousPrice;
            if (change > 20.0)
                return Fail($"第 {day} 天面粉价格变化超过 20%：{change:0.00}%");
            if (change < 5.0) smallChanges++;
            else if (change < 15.0) mediumChanges++;
            else largeChanges++;
            previousPrice = price;
        }
        if (!differentSeedChangedPrice || smallChanges == 0 || mediumChanges == 0 || largeChanges == 0)
            return Fail("市场价格曲线缺少种子差异或涨跌档位");
        return true;
    }

    private static bool CheckDayTiming()
    {
        var game = new FarmGame(12345);
        for (int i = 0; i < 51; i++)
            if (game.AdvanceTick().DayAdvanced || game.CurrentDay != 1)
                return Fail("未跨过精确日界时提前进入下一天");
        if (!game.AdvanceTick().DayAdvanced || game.CurrentDay != 2 ||
            game.CurrentFlourPriceCents != 500 || game.DailyPriceChangePercent != 0.0)
            return Fail("第 52 秒未进入下一天，或非报价日价格变化");
        while (game.Calendar.ElapsedSeconds < 720)
            game.AdvanceTick();
        var expected = new MarketQuotes(12345, 14);
        foreach (var commodity in CommodityCatalog.All)
            if (game.GetQuote(commodity.Id) != expected.GetQuote(commodity.Id))
                return Fail("第 14 个经过日未使用正式独立商品报价");
        if (game.CurrentFlourPriceCents != game.GetProductPriceCents(CropKind.Wheat) ||
            game.DailyPriceChangePercent != (double)game.GetQuote(
                new CommodityId(CropKind.Wheat, CommodityKind.Product)).ChangePercent)
            return Fail("旧面粉查询没有委托本次正式报价");
        return true;
    }

    private static bool CheckPause()
    {
        var game = new FarmGame(12345);
        int price = game.CurrentFlourPriceCents;
        game.SetPaused(true);
        for (int i = 0; i < 100; i++)
            if (game.AdvanceTick() != default || game.Calendar.ElapsedSeconds != 0 ||
                game.CurrentFlourPriceCents != price)
                return Fail("暂停时经营、日历或行情继续推进");
        game.SetPaused(false);
        if (!game.AdvanceTick().WorkerActed || game.Calendar.ElapsedSeconds != 1)
            return Fail("恢复后未从原进度继续");
        return true;
    }

    private static bool Throws<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
            return false;
        }
        catch (TException)
        {
            return true;
        }
    }

    private static bool Fail(string message)
    {
        GD.PushError(message);
        return false;
    }
}
