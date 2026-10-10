using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
using FarmExchange.Logging;
using FarmExchange.Time;
using Godot;
using static TestLogging;

public static class TestLoggingFaults
{
    public static bool RunChecks()
    {
        try
        {
            CheckSynchronousSlowWriter();
            CheckInjectedDiskError();
            CheckPersistentFileFailure();
            CheckExceptionPropagationWithFileFailure();
            return true;
        }
        catch (Exception error) { GD.PrintErr("日志故障专项失败：" + error); return false; }
    }

    // 使用实际 File 的下一个滚动路径冲突；不改变宿主磁盘权限，也不消耗磁盘容量。
    internal static void BlockNextRoll(string directory)
    {
        string current = Directory.GetFiles(directory, "*.log").Single();
        string prefix = Path.GetFileNameWithoutExtension(current);
        for (int index = 1; index <= 3; index++)
            Directory.CreateDirectory(Path.Combine(directory, prefix + "_" + index.ToString("000") + ".log"));
    }

    private static void CheckSynchronousSlowWriter()
    {
        using var writer = new ControlledWriter { DelayMilliseconds = 5 };
        using var logging = RuntimeLog.Capture(writer);
        using var game = new FarmGame(130, logging);
        int writesBefore = writer.Attempts;
        var watch = Stopwatch.StartNew();
        var result = game.Buy(new(CropKind.Wheat, CommodityKind.Raw), 1);
        watch.Stop();
        int writes = writer.Attempts - writesBefore;
        Require(result.Success && writes == 3 && watch.ElapsedMilliseconds >= writes * 5,
            "真实 provider 没有同步承受可控慢写，或经营结果改变");
        Require(logging.Health.Health == LoggingHealth.Healthy && writer.ToString().Contains("EventName=TradeFinished"),
            "慢写丢失事件或被误报为失败");
        GD.Print($"慢写证据：{writes} 条真实格式化输出，目标每条延迟 5 ms，命令实测 {watch.Elapsed.TotalMilliseconds:F2} ms；不代表真实慢磁盘 FPS");
    }

    private static void CheckInjectedDiskError()
    {
        using var writer = new ControlledWriter();
        int diagnostics = 0;
        using var logging = RuntimeLog.Capture(writer, diagnostic: _ => diagnostics++);
        using var game = new FarmGame(130, logging);
        writer.Fail = true;
        var commodity = new CommodityId(CropKind.Wheat, CommodityKind.Raw);
        var result = game.Buy(commodity, 2);
        Require(result.Success && game.GetStock(commodity) == 2 && logging.Health.FailureCount == 3 && diagnostics == 1 &&
            logging.Health.KnownLostCount == null, "注入磁盘 IOException 改变业务或遗漏健康事实");
        writer.Fail = false;
        game.Sell(commodity, 1);
        Require(logging.Health.Health == LoggingHealth.Healthy && game.GetStock(commodity) == 1,
            "恢复后业务或目标仍不可用");
        Require(!Lines(writer.ToString()).Any(line => HasEvent(line, "TradeFinished") && line.Contains("Side: \"Buy\"")),
            "恢复重放了缺失交易日志");
    }

    private static void CheckPersistentFileFailure()
    {
        string directory = ProjectSettings.GlobalizePath("res://build/issue130-validation/persistent-file-" + Guid.NewGuid().ToString("N"));
        using var logging = RuntimeLog.OpenFile(directory, false, new(), new(128, 10), _ => { });
        BlockNextRoll(Path.Combine(directory, "runtime"));
        using var game = new FarmGame(130, logging);
        long before = logging.Health.FailureCount;
        for (int index = 0; index < 20; index++) game.Buy(new(CropKind.Wheat, CommodityKind.Raw), 0);
        Require(logging.Health.FailureCount >= before + 60 && logging.Health.Health == LoggingHealth.Unavailable && game.MoneyCents == 5000,
            "运行中真实 File 目标未持续失败，或失败污染经营");
    }

    private static void CheckExceptionPropagationWithFileFailure()
    {
        string directory = ProjectSettings.GlobalizePath("res://build/issue130-validation/exception-file-" + Guid.NewGuid().ToString("N"));
        using var logging = RuntimeLog.OpenFile(directory, false, new(), new(128, 10), _ => { });
        BlockNextRoll(Path.Combine(directory, "runtime"));
        using var game = new FarmGame(130, logging);
        var driver = new SimulationDriver(game.Log!.Time);
        var original = new ProjectedException();
        long before = logging.Health.FailureCount;
        Exception? received = null;
        try
        {
            driver.Advance(1, game, _ =>
            {
                var command = game.Log.Trading.BeginBuy(new(CropKind.Wheat, CommodityKind.Raw), 1)!;
                try { throw original; }
                catch (Exception error) { command.Faulted(error); throw; }
            });
        }
        catch (Exception error) { received = error; }
        Require(ReferenceEquals(received, original) && original.Projections == 1 &&
            logging.Health.FailureCount > before && logging.Health.Health == LoggingHealth.Unavailable,
            "真实 File 异常写入失败改变原异常、造成外层重试或遗漏健康故障");
    }

    private sealed class ProjectedException : Exception
    {
        internal int Projections { get; private set; }
        internal ProjectedException() : base("真实 File 故障下的原业务异常") { }
        public override string ToString() { Projections++; return base.ToString(); }
    }

    private sealed class ControlledWriter : StringWriter
    {
        internal int DelayMilliseconds;
        internal int Attempts;
        internal bool Fail;
        public override void WriteLine(string? value)
        {
            Attempts++;
            if (DelayMilliseconds > 0) Thread.Sleep(DelayMilliseconds);
            // 0x80070070 是磁盘空间不足的 IOException HResult；这是受控注入，不是填满宿主磁盘。
            if (Fail) throw new IOException("受控磁盘空间不足注入", unchecked((int)0x80070070));
            base.WriteLine(value);
        }
    }
}
