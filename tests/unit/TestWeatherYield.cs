using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using FarmExchange.Farming;
using FarmExchange.Gameplay;
using FarmExchange.Time;
using FarmExchange.Weather;

public partial class TestWeatherYield : Node
{
    public override void _Ready() => GetTree().Quit(RunChecks() ? 0 : 1);

    public static bool RunChecks()
    {
        try
        {
            CheckCoefficientsAndRounding();
            CheckPreciseDurationAndPromotion();
            CheckDrawTimingAndRepeatedWater();
            CheckRegionalRainPhases();
            CheckConfiguredGameCreation();
            CheckNaturalAndExplicitBatchParity();
            GD.Print("天气产量、精确末秒、抽取时点与自然／显式批量等价检查通过");
            return true;
        }
        catch (Exception error) { GD.PushError("天气产量测试失败：" + error); return false; }
    }

    private static FarmingSystem Growing(int seed, CropKind crop, int delay = 0)
    {
        var farms = new FarmingSystem(1, seed);
        farms.Place(0, crop);
        var calendar = new GameCalendar(crop == CropKind.Sugarcane ? 4320u : 0u).Snapshot;
        Require(farms.TryWork(0, calendar), "播种失败");
        for (int i = 0; i < delay; i++)
            Require(!farms.AdvanceGrowth(0, out _, WeatherKind.Sunny), "干田等待期间开始生长");
        farms.SupplyWater(0);
        return farms;
    }

    private static int Expected(double quantity) => Math.Max(1, (int)Math.Round(quantity, MidpointRounding.AwayFromZero));

    private static void CheckCoefficientsAndRounding()
    {
        int[] cloudy = { 0, -10, 0, 10, 0, -10, 10 };
        int[] rain = { 0, 20, 20, -10, 0, 20, -10 };
        foreach (CropKind crop in Enum.GetValues<CropKind>())
        {
            int seed = 271 + (int)crop;
            CropDefinition definition = FarmGame.GetCrop(crop);
            double basis = definition.HarvestQuantity * (6 + new Random(seed).NextDouble() * 3);
            foreach (WeatherKind weather in Enum.GetValues<WeatherKind>())
            {
                FarmingSystem farms = Growing(seed, crop);
                int seconds = GameTimeUnits.RemainingSeconds(definition.GrowthDays * GameTimeUnits.PerDay);
                for (int i = 1; i < seconds; i++)
                    Require(!farms.AdvanceGrowth(0, out _, weather), "天气改变固定成熟时长");
                Require(farms.AdvanceGrowth(0, out CropHarvestResult harvest, weather), "到期未收获");
                int percent = weather == WeatherKind.Sunny ? 0 : weather == WeatherKind.Cloudy ? cloudy[(int)crop] : rain[(int)crop];
                Require(harvest.Crop == crop && harvest.Quantity == Expected(basis * (1 + percent / 100d)),
                    $"{crop}/{weather}系数或浮点基础量结算错误");
                Require(!farms.AdvanceGrowth(0, out _, weather) && !farms.Get(0).HasWater, "收获重复或水分未清除");
            }
        }
        Require(CropYieldRules.CalculateQuantity(2.5, 0, 1) == 3 &&
            CropYieldRules.CalculateQuantity(0.01, -10, 1) == 1, "半份向上取整或最低一份错误");
        for (int seed = 0; seed < 64; seed++)
        {
            FarmingSystem farms = Growing(seed, CropKind.Sunflower);
            farms.AdvanceQuietSeconds(0, 874, WeatherKind.Sunny);
            Require(farms.AdvanceGrowth(0, out CropHarvestResult result, WeatherKind.Sunny) && result.Quantity >= 6 && result.Quantity <= 9,
                "每轮随机基础量越出6～9倍范围");
        }
    }

