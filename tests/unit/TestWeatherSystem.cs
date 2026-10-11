using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Land;
using FarmExchange.Time;
using FarmExchange.Weather;

public partial class TestWeatherSystem : Node
{
    public override void _Ready() => GetTree().Quit(
        OS.GetCmdlineUserArgs().Contains("--weather-query-probe") ? RunQueryProbe() : RunChecks() ? 0 : 1);

    public static bool RunChecks() => CheckConfiguration() && CheckExplicitRegions() &&
        CheckFarmPriority() && CheckFarmQueryTransitions() && CheckNaturalCycles();

    // 此探针只由专用场景显式运行，用相同满地图几何隔离真实天气查询成本。
    private static int RunQueryProbe()
    {
        var land = new LandOccupancy(FarmGame.MapSize * FarmGame.MapSize);
        for (int row = 0; row < FarmGame.MapSize; row += 3)
            for (int column = 0; column < FarmGame.MapSize; column += 6)
                land.Place(row * FarmGame.MapSize + column, BuildingKind.Farm);
        IReadOnlyList<BuildingSpaceSnapshot> farms = land.Instances;
        var queryWarmup = new WeatherSystem(34, WeatherMode.Explicit);
        for (int query = 0; query < 1000; query++)
            queryWarmup.GetFarmWeather(farms[0]);
        var sunny = new WeatherSystem(34, WeatherMode.Explicit);
        var regions = new WeatherSystem(34, WeatherMode.Explicit);
        if (!regions.TrySetRegion(new Rect2I(96, 96, 96, 192), WeatherKind.Rain, out _) ||
            !regions.TrySetRegion(new Rect2I(192, 96, 96, 192), WeatherKind.Cloudy, out _) ||
            !regions.TrySetRegion(new Rect2I(96, 288, 192, 96), WeatherKind.Rain, out _))
            return 1;
        var measurements = new List<object>();
        foreach (WeatherSystem weather in new[] { sunny, regions })
        {
            int expectedPerSweep = 0;
            var expectedWeather = new WeatherKind[farms.Count];
            for (int i = 0; i < farms.Count; i++)
            {
                WeatherKind expected = WeatherKind.Sunny;
                foreach (Vector2I offset in farms[i].Footprint.Offsets)
                {
                    WeatherKind actual = weather.GetWeather(farms[i].AnchorCell + offset);
                    if (actual == WeatherKind.Rain)
                    {
                        expected = WeatherKind.Rain;
                        break;
                    }
                    if (actual == WeatherKind.Cloudy)
                        expected = WeatherKind.Cloudy;
                }
                expectedWeather[i] = expected;
                expectedPerSweep += (int)expected;
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            int coldChecksum = 0;
            long coldAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            long coldStart = Stopwatch.GetTimestamp();
            for (int i = 0; i < farms.Count; i++)
            {
                WeatherKind actual = weather.GetFarmWeather(farms[i]);
                if (actual != expectedWeather[i])
                    return 1;
                coldChecksum += (int)actual;
            }
            double coldElapsedMs = Stopwatch.GetElapsedTime(coldStart).TotalMilliseconds;
            long coldAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - coldAllocatedBefore;
            const int SimulationSeconds = 16;
            const int QueriesPerSecond = 3;
            var sweepMilliseconds = new double[SimulationSeconds * QueriesPerSecond];
            int checksum = 0;
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            for (int sweep = 0; sweep < sweepMilliseconds.Length; sweep++)
            {
                long sweepStart = Stopwatch.GetTimestamp();
                for (int i = 0; i < farms.Count; i++)
                    checksum += (int)weather.GetFarmWeather(farms[i]);
                sweepMilliseconds[sweep] = Stopwatch.GetElapsedTime(sweepStart).TotalMilliseconds;
            }
            double elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            if (checksum != expectedPerSweep * sweepMilliseconds.Length)
                return 1;
            Array.Sort(sweepMilliseconds);
            measurements.Add(new
            {
                ActiveRegions = weather.ActiveRegions.Count,
                FarmCount = farms.Count,
                QueryCount = farms.Count * sweepMilliseconds.Length,
                SimulationSeconds,
                QueriesPerSecond,
                ColdSweepElapsedMs = coldElapsedMs,
                ColdSweepAllocatedBytes = coldAllocatedBytes,
                ColdSweepChecksum = coldChecksum,
                ElapsedMs = elapsedMs,
                AllocatedBytes = allocatedBytes,
                BytesPerQuery = (double)allocatedBytes / (farms.Count * sweepMilliseconds.Length),
                P95SweepMs = sweepMilliseconds[(int)Math.Ceiling(sweepMilliseconds.Length * .95) - 1],
                Checksum = checksum,
                ExpectedChecksum = expectedPerSweep * sweepMilliseconds.Length,
            });
            GD.Print(FormattableString.Invariant(
                $"天气查询探针：{weather.ActiveRegions.Count} 区；冷扫 {coldElapsedMs:F3} ms/{coldAllocatedBytes} bytes；{farms.Count * sweepMilliseconds.Length} 次热查询 {elapsedMs:F3} ms/{allocatedBytes} bytes，校验和 {checksum}"));
        }
        string directory = ProjectSettings.GlobalizePath("res://coverage/weather-query-probe");
        System.IO.Directory.CreateDirectory(directory);
        System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "performance.json"),
            JsonSerializer.Serialize(measurements, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    private static bool CheckConfiguration()
    {
        string json = FileAccess.GetFileAsString(WeatherRules.ResourcePath);
        WeatherRules rules = WeatherRules.Parse(json);
        if (rules.CycleSeconds != 172800 || rules.CloudyWeight != 4 || rules.RainWeight != 3 ||
            rules.MinimumIntervalDays != 1 || rules.MaximumIntervalDays != 3 ||
            rules.MinimumDurationDays != 2 || rules.MaximumDurationDays != 4 ||
            rules.MinimumRegionSizeCells != 96 || rules.MaximumRegionSizeCells != 192 ||
            rules.MaximumActiveRegions != 3)
            return Fail("正式天气数据与确认的 Demo 基线不一致");
        foreach (string invalid in new[]
        {
            "null", "{}", json.Replace("\"CloudyWeight\": 4", "\"CloudyWeight\": 0"),
            json.Replace("\"RainWeight\": 3", "\"RainWeight\": -1"),
            json.Replace("\"DaysPerYear\": 336", "\"DaysPerYear\": 335"),
            json.Replace("\"MaximumRegionSizeCells\": 192", "\"MaximumRegionSizeCells\": 385"),
            json.Replace("\"MaximumDurationDays\": 4", "\"MaximumDurationDays\": 1"),
            json.Replace("\"MinimumIntervalDays\": 1", "\"MinimumIntervalDays\": 0"),
            json.Replace("\"MaximumActiveRegions\": 3", "\"MaximumActiveRegions\": 0"),
            json.Replace("\"CycleYears\": 10", "\"CycleYears\": 2147483647"),
            json.Replace("\"RainWeight\": 3", "\"RainWeight\": 2147483647"),
            json.Replace("\"CloudyWeight\": 4", "\"TypoWeight\": 4"),
        })
            if (!Throws<JsonException>(() => WeatherRules.Parse(invalid)))
                return Fail("非法或缺失天气数据未明确拒绝");
        return true;
    }

    private static bool CheckExplicitRegions()
    {
        var weather = new WeatherSystem(23, WeatherMode.Explicit);
        IReadOnlyList<WeatherRegionSnapshot> initial = weather.ActiveRegions;
        Rect2I first = new(0, 0, 10, 10);
        if (initial.Count != 0 || weather.GetWeather(Vector2I.Zero) != WeatherKind.Sunny ||
            weather.GetNextEventSeconds(0) != uint.MaxValue ||
            !weather.TrySetRegion(first, WeatherKind.Rain, out string reason) || reason != "" ||
            initial.Count != 0 || weather.GetWeather(new Vector2I(9, 9)) != WeatherKind.Rain ||
            weather.GetWeather(new Vector2I(10, 9)) != WeatherKind.Sunny)
            return Fail("默认晴天、半开区域边缘或旧快照错误");
        IReadOnlyList<WeatherRegionSnapshot> one = weather.ActiveRegions;
        foreach (Rect2I invalid in new[]
        {
            new Rect2I(-1, 0, 1, 1), new Rect2I(0, -1, 1, 1), new Rect2I(0, 0, 0, 1),
            new Rect2I(0, 0, 1, 0), new Rect2I(383, 383, 2, 1),
            new Rect2I(0, 383, 1, 2), new Rect2I(int.MaxValue, 0, int.MaxValue, 1),
            new Rect2I(9, 9, 10, 10),
        })
            if (weather.TrySetRegion(invalid, WeatherKind.Cloudy, out reason) || reason == "" ||
                !ReferenceEquals(one, weather.ActiveRegions))
                return Fail("非法或重叠矩形修改了天气状态");
        if (weather.TrySetRegion(new Rect2I(20, 20, 1, 1), WeatherKind.Sunny, out _) ||
            weather.TrySetRegion(new Rect2I(20, 20, 1, 1), (WeatherKind)9, out _) ||
            weather.TryClearRegion(new Rect2I(0, 0, 9, 9), out _) ||
            !ReferenceEquals(one, weather.ActiveRegions) ||
            !weather.TrySetRegion(new Rect2I(10, 0, 10, 10), WeatherKind.Cloudy, out _) ||
            !weather.TrySetRegion(new Rect2I(20, 0, 10, 10), WeatherKind.Rain, out _) ||
            weather.TrySetRegion(new Rect2I(30, 0, 10, 10), WeatherKind.Cloudy, out _))
            return Fail("天气类型、完整移除、邻边或三片上限错误");
        weather.AdvanceTo(400000);
        if (weather.ActiveRegions.Count != 3 || !weather.TryClearRegion(first, out reason) || reason != "" ||
            weather.GetWeather(Vector2I.Zero) != WeatherKind.Sunny ||
            weather.TryClearRegion(first, out _) || weather.TryClearRegion(new Rect2I(-1, 0, 1, 1), out _) ||
            !Throws<ArgumentOutOfRangeException>(() => weather.AdvanceTo(399999)) ||
            !Throws<ArgumentOutOfRangeException>(() => weather.GetWeather(new Vector2I(384, 0))) ||
            !Throws<ArgumentOutOfRangeException>(() => new WeatherSystem(1, (WeatherMode)99)))
            return Fail("显式天气被自然推进覆盖，或非法输入未拒绝");
        var wholeMap = new WeatherSystem(1, WeatherMode.Explicit);
        if (!wholeMap.TrySetRegion(new Rect2I(0, 0, 384, 384), WeatherKind.Rain, out _) ||
            wholeMap.GetWeather(new Vector2I(383, 383)) != WeatherKind.Rain ||
            !Throws<NotSupportedException>(() => ((IList<WeatherRegionSnapshot>)one).Clear()))
            return Fail("显式全图设置或快照只读约定错误");
        return true;
    }

    private static bool CheckFarmPriority()
    {
        var land = new LandOccupancy(FarmGame.MapSize * FarmGame.MapSize);
        int index = 10 * FarmGame.MapSize + 10;
        land.Place(index, BuildingKind.Farm);
        BuildingSpaceSnapshot farm = land.GetSpace(index)!;
        var weather = new WeatherSystem(1, WeatherMode.Explicit);
        if (weather.GetFarmWeather(farm) != WeatherKind.Sunny ||
            !weather.TrySetRegion(new Rect2I(10, 10, 1, 1), WeatherKind.Cloudy, out _) ||
            weather.GetFarmWeather(farm) != WeatherKind.Cloudy ||
            !weather.TrySetRegion(new Rect2I(12, 12, 1, 1), WeatherKind.Rain, out _) ||
            weather.GetFarmWeather(farm) != WeatherKind.Rain ||
            !weather.TryClearRegion(new Rect2I(12, 12, 1, 1), out _) ||
            weather.GetFarmWeather(farm) != WeatherKind.Cloudy)
            return Fail("九格角落雨或云未决定整田天气");
        land.Place(0, BuildingKind.Road);
        if (!Throws<ArgumentException>(() => weather.GetFarmWeather(land.GetSpace(0)!)))
            return Fail("整田查询接受了非农田实例");
        return true;
    }

    private static bool CheckFarmQueryTransitions()
    {
        var land = new LandOccupancy(FarmGame.MapSize * FarmGame.MapSize);
        var weather = new WeatherSystem(34, WeatherMode.Explicit);
        Rect2I rain = new(96, 96, 96, 192);
        Rect2I cloud = new(192, 96, 96, 192);
        Rect2I tailRain = new(96, 288, 192, 96);
        weather.TrySetRegion(rain, WeatherKind.Rain, out _);
        weather.TrySetRegion(cloud, WeatherKind.Cloudy, out _);
        weather.TrySetRegion(tailRain, WeatherKind.Rain, out _);
        foreach (int row in new[] { 0, 93, 94, 95, 96, 189, 190, 191, 192, 285, 286, 287, 288, 381 })
            foreach (int column in new[] { 0, 93, 94, 95, 96, 189, 190, 191, 192, 285, 286, 287, 288, 381 })
                foreach (BuildingOrientation orientation in Enum.GetValues<BuildingOrientation>())
                {
                    int index = row * FarmGame.MapSize + column;
                    land.Place(index, BuildingKind.Farm, orientation);
                    BuildingSpaceSnapshot farm = land.GetSpace(index)!;
                    if (!FarmQueryMatchesCells(weather, farm))
                        return Fail("跨天气边缘、角落或朝向的冷热整田查询不同");
                    land.Remove(index);
                    land.Place(index, BuildingKind.Road);
                    if (!Throws<ArgumentException>(() => weather.GetFarmWeather(land.GetSpace(index)!)))
                        return Fail("原农田锚点改建道路后复用了农田天气缓存");
                    land.Remove(index);
                    land.Place(index, BuildingKind.Farm, orientation);
                    if (!FarmQueryMatchesCells(weather, land.GetSpace(index)!))
                        return Fail("同锚点拆除重建的固定几何天气改变");
                    land.Remove(index);
                }
        int boundaryIndex = 95 * FarmGame.MapSize + 95;
        land.Place(boundaryIndex, BuildingKind.Farm);
        BuildingSpaceSnapshot boundary = land.GetSpace(boundaryIndex)!;
        if (weather.GetFarmWeather(boundary) != WeatherKind.Rain ||
            weather.TrySetRegion(new Rect2I(95, 95, 3, 3), WeatherKind.Cloudy, out _) ||
            weather.TryClearRegion(new Rect2I(96, 96, 1, 1), out _) ||
            !FarmQueryMatchesCells(weather, boundary) ||
            !weather.TryClearRegion(rain, out _) || weather.GetFarmWeather(boundary) != WeatherKind.Sunny ||
            !weather.TrySetRegion(rain, WeatherKind.Cloudy, out _) || weather.GetFarmWeather(boundary) != WeatherKind.Cloudy ||
            !weather.TryClearRegion(rain, out _) || !FarmQueryMatchesCells(weather, boundary))
            return Fail("显式成功设置或清除后整田天气仍读取旧值，或失败改变真实天气");

        var naturalLand = new LandOccupancy(FarmGame.MapSize * FarmGame.MapSize);
        foreach (int row in new[] { 0, 95, 191, 285, 381 })
            foreach (int column in new[] { 0, 95, 191, 285, 381 })
                naturalLand.Place(row * FarmGame.MapSize + column, BuildingKind.Farm);
        foreach (uint initial in new[] { 0u, 172790u })
        {
            uint elapsed = initial;
            var natural = new WeatherSystem(23, WeatherMode.Natural, elapsed);
            for (int step = 0; step < 12; step++)
            {
                foreach (BuildingSpaceSnapshot farm in naturalLand.Instances)
                    if (!FarmQueryMatchesCells(natural, farm))
                        return Fail("自然天气事件或循环接缝后整田派生值没有同步");
                uint next = elapsed + natural.GetNextEventSeconds(elapsed);
                natural.AdvanceTo(next - 1);
                foreach (BuildingSpaceSnapshot farm in naturalLand.Instances)
                    if (!FarmQueryMatchesCells(natural, farm))
                        return Fail("自然事件前整田派生值提前改变");
                natural.AdvanceTo(next);
                foreach (BuildingSpaceSnapshot farm in naturalLand.Instances)
                    if (!FarmQueryMatchesCells(natural, farm))
                        return Fail("自然事件生效或结束后整田派生值没有同步");
                elapsed = next;
            }
        }
        return true;
    }

    private static bool FarmQueryMatchesCells(WeatherSystem weather, BuildingSpaceSnapshot farm)
    {
        WeatherKind expected = WeatherKind.Sunny;
        foreach (Vector2I offset in farm.Footprint.Offsets)
        {
            WeatherKind kind = weather.GetWeather(farm.AnchorCell + offset);
            if (kind == WeatherKind.Rain)
            {
                expected = kind;
                break;
            }
            if (kind == WeatherKind.Cloudy)
                expected = kind;
        }
        return weather.GetFarmWeather(farm) == expected && weather.GetFarmWeather(farm) == expected;
    }

    private static bool CheckNaturalCycles()
    {
        foreach (int seed in new[] { 1, 23, -500 })
        {
            var weather = new WeatherSystem(seed);
            var equivalent = new WeatherSystem(seed);
            if (weather.ActiveRegions.Count != 0 || weather.CycleSeconds != 172800 ||
                weather.TrySetRegion(new Rect2I(0, 0, 1, 1), WeatherKind.Rain, out _) ||
                weather.TryClearRegion(new Rect2I(0, 0, 1, 1), out _) ||
                weather.GetNextEventSeconds(0) < 52 || weather.GetNextEventSeconds(0) > 155)
                return Fail("自然开局不是晴天，或接受了显式修改");
            var started = new Dictionary<WeatherRegionSnapshot, uint>();
            uint completed = 0;
            bool sawCloud = false;
            bool sawRain = false;
            uint time = 0;
            uint limit = weather.CycleSeconds * 2 + 300;
            while (time < limit)
            {
                uint distance = weather.GetNextEventSeconds(time);
                if (distance == 0 || distance == uint.MaxValue)
                    return Fail("下一自然天气事件距离无效");
                uint next = Math.Min(limit, time + distance);
                IReadOnlyList<WeatherRegionSnapshot> before = weather.ActiveRegions;
                weather.AdvanceTo(next - 1);
                if (!weather.ActiveRegions.SequenceEqual(before))
                    return Fail("自然天气在真实事件前变化");
                weather.AdvanceTo(next);
                equivalent.AdvanceTo(next);
                if (!weather.ActiveRegions.SequenceEqual(equivalent.ActiveRegions) ||
                    !ValidRegions(weather.ActiveRegions))
                    return Fail("相同种子不同批量或循环接缝违反互斥状态");
                foreach (WeatherRegionSnapshot ended in started.Keys.Where(region => !weather.ActiveRegions.Contains(region)).ToArray())
                {
                    uint duration = next - started[ended];
                    if (duration < 102 || duration > 206)
                        return Fail("实际天气持续时间被接缝缩短或超出2～4日");
                    started.Remove(ended);
                    completed++;
                }
                foreach (WeatherRegionSnapshot region in weather.ActiveRegions)
                {
                    started.TryAdd(region, next);
                    sawCloud |= region.Kind == WeatherKind.Cloudy;
                    sawRain |= region.Kind == WeatherKind.Rain;
                }
                time = next;
            }
            uint sample = weather.CycleSeconds + 170;
            var later = new WeatherSystem(seed, WeatherMode.Natural, sample);
            var cyclic = new WeatherSystem(seed, WeatherMode.Natural, sample + weather.CycleSeconds);
            if (completed < 100 || !sawCloud || !sawRain ||
                !later.ActiveRegions.SequenceEqual(cyclic.ActiveRegions))
                return Fail("自然排程未覆盖两种天气或循环复用结果不同");
            var maximum = new WeatherSystem(seed, WeatherMode.Natural, uint.MaxValue);
            if (!ValidRegions(maximum.ActiveRegions) || maximum.GetNextEventSeconds(uint.MaxValue) == 0)
                return Fail("宽累计时间天气查询错误");
        }
        return true;
    }

    private static bool ValidRegions(IReadOnlyList<WeatherRegionSnapshot> regions)
    {
        if (regions.Count > 3)
            return false;
        for (int i = 0; i < regions.Count; i++)
        {
            Rect2I cells = regions[i].Cells;
            if (regions[i].Kind == WeatherKind.Sunny || cells.Position.X < 0 || cells.Position.Y < 0 ||
                cells.End.X > FarmGame.MapSize || cells.End.Y > FarmGame.MapSize ||
                cells.Size.X < 96 || cells.Size.X > 192 || cells.Size.Y < 96 || cells.Size.Y > 192)
                return false;
            for (int j = 0; j < i; j++)
                if (cells.Intersects(regions[j].Cells))
                    return false;
        }
        return true;
    }

    private static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return true; }
        return false;
    }

    private static bool Fail(string message)
    {
        GD.PrintErr($"TestWeatherSystem: {message}");
        return false;
    }
}
