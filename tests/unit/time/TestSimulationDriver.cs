using System;
using System.Threading.Tasks;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Land;
using FarmExchange.Time;

public partial class TestSimulationDriver : Node
{
    public override async void _Ready()
    {
        try { GetTree().Quit(RunChecks() && await RunNavigationChecks(GetTree()) ? 0 : 1); }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }

    public static bool RunChecks()
    {
        var game = new FarmGame(12345, weatherMode: FarmExchange.Weather.WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
        var driver = new SimulationDriver();
        if (driver.Advance(0.5, game) != 0 || driver.Progress != 0.5) return Fail("未保存半tick");
        game.SetPaused(true);
        driver.SetRate(2);
        if (driver.Advance(100, game) != 0 || driver.Progress != 0.5) return Fail("暂停/改速丢失半tick或累计暂停时间");
        game.SetPaused(false);
        if (driver.Advance(0.25, game) != 1 || game.Calendar.ElapsedSeconds != 1 || driver.Progress != 0)
            return Fail("恢复与改速没有准确消费未完成tick");
        int playerChanges = 0;
        driver.RateChanged += (_, source) => { if (source == SimulationRateSource.Player) playerChanges++; };
        driver.SetRate(2);
        if (playerChanges != 1) return Fail("同倍率主动选择没有发出Player来源");
        foreach (double invalid in new[] { 0, -1, 1.5, double.NaN, double.PositiveInfinity, 3 })
        {
            try { driver.SetRate(invalid); return Fail("公共接口允许非法或开发倍率"); }
            catch (ArgumentOutOfRangeException) { }
        }
        foreach (double invalid in new[] { -1, double.NaN, double.PositiveInfinity })
        {
            try { driver.Advance(invalid, game); return Fail("驱动接受非法现实时间"); }
            catch (ArgumentOutOfRangeException) { }
        }
        driver.SetDevelopmentRate(16, SimulationRateSource.Scenario);
        if (driver.Advance(0.125, game) != 2 || game.Calendar.ElapsedSeconds != 3)
            return Fail("开发倍率没有使用同一经营入口");
        foreach (double rate in new[] { 0.5, 1, 2, 5, 10, 16 })
            if (!SimulationDriver.IsDevelopmentRateAllowed(rate)) return Fail("合法开发倍率被拒绝");
        foreach (double invalid in new[] { 0, -1, 0.25, 1.5, 17, 20, double.NaN, double.PositiveInfinity })
        {
            try { driver.SetDevelopmentRate(invalid); return Fail("非法开发倍率未被拒绝"); }
            catch (ArgumentOutOfRangeException) { }
        }
        try { driver.Advance(double.MaxValue, game); return Fail("溢出推进未拒绝"); }
        catch (InvalidOperationException) { }
        if (game.Calendar.ElapsedSeconds != 3) return Fail("溢出拒绝修改了经营对象");

        var switched = new FarmGame(12345, weatherMode: FarmExchange.Weather.WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
        var switchingDriver = new SimulationDriver();
        uint boundary = 1;
        uint advanced = switchingDriver.Advance(2, switched, point =>
        {
            if (point.ElapsedSeconds == 1)
            {
                switchingDriver.SetRate(2, SimulationRateSource.Scenario);
                boundary = uint.MaxValue;
                return false;
            }
            return true;
        }, () => boundary);
        if (advanced != 3 || switched.Calendar.ElapsedSeconds != 3 || switchingDriver.Progress != 0)
            return Fail("同帧边界换速没有按新倍率消费余下现实时间");
        uint capped = switchingDriver.Advance(1, switched, _ => false, () => switched.Calendar.ElapsedSeconds < 4 ? 1u : 0);
        if (capped != 1 || switched.Calendar.ElapsedSeconds != 4) return Fail("驱动跨过宿主终点");
        return CheckCheckpointRecovery() && CheckQuietRecovery() && CheckRejectedProgress() &&
            CheckRejectedRoadCommands() && CheckNavigationErrors();
    }

    public static async Task<bool> RunNavigationChecks(SceneTree tree)
    {
        foreach (bool remove in new[] { false, true })
            foreach (bool keepGoing in new[] { false, true })
                foreach (double delta in new[] { 1.0, 2.0 })
                    if (!await CheckCheckpointRoadChange(tree, remove, keepGoing, delta)) return false;
        return await CheckNavigationFraction(tree);
    }

    private static async Task<bool> CheckCheckpointRoadChange(SceneTree tree, bool remove, bool keepGoing, double delta)
    {
        using var game = new FarmGame(12345, weatherMode: FarmExchange.Weather.WeatherMode.Explicit,
            terrainRules: TerrainTestFixtures.AllLandRules);
        Vector2I road = new(10, 10);
        if (remove)
        {
            if (!game.TryPlaceRoads(new[] { road }, RoadMaterial.Dirt).Success) return Fail("拆路驱动夹具建路失败");
            await game.WaitForNavigationAsync(tree);
        }
        var driver = new SimulationDriver();
        int checkpoints = 0;
        bool changed = false;
        uint advanced = driver.Advance(delta, game, _ =>
        {
            checkpoints++;
            changed = remove ? game.TryRemoveRoads(new[] { road }).Success :
                game.TryPlaceRoads(new[] { road }, RoadMaterial.Dirt).Success;
            return keepGoing;
        });
        if (!changed || checkpoints != 1 || advanced != 1 || driver.LastAdvanceTicks != 1 ||
            game.Calendar.ElapsedSeconds != 1 || driver.Progress != 0 || game.IsNavigationReady ||
            game.MoneyCents != 4900 || game.GetPlot(road).Building != (remove ? BuildingKind.None : BuildingKind.Road))
            return Fail($"检查点道路变更后未保留完整秒并停止：拆路={remove}，继续={keepGoing}，帧秒={delta}");
        var spaces = game.GetBuildingSpaces();
        if (driver.Advance(100, game, _ => throw new InvalidOperationException("等待不应触发检查点"),
            () => throw new InvalidOperationException("等待不应请求宿主预算")) != 0 || driver.LastAdvanceTicks != 0 ||
            driver.Progress != 0 || game.Calendar.ElapsedSeconds != 1 || game.MoneyCents != 4900 ||
            !ReferenceEquals(spaces, game.GetBuildingSpaces()))
            return Fail("导航等待期间累计时间或改变已提交经营状态");
        await game.WaitForNavigationAsync(tree);
        if (!game.IsNavigationReady || driver.Advance(0.25, game) != 0 || driver.Progress != 0.25 ||
            driver.Advance(0.75, game) != 1 || driver.LastAdvanceTicks != 1 ||
            game.Calendar.ElapsedSeconds != 2 || driver.Progress != 0)
            return Fail("道路恢复后追赶了等待或检查点后剩余现实时间");
        return true;
    }

    private static async Task<bool> CheckNavigationFraction(SceneTree tree)
    {
        using var game = new FarmGame(12345, weatherMode: FarmExchange.Weather.WeatherMode.Explicit,
            terrainRules: TerrainTestFixtures.AllLandRules);
        var driver = new SimulationDriver();
        if (driver.Advance(0.5, game) != 0 || driver.Progress != 0.5 ||
            !game.TryPlaceRoads(new[] { new Vector2I(10, 10) }, RoadMaterial.Dirt).Success)
            return Fail("导航等待半 tick 夹具失败");
        for (int frame = 0; frame < 8; frame++)
            if (driver.Advance(0.125, game) != 0 || driver.Progress != 0.5 || driver.LastAdvanceTicks != 0)
                return Fail("导航等待的小帧累计或丢失了已有半 tick");
        game.SetPaused(true);
        driver.SetRate(2);
        if (driver.Advance(100, game) != 0 || driver.Progress != 0.5)
            return Fail("导航等待时暂停改速丢失已有半 tick");
        game.SetPaused(false);
        if (driver.Advance(0.125, game) != 0 || driver.Progress != 0.5)
            return Fail("取消暂停后仍未就绪却接纳了帧时间");
        game.SetPaused(true);
        await game.WaitForNavigationAsync(tree);
        if (!game.IsPaused || !game.IsNavigationReady || driver.Advance(100, game) != 0 || driver.Progress != 0.5)
            return Fail("导航完成改变暂停或累计暂停时间");
        game.SetPaused(false);
        if (driver.Advance(0.125, game) != 0 || driver.Progress != 0.75 ||
            driver.Advance(0.125, game) != 1 || game.Calendar.ElapsedSeconds != 1 || driver.Progress != 0 ||
            driver.Advance(0.25, game) != 0 || driver.Progress != 0.5 || game.Calendar.ElapsedSeconds != 1)
            return Fail("恢复后未按新倍率恰好消费一次等待前小数");
        return true;
    }

    private static bool CheckRejectedRoadCommands()
    {
        using var game = new FarmGame(12345, weatherMode: FarmExchange.Weather.WeatherMode.Explicit,
            terrainRules: TerrainTestFixtures.AllLandRules);
        var driver = new SimulationDriver();
        bool rejected = true;
        uint advanced = driver.Advance(2, game, _ =>
        {
            rejected &= !game.TryPlaceRoads(new[] { new Vector2I(-1, 0) }, RoadMaterial.Dirt).Success &&
                !game.TryRemoveRoads(new[] { new Vector2I(10, 10) }).Success;
            return false;
        });
        return rejected && advanced == 2 && driver.LastAdvanceTicks == 2 && driver.Progress == 0 &&
            game.Calendar.ElapsedSeconds == 2 && game.MoneyCents == 5000 && game.IsNavigationReady ||
            Fail("失败道路命令错误阻止驱动继续使用本帧时间");
    }

    private static bool CheckNavigationErrors()
    {
        using var game = new FarmGame(12345, weatherMode: FarmExchange.Weather.WeatherMode.Explicit,
            terrainRules: TerrainTestFixtures.AllLandRules);
        var driver = new SimulationDriver();
        driver.Advance(0.5, game);
        var original = new InvalidOperationException("建路后的真实宿主错误");
        try
        {
            driver.Advance(1.5, game, _ =>
            {
                game.TryPlaceRoads(new[] { new Vector2I(10, 10) }, RoadMaterial.Dirt);
                throw original;
            });
            return Fail("导航变化掩盖了真实检查点异常");
        }
        catch (InvalidOperationException received)
        {
            if (!ReferenceEquals(original, received) || driver.LastAdvanceTicks != 1 || driver.Progress != 0 ||
                game.Calendar.ElapsedSeconds != 1 || game.MoneyCents != 4900 || game.IsNavigationReady)
                return Fail("建路后真实异常未原样传播或改变已提交完整秒");
        }
        try { driver.Advance(double.NaN, game); return Fail("导航等待掩盖了非法现实时间"); }
        catch (ArgumentOutOfRangeException) { }
        foreach (bool pauseBeforeDisposal in new[] { false, true })
        {
            foreach (bool keepGoing in new[] { false, true })
            {
                using var disposedAtCheckpoint = new FarmGame(12345, weatherMode: FarmExchange.Weather.WeatherMode.Explicit,
                    terrainRules: TerrainTestFixtures.AllLandRules);
                var disposingDriver = new SimulationDriver();
                try
                {
                    disposingDriver.Advance(2, disposedAtCheckpoint, _ =>
                    {
                        if (pauseBeforeDisposal) disposedAtCheckpoint.SetPaused(true);
                        disposedAtCheckpoint.Dispose();
                        return keepGoing;
                    });
                    return Fail($"检查点释放局被当作正常等待：先暂停={pauseBeforeDisposal}，继续={keepGoing}");
                }
                catch (ObjectDisposedException)
                {
                    if (disposingDriver.LastAdvanceTicks != 1 || disposingDriver.Progress != 0 ||
                        disposedAtCheckpoint.Calendar.ElapsedSeconds != 1)
                        return Fail("检查点释放异常丢失已完成秒");
                }
            }
        }
        foreach (bool pauseBeforeDisposal in new[] { false, true })
        {
            using var disposed = new FarmGame(12345, weatherMode: FarmExchange.Weather.WeatherMode.Explicit,
                terrainRules: TerrainTestFixtures.AllLandRules);
            var disposedDriver = new SimulationDriver();
            disposedDriver.Advance(0.5, disposed);
            if (pauseBeforeDisposal) disposed.SetPaused(true);
            disposed.Dispose();
            try { disposedDriver.Advance(0.5, disposed); return Fail($"释放局被当作正常等待：先暂停={pauseBeforeDisposal}"); }
            catch (ObjectDisposedException)
            {
                if (disposedDriver.Progress != 0.5 || disposedDriver.LastAdvanceTicks != 0)
                    return Fail("释放局拒绝修改了已有小数");
            }
        }
        return true;
    }

    private static bool CheckCheckpointRecovery()
    {
        using var game = new FarmGame(12345, weatherMode: FarmExchange.Weather.WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
        var driver = new SimulationDriver();
        driver.Advance(0.5, game);
        var original = new InvalidOperationException("半 tick 检查点异常");
        try { driver.Advance(0.5, game, _ => throw original); return Fail("检查点异常没有传播"); }
        catch (InvalidOperationException received)
        {
            if (!ReferenceEquals(original, received) || game.Calendar.ElapsedSeconds != 1 || driver.Progress != 0 ||
                driver.LastAdvanceTicks != 1)
                return Fail("检查点失败后没有提交已完成秒所消费的旧小数");
        }
        if (driver.Advance(0.5, game) != 0 || game.Calendar.ElapsedSeconds != 1 || driver.Progress != 0.5)
            return Fail("累计现实 1.5 秒重复推进旧半 tick");
        game.SetPaused(true);
        driver.SetRate(2);
        if (driver.Advance(20, game) != 0 || driver.Progress != 0.5 || driver.LastAdvanceTicks != 0)
            return Fail("异常后暂停或改速修改了未完成小数");
        game.SetPaused(false);
        if (driver.Advance(0.25, game) != 1 || game.Calendar.ElapsedSeconds != 2 || driver.Progress != 0)
            return Fail("异常后恢复改速未准确完成下一秒");
        return true;
    }

    private static bool CheckQuietRecovery()
    {
        using var game = new FarmGame(12345, weatherMode: FarmExchange.Weather.WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
        foreach (var space in game.GetBuildingSpaces())
            if (game.RemoveBuilding(space.AnchorCell) != null) return Fail("平静推进夹具未能清空设施");
        var driver = new SimulationDriver();
        driver.Advance(0.5, game);
        bool wasQuiet = false;
        var original = new InvalidOperationException("平静区间检查点异常");
        try
        {
            driver.Advance(1.5, game, point => { wasQuiet = !point.IsEvent; throw original; });
            return Fail("平静区间检查点异常未传播");
        }
        catch (InvalidOperationException received)
        {
            if (!ReferenceEquals(original, received) || !wasQuiet || driver.LastAdvanceTicks != 2 ||
                game.Calendar.ElapsedSeconds != 2 || driver.Progress != 0)
                return Fail("平静完整区间未在异常前提交进度");
        }
        if (driver.Advance(0.5, game) != 0 || game.Calendar.ElapsedSeconds != 2 || driver.Progress != 0.5)
            return Fail("平静区间异常后旧小数被重复计算");
        return true;
    }

    private static bool CheckRejectedProgress()
    {
        using var game = new FarmGame(12345, uint.MaxValue, weatherMode: FarmExchange.Weather.WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
        var driver = new SimulationDriver();
        driver.Advance(0.5, game);
        int money = game.MoneyCents;
        var spaces = game.GetBuildingSpaces();
        foreach (double delta in new[] { -1, double.NaN, double.MaxValue, 0.5 })
        {
            try { driver.Advance(delta, game); return Fail("非法输入或日历容量未拒绝"); }
            catch (ArgumentOutOfRangeException) { }
            catch (InvalidOperationException) { }
            if (driver.Progress != 0.5 || driver.LastAdvanceTicks != 0 ||
                game.Calendar.ElapsedSeconds != uint.MaxValue || game.MoneyCents != money ||
                !ReferenceEquals(spaces, game.GetBuildingSpaces()))
                return Fail("推进前拒绝修改了小数、日历或资源");
        }
        var original = new InvalidOperationException("宿主预算异常");
        try { driver.Advance(0.5, game, maxTicks: () => throw original); return Fail("宿主预算异常未传播"); }
        catch (InvalidOperationException received)
        {
            if (!ReferenceEquals(original, received) || driver.Progress != 0.5 || driver.LastAdvanceTicks != 0)
                return Fail("宿主预算前置拒绝修改了已有小数");
        }
        using var resumed = new FarmGame(12345, weatherMode: FarmExchange.Weather.WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
        var resumedDriver = new SimulationDriver();
        resumedDriver.Advance(0.5, resumed);
        int budgets = 0;
        try
        {
            resumedDriver.Advance(2, resumed, _ => false, () => ++budgets == 1 ? 1u : throw original);
            return Fail("后续预算异常未传播");
        }
        catch (InvalidOperationException received)
        {
            if (!ReferenceEquals(original, received) || resumedDriver.LastAdvanceTicks != 1 ||
                resumed.Calendar.ElapsedSeconds != 1 || resumedDriver.Progress != 0)
                return Fail("后续预算拒绝丢失本帧此前已提交秒");
        }
        return true;
    }

    private static bool Fail(string message) { GD.PushError(message); return false; }
}
