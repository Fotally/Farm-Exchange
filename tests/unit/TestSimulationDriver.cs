using System;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Time;

public partial class TestSimulationDriver : Node
{
    public override void _Ready() => GetTree().Quit(RunChecks() ? 0 : 1);

    public static bool RunChecks()
    {
        var game = new FarmGame(12345);
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

        var switched = new FarmGame(12345);
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
        return true;
    }

    private static bool Fail(string message) { GD.PushError(message); return false; }
}