    private static void CheckPreciseDurationAndPromotion()
    {
        // 选择能把末秒5单位与错误7单位区分开的确定性种子。
        int seed = Enumerable.Range(0, 100000).First(value =>
        {
            double basis = 6 * (6 + new Random(value).NextDouble() * 3);
            return Expected(basis * (1 - 0.1 * 5 / 1440)) != Expected(basis * (1 - 0.1 * 7 / 1442));
        });
        double quantity = 6 * (6 + new Random(seed).NextDouble() * 3);
        FarmingSystem farms = Growing(seed, CropKind.Radish);
        farms.AdvanceQuietSeconds(0, 205, WeatherKind.Sunny);
        Require(farms.Get(0).RemainingTimeUnits == 5 &&
            farms.AdvanceGrowth(0, out CropHarvestResult harvest, WeatherKind.Rain) &&
            harvest.Quantity == Expected(quantity * (1 - 0.1 * 5 / 1440)), "末秒天气没有只累计实际剩余5单位");
        farms = Growing(42, CropKind.Radish);
        farms.AdvanceQuietSeconds(0, 100, WeatherKind.Cloudy);
        farms.AdvanceQuietSeconds(0, 99, WeatherKind.Rain);
        double promotedBasis = 6 * (6 + new Random(42).NextDouble() * 3);
        Require(farms.TryMatureBeforeDisallowedSeason(0, Season.Summer, out CropHarvestResult promoted) &&
            promoted.Quantity == Expected(promotedBasis * (1 + (100d - 99) * 0.1 / 199)),
            "促熟补算未来天气或没有按已历精确时间加权");
        farms = Growing(42, CropKind.Radish);
        farms.AdvanceQuietSeconds(0, 1, WeatherKind.Rain);
        Require(!farms.TryMatureBeforeDisallowedSeason(0, Season.Summer, out _) &&
            farms.ClearDisallowedCrops(Season.Summer).Count(CropKind.Radish) == 1 &&
            !farms.AdvanceGrowth(0, out _, WeatherKind.Sunny), "禁生清理产生收成");
    }

    private static void CheckDrawTimingAndRepeatedWater()
    {
        FarmingSystem immediate = Growing(67, CropKind.Radish);
        FarmingSystem delayed = Growing(67, CropKind.Radish, 1000);
        for (int i = 0; i < 206; i++)
        {
            delayed.Get(0);
            delayed.SupplyWater(0);
            bool one = immediate.AdvanceGrowth(0, out CropHarvestResult first, WeatherKind.Cloudy);
            bool two = delayed.AdvanceGrowth(0, out CropHarvestResult second, WeatherKind.Cloudy);
            Require(one == two && first == second, "等待供水、读取详情或重复供水重新抽取基础量");
        }
        // 已留水田直接播种开始生长，仍只锁定一次相同随机量。
        var retained = new FarmingSystem(1, 67);
        retained.Place(0, CropKind.Radish);
        retained.SupplyWater(0);
        Require(retained.TryWork(0, new GameCalendar().Snapshot), "留水播种失败");
        retained.AdvanceQuietSeconds(0, 205, WeatherKind.Cloudy);
        Require(retained.AdvanceGrowth(0, out CropHarvestResult harvest, WeatherKind.Cloudy) &&
            harvest.Quantity == Expected(6 * (6 + new Random(67).NextDouble() * 3) * 1.1), "留水播种抽取错误");
    }

    private static FarmGame Empty(int seed, WeatherMode mode)
    {
        var game = new FarmGame(seed, weatherMode: mode, terrainRules: TerrainTestFixtures.AllLandRules);
        foreach (var space in game.GetBuildingSpaces()) game.RemoveBuilding(space.AnchorCell);
        return game;
    }

