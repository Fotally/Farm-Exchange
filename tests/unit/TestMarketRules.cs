using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Market;

public partial class TestMarketRules : Node
{
    public override void _Ready()
    {
        bool passed = RunChecks();
        GetTree().Quit(passed ? 0 : 1);
    }

    public static bool RunChecks() => CheckMarketCurve() && CheckDayTiming();

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
        var game = new FarmGame(12345);
        int[] initialPrices = { 500, 500, 600, 400, 800, 1000 };
        int[] initialRawPrices = { 250, 250, 300, 200, 400, 500 };
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
        for (int i = 0; i < FarmGame.TicksPerDay - 1; i++)
            if (game.AdvanceTick().DayAdvanced || game.CurrentDay != 1)
                return Fail("未满 10 tick 就提前进入下一天");
        if (!game.AdvanceTick().DayAdvanced || game.CurrentDay != 2 ||
            game.DailyPriceChangePercent == 0.0)
            return Fail("第 10 tick 未进入下一天并更新价格");
        foreach (CropDefinition crop in FarmGame.Crops)
        {
            int productPrice = (game.CurrentFlourPriceCents * crop.PricePercent + 50) / 100;
            if (game.GetProductPriceCents(crop.Kind) != productPrice ||
                game.GetRawPriceCents(crop.Kind) != (productPrice * crop.RawPricePercent + 50) / 100)
                return Fail($"{crop.CropName}原料或加工品没有按当天价格计价");
        }
        return true;
    }

    private static bool Fail(string message)
    {
        GD.PushError(message);
        return false;
    }
}
