using System;
using System.IO;
using System.Linq;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Logging;
using FarmExchange.Time;

public partial class TestTimeLogging : Node
{
    public override void _Ready() => GetTree().Quit(RunChecks() ? 0 : 1);

    public static bool RunChecks()
    {
        try
        {
            CheckPause();
            CheckRate();
            CheckObservationEnd();
            CheckIsolation();
            return true;
        }
        catch (Exception error) { GD.PrintErr("时间日志检查失败：" + error); return false; }
    }

    private static void CheckPause()
    {
        using var text = new StringWriter();
        using var log = RuntimeLog.Capture(text);
        using var game = new FarmGame(17, log);
        game.SetPaused(false);
        game.SetPaused(true);
        game.SetPaused(true);
        game.SetPaused(false, CommandOrigin.Scenario);
        string[] changed = Events(text, "PauseChanged");
        Require(changed.Length == 2 && changed[0].Contains("IsPaused: true") &&
            changed[1].Contains("IsPaused: false") && changed[1].Contains("CommandOrigin: \"Scenario\""),
            "暂停重复写状态或丢失真实来源");
        Require(Events(text, "CommandReceived").Length == 4 && Events(text, "CommandFinished").Length == 4 &&
            Events(text, "CommandReceived").All(line => line.Contains("CommandName: \"SetPaused\"") &&
                line.Contains("CommandArguments: { IsPaused:")), "同值暂停没有完整指令关联");
        uint before = game.Calendar.ElapsedSeconds;
        game.SetPaused(true);
        game.AdvanceTicks(10);
        Require(game.Calendar.ElapsedSeconds == before, "日志改变暂停推进规则");
    }

    private static void CheckRate()
    {
        using var text = new StringWriter();
        using var log = RuntimeLog.Capture(text);
        using var game = new FarmGame(17, log);
        var driver = new SimulationDriver(game.Log!.Time);
        driver.Advance(0.25, game);
        game.SetPaused(true);
        int notified = 0;
        driver.RateChanged += (_, _) =>
        {
            notified++;
            Require(Events(text, "SimulationRateSelected").Length == notified &&
                Events(text, "CommandFinished").Last().Contains("SelectSimulationRate"),
                "倍率原通知早于已接受事实或指令结束");
        };
        driver.SetRate(1);
        driver.SetRate(2, SimulationRateSource.Scenario);
        driver.SetRate(2);
        driver.SetRate(0.5);
        string[] selections = Events(text, "SimulationRateSelected");
        Require(selections.Length == 4 && selections[0].Contains("ValueChanged: false") &&
            selections[0].Contains("PreviousRate: 1") && selections[1].Contains("Source: \"Scenario\"") &&
            selections[1].Contains("PreviousRate: 1") && selections[1].Contains("EffectiveRate: 2") &&
            selections[2].Contains("ValueChanged: false") && selections[3].Contains("EffectiveRate: 0.5"),
            "倍率同值或前后实际值不正确");
        foreach (double invalid in new[] { 0d, 3d, double.NaN, double.PositiveInfinity })
        {
            try { driver.SetRate(invalid); throw new InvalidOperationException("非法倍率被接受"); }
            catch (ArgumentOutOfRangeException) { }
        }
        Require(notified == 4 && Events(text, "SimulationRateSelected").Length == 4 &&
            driver.Rate == 0.5 && driver.Progress == 0.25 && game.IsPaused && driver.Advance(100, game) == 0,
            "非法倍率产生接受事件或改速解除暂停/丢失小数进度");
#if DEBUG
        driver.SetDevelopmentRate(20, SimulationRateSource.Scenario);
        Require(Events(text, "SimulationRateSelected").Last().Contains("RequestedRate: 20"), "开发倍率未走同一观察");
#endif
        var original = new InvalidOperationException("原倍率通知异常");
        driver.RateChanged += (_, _) => throw original;
        try { driver.SetRate(1); throw new Exception("原通知异常被吞掉"); }
        catch (InvalidOperationException error) { Require(ReferenceEquals(error, original), "原通知异常被替换"); }
        Require(driver.Rate == 1 && Events(text, "SimulationRateSelected").Last().Contains("EffectiveRate: 1"),
            "通知异常回滚了已接受选择");
    }

    private static void CheckObservationEnd()
    {
        using var text = new StringWriter();
        using var log = RuntimeLog.Capture(text);
        using var game = new FarmGame(17, log);
        var pause = game.Log!.Time.BeginPause(true)!;
        pause.Faulted(new InvalidOperationException("原暂停异常"));
        pause.Complete(true);
        pause.Faulted(new Exception("重复"));
        var rate = game.Log.Time.BeginRate(2, 1, SimulationRateSource.Player)!;
        rate.Faulted(new InvalidOperationException("原选择异常"));
        rate.Complete(2);
        var accepted = game.Log.Time.BeginRate(1, 1, SimulationRateSource.Scenario)!;
        accepted.Complete(1);
        accepted.Complete(1);
        accepted.Faulted(new Exception("重复"));
        Require(Events(text, "BusinessException").Length == 2 && Events(text, "CommandFinished").Length == 3 &&
            Events(text, "SimulationRateSelected").Length == 1 && Events(text, "PauseChanged").Length == 0,
            "异常或同一观察重复结束");
        game.Dispose();
        Require(game.Log.Time.BeginPause(false) == null && game.Log.Time.BeginRate(1, 1, SimulationRateSource.Player) == null,
            "已关闭局仍创建时间观察");
    }

    private static void CheckIsolation()
    {
        using var text = new StringWriter();
        using var log = RuntimeLog.Capture(text);
        using var disabled = RuntimeLog.Disabled();
        using var brokenWriter = new BrokenWriter();
        using var brokenLog = RuntimeLog.Capture(brokenWriter, diagnostic: _ => { });
        using var traced = new FarmGame(17, log);
        using var muted = new FarmGame(17, disabled);
        using var broken = new FarmGame(17, brokenLog);
        using var plain = new FarmGame(17);
        foreach (var game in new[] { traced, muted, broken, plain })
        {
            var driver = new SimulationDriver(game.Log?.Time);
            driver.Advance(0.5, game);
            game.SetPaused(true);
            driver.SetRate(2);
            driver.Advance(100, game);
            game.SetPaused(false);
            driver.Advance(1, game);
            SimulationRateSource? observed = null;
            driver.RateChanged += (_, source) => observed = source;
            driver.SetRate(2, (SimulationRateSource)99);
            Require(game.Calendar.ElapsedSeconds == 2 && driver.Progress == 0.5 && game.MoneyCents == traced.MoneyCents,
                "日志开关/写入故障影响时间状态");
            Require(observed == (SimulationRateSource)99 && driver.Rate == 2, "日志改变旧入口非法来源的原样通知行为");
        }
        Require(Events(text, "SimulationRateSelected").Last().Contains("Source: \"99\""), "未知来源被解释为合法枚举");
        Require(brokenLog.Health.FailureCount > 0, "故障输出未记入日志健康");
    }

    private sealed class BrokenWriter : StringWriter
    {
        public override void WriteLine(string? value) => throw new IOException("时间测试输出故障");
    }

    private static string[] Events(StringWriter text, string name) => text.ToString()
        .Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(line => line.Contains("EventName=" + name + " ")).ToArray();
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