    private static void CheckRegionalRainPhases()
    {
        using FarmGame game = Empty(55, WeatherMode.Explicit);
        Vector2I farm = new(189, 189);
        Require(game.TryPlace(farm, BuildingKind.Farm, CropKind.Radish).Success, "建田失败");
        game.AdvanceTick();
        Require(game.GetPlot(farm).Crop == CropStage.Seeded, "干田播种相位错误");
        Rect2I corner = new(191, 191, 1, 1);
        Require(game.TrySetWeatherRegion(corner, WeatherKind.Rain, out _), "设置角落雨区失败");
        TickResult supplied = game.AdvanceTick();
        Require(!supplied.WorkerActed && game.GetPlot(farm).RemainingSeconds == 205 &&
            game.GetFarmDetails(farm).CurrentWeather == WeatherKind.Rain, "角落雨没有整田供水并推进当秒");
        SimulationAdvanceResult result = game.AdvanceTicks(205);
        Require(result.Harvested > 0 && result.QuietTicks > 190 && !game.GetPlot(farm).HasWater &&
            game.GetPlot(farm).Crop == CropStage.Seeded, "持续雨收获重新润湿或批量退化逐秒");
        game.AdvanceTick();
        Require(game.GetPlot(farm).HasWater && game.GetPlot(farm).RemainingSeconds == 205, "下一雨秒没有重新供水");
    }

    private static void CheckNaturalAndExplicitBatchParity()
    {
        foreach (WeatherMode mode in Enum.GetValues<WeatherMode>())
        {
            using FarmGame one = new(973, weatherMode: mode, terrainRules: TerrainTestFixtures.AllLandRules);
            using FarmGame batch = new(973, weatherMode: mode, terrainRules: TerrainTestFixtures.AllLandRules);
            using FarmGame split = new(973, weatherMode: mode, terrainRules: TerrainTestFixtures.AllLandRules);
            if (mode == WeatherMode.Explicit)
                foreach (FarmGame game in new[] { one, batch, split })
                    Require(game.TrySetWeatherRegion(new(189, 189, 1, 1), WeatherKind.Rain, out _), "雨区设置失败");
            long harvested = 0, produced = 0;
            for (int segment = 0; segment < 50; segment++)
            {
                uint count = (uint)(401 + segment % 9);
                long segmentHarvest = 0, segmentProduced = 0;
                for (uint i = 0; i < count; i++)
                {
                    TickResult tick = one.AdvanceTick();
                    segmentHarvest += tick.Harvested;
                    segmentProduced += tick.Produced;
                }
                SimulationAdvanceResult all = batch.AdvanceTicks(count);
                SimulationAdvanceResult a = split.AdvanceTicks(count / 2), b = split.AdvanceTicks(count - count / 2);
                Require(all.Harvested == segmentHarvest && all.Produced == segmentProduced &&
                    a.Harvested + b.Harvested == segmentHarvest && a.Produced + b.Produced == segmentProduced,
                    "自然或显式天气不同批次收获／加工不等价");
                harvested += segmentHarvest; produced += segmentProduced;
                foreach (FarmGame other in new[] { batch, split })
                {
                    Require(one.Calendar == other.Calendar && one.GetWorkers().SequenceEqual(other.GetWorkers()) &&
                        one.ActiveWeatherRegions.SequenceEqual(other.ActiveWeatherRegions) &&
                        one.GetPresentationResults().Select(r => (r.ElapsedSeconds, r.AnchorCell, r.CropKind, r.Kind, r.Quantity, r.WorkerNumber)).SequenceEqual(other.GetPresentationResults().Select(r => (r.ElapsedSeconds, r.AnchorCell, r.CropKind, r.Kind, r.Quantity, r.WorkerNumber))), "天气、工人或最近真实结果不同");
                    foreach (var space in one.GetBuildingSpaces())
                        Require(one.GetPlot(space.AnchorCell) == other.GetPlot(space.AnchorCell), "生长／加工进度不同");
                    foreach (CropKind crop in Enum.GetValues<CropKind>())
                        Require(one.GetRawStock(crop) == other.GetRawStock(crop) &&
                            one.GetProductStock(crop) == other.GetProductStock(crop), "库存不同");
                }
            }
            Require(harvested > 0 && produced > 0, "等价测试未经历真实生产");
        }
    }

    private static void CheckConfiguredGameCreation()
    {
        string originalJson = FileAccess.GetFileAsString(WeatherRules.ResourcePath);
        JsonObject data = JsonNode.Parse(originalJson)!.AsObject();
        data["CycleYears"] = 1;
        data["MaximumIntervalDays"] = 1;
        data["MaximumDurationDays"] = 2;
        data["MinimumRegionSizeCells"] = 384;
        data["MaximumRegionSizeCells"] = 384;
        data["MaximumActiveRegions"] = 1;
        WeatherRules rules = WeatherRules.Parse(data.ToJsonString());
        WeatherRules indentedRules = WeatherRules.Parse(data.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        var compactWeather = new WeatherSystem(973, rules);
        var indentedWeather = new WeatherSystem(973, indentedRules);
        uint time = 0;
        while (time < compactWeather.CycleSeconds)
        {
            uint distance = compactWeather.GetNextEventSeconds(time);
            Require(distance == indentedWeather.GetNextEventSeconds(time), "JSON排版改变合法配置的真实事件距离");
            time += distance;
            compactWeather.AdvanceTo(time);
            indentedWeather.AdvanceTo(time);
            Require(compactWeather.ActiveRegions.SequenceEqual(indentedWeather.ActiveRegions),
                "JSON排版改变合法配置的真实区域执行");
        }
        Require(WeatherRules.Parse(originalJson).CycleSeconds == WeatherRules.Default.CycleSeconds,
            "合法配置变体污染了正式配置");
        using FarmGame one = new(973, weatherRules: rules, terrainRules: TerrainTestFixtures.AllLandRules);
        using FarmGame batch = new(973, weatherRules: rules, terrainRules: TerrainTestFixtures.AllLandRules);
        var expectedWeather = new WeatherSystem(973, rules);
        long harvested = 0;
        foreach (uint count in new[] { 51u, 1u, 103u, 206u, 1440u, 15520u })
        {
            long expectedHarvested = 0, expectedProduced = 0;
            var batchBefore = batch.ActiveWeatherRegions;
            for (uint i = 0; i < count; i++)
            {
                TickResult tick = one.AdvanceTick();
                expectedHarvested += tick.Harvested;
                expectedProduced += tick.Produced;
            }
            Require(ReferenceEquals(batchBefore, batch.ActiveWeatherRegions) && batch.Calendar != one.Calendar,
                "共享天气配置的经营局相互污染");
            SimulationAdvanceResult result = batch.AdvanceTicks(count);
            expectedWeather.AdvanceTo(one.Calendar.ElapsedSeconds);
            Require(one.ActiveWeatherRegions.SequenceEqual(expectedWeather.ActiveRegions) &&
                one.ActiveWeatherRegions.SequenceEqual(batch.ActiveWeatherRegions) &&
                result.Harvested == expectedHarvested && result.Produced == expectedProduced &&
                one.Calendar == batch.Calendar && one.GetWorkers().SequenceEqual(batch.GetWorkers()),
                "建局传入配置未实际采纳或事件／循环的单批推进不等价");
            if (one.Calendar.ElapsedSeconds == 52)
                Require(one.ActiveWeatherRegions.Count == 1 &&
                    one.ActiveWeatherRegions[0].Cells == new Rect2I(0, 0, 384, 384) &&
                    one.GetFarmDetails(new Vector2I(189, 189)).CurrentWeather == one.GetWeather(Vector2I.Zero),
                    "建局配置的间隔、尺寸或农田权威天气未生效");
            foreach (var space in one.GetBuildingSpaces())
                Require(one.GetPlot(space.AnchorCell) == batch.GetPlot(space.AnchorCell), "配置局生产状态不等价");
            foreach (CropKind crop in Enum.GetValues<CropKind>())
                Require(one.GetRawStock(crop) == batch.GetRawStock(crop) &&
                    one.GetProductStock(crop) == batch.GetProductStock(crop), "配置局实际库存不等价");
            harvested += expectedHarvested;
        }
        Require(harvested > 0, "配置局推进未经历真实收获");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
