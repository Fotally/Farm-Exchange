using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
using FarmExchange.Logging;
using FarmExchange.Time;

public static class TestExceptionLogging
{
    private static readonly CommodityId Wheat = new(CropKind.Wheat, CommodityKind.Raw);

    public static bool RunChecks()
    {
        try
        {
            CheckAdvanceFailures();
            CheckDriverFailures();
            CheckPropagationAndIndependentCommands();
            CheckIndependentObservationLifetime();
            CheckGameIsolation();
            CheckProjectionAndOutputFailures();
            CheckNormalAndDisabled();
            return true;
        }
        catch (Exception error) { GD.PrintErr("共用异常观察测试失败：" + error); return false; }
    }

    private static void CheckAdvanceFailures()
    {
        using var text = new StringWriter();
        using var logging = RuntimeLog.Capture(text);
        using var game = new FarmGame(130, logging);
        game.AdvanceTick();
        Exception capacity = Caught(() => game.AdvanceTicks(uint.MaxValue));
        Require(capacity is InvalidOperationException, "批量容量异常类型改变");
        AssertException(Events(text, "BusinessException").Single(), capacity, "AdvanceValidation", 1, false);
        Require(capacity.StackTrace!.Contains("AdvanceTicksCore"), "批量容量原抛出位置丢失");

        var original = new InvalidOperationException("检查点原异常及堆栈");
        Exception received = Caught(() => game.AdvanceTicks(1, _ => ThrowAtCheckpoint(original)));
        Require(ReferenceEquals(original, received) && received.StackTrace!.Contains(nameof(ThrowAtCheckpoint)),
            "检查点异常对象或原抛出位置被替换");
        AssertException(Events(text, "BusinessException").Last(), original, "Checkpoint", 2, false);
        Require(Events(text, "BusinessException").Length == 2, "批量或检查点异常重复记录");
        game.Dispose();
        Require(Events(text, "PerformanceSummary").Single(line => line.Contains("GameAdvance"))
            .Contains("FailedBatchCount: 2"), "异常记录破坏失败批次统计");
        Require(Events(text, "ProductionSummary").Last().Contains("BoundaryMissing"), "异常记录遗漏生产覆盖缺口");

        // 复用已有日历构造夹具，真实调用单 tick 容量检查；不反射修改私有日历。
        using var exhausted = new FarmGame(130, uint.MaxValue, logging);
        Exception tick = Caught(() => exhausted.AdvanceTick());
        Require(tick is InvalidOperationException && tick.StackTrace!.Contains("AdvanceTickCore"), "单 tick 容量异常改变");
        AssertException(Events(text, "BusinessException").Last(), tick, "AdvanceValidation", uint.MaxValue, false);
        Require(Events(text, "BusinessException").Length == 3 && exhausted.Calendar.ElapsedSeconds == uint.MaxValue,
            "单 tick 异常重复记录或改变日历");
    }

    private static void CheckDriverFailures()
    {
        using var text = new StringWriter();
        using var logging = RuntimeLog.Capture(text);
        using var game = new FarmGame(130, logging);
        var driver = new SimulationDriver(game.Log!.Time);
        foreach (double delta in new[] { double.NaN, -1d, (double)uint.MaxValue + 1 })
        {
            Exception error = Caught(() => driver.Advance(delta, game));
            Require(delta > 0 ? error is InvalidOperationException : error is ArgumentOutOfRangeException,
                "驱动参数/容量异常类型改变");
            AssertException(Events(text, "BusinessException").Last(), error, "DriverValidation", 0, false);
        }
        var budget = new InvalidOperationException("宿主预算原异常");
        Exception received = Caught(() => driver.Advance(1, game, maxTicks: () => ThrowAtBudget(budget)));
        Require(ReferenceEquals(budget, received) && received.StackTrace!.Contains(nameof(ThrowAtBudget)),
            "预算异常对象或抛出堆栈改变");
        AssertException(Events(text, "BusinessException").Last(), budget, "DriverBudget", 0, false);
        Require(game.Calendar.ElapsedSeconds == 0 && driver.Progress == 0 && Events(text, "BusinessException").Length == 4,
            "驱动前置异常推进经营或重复记录");

        var checkpoint = new InvalidOperationException("驱动下的检查点原异常");
        received = Caught(() => driver.Advance(1, game, _ => ThrowAtCheckpoint(checkpoint)));
        Require(ReferenceEquals(checkpoint, received), "驱动替换检查点异常");
        AssertException(Events(text, "BusinessException").Last(), checkpoint, "Checkpoint", 1, false);
        Require(Events(text, "BusinessException").Length == 5, "驱动重复记录已由批量观察的异常");
        game.Dispose();
        Require(Events(text, "PerformanceSummary").Single(line => line.Contains("GameAdvance"))
            .Contains("FailedBatchCount: 1"), "驱动前置异常被误记为已执行批次");
    }

    private static void CheckPropagationAndIndependentCommands()
    {
        using var text = new StringWriter();
        using var logging = RuntimeLog.Capture(text);
        using var game = new FarmGame(130, logging);
        var driver = new SimulationDriver(game.Log!.Time);
        var original = new InvalidOperationException("相同对象可再次用于独立操作");
        for (int attempt = 0; attempt < 2; attempt++)
        {
            Exception received = Caught(() => driver.Advance(1, game, _ => ThrowObservedCommand(game, original)));
            Require(ReferenceEquals(received, original), "嵌套观察替换命令原异常");
        }
        string[] failures = Events(text, "BusinessException");
        Require(failures.Length == 2 && failures.Select(line => Field(line, "CommandId")).Distinct().Count() == 2,
            "同次命令传播重复或后续独立调用被永久去重");
        AssertException(failures[0], original, "Command", 1, true);
        AssertException(failures[1], original, "Command", 2, true);

        // 公开领域观察的契约测试：没有伪称真实买入命令本身抛出此异常。
        driver.Advance(1, game, _ =>
        {
            for (int sibling = 0; sibling < 2; sibling++)
                Require(ReferenceEquals(Caught(() => ThrowObservedCommand(game, original)), original), "兄弟命令异常替换");
            return true;
        });
        Require(Events(text, "BusinessException").Length == 4, "同一外层内的独立兄弟命令被合并");
        var equalMessage = new InvalidOperationException(original.Message);
        Caught(() => driver.Advance(1, game, _ =>
        {
            Caught(() => ThrowObservedCommand(game, original));
            return ThrowAtCheckpoint(equalMessage);
        }));
        failures = Events(text, "BusinessException");
        Require(failures.Length == 6, "不同对象相同消息被合并");
        AssertException(failures.Last(), equalMessage, "Checkpoint", 4, false);
        Require(Events(text, "CommandReceived").Length == 5 && Events(text, "CommandFinished").Length == 5 &&
            Events(text, "CommandFinished").All(line => line.Contains("CommandStatus: \"Faulted\"")) &&
            !Events(text, "TradeFinished").Any(), "传播去重漏记命令终结或伪造交易成功");
        game.Dispose();
        Require(Events(text, "PerformanceSummary").Single(line => line.Contains("GameAdvance"))
            .Contains("FailedBatchCount: 3"), "内部命令失败与外层批次失败统计混淆");
    }

    private static void CheckGameIsolation()
    {
        using var text = new StringWriter();
        using var logging = RuntimeLog.Capture(text);
        using var first = new FarmGame(130, logging);
        using var second = new FarmGame(130, logging);
        string[] identities = Events(text, "GameInitialized").Select(line => Field(line, "GameInstanceId")).ToArray();
        var original = new InvalidOperationException("跨局同对象");
        Exception received = Caught(() => first.AdvanceTicks(1, _ => ThrowObservedCommand(second, original)));
        Require(ReferenceEquals(received, original), "跨局传播改变异常");
        string[] failures = Events(text, "BusinessException");
        Require(failures.Length == 2 && Field(failures[0], "GameInstanceId") == identities[1] &&
            Field(failures[1], "GameInstanceId") == identities[0], "不同经营局的观察凭据或上下文串线");
        AssertException(failures[0], original, "Command", 0, true);
        AssertException(failures[1], original, "Checkpoint", 1, false);
    }

    private static void CheckIndependentObservationLifetime()
    {
        using var text = new StringWriter();
        using var logging = RuntimeLog.Capture(text);
        using var game = new FarmGame(130, logging);
        var original = new InvalidOperationException("非栈式命令观察生命周期");
        var first = game.Log!.Trading.BeginBuy(Wheat, 1)!;
        var second = game.Log.Trading.BeginBuy(Wheat, 1)!;
        first.Faulted(original);
        second.Faulted(original);
        Require(Events(text, "BusinessException").Length == 2, "非 LIFO 结束的独立命令错误共享去重凭据");
        TradeLogOperation? pending = null;
        game.AdvanceTicks(1, _ => { pending = game.Log.Trading.BeginBuy(Wheat, 1); return true; });
        Caught(() => game.AdvanceTicks(1, _ => ThrowAtCheckpoint(original)));
        Require(Events(text, "BusinessException").Length == 3 && Events(text, "CommandFinished").Length == 2,
            "未终结的旧命令污染后续推进或被伪造终结");
        pending!.Faulted(original);
        Caught(() => game.AdvanceTicks(1, _ => ThrowAtCheckpoint(original)));
        Require(Events(text, "BusinessException").Length == 5 && Events(text, "CommandFinished").Length == 3,
            "迟到的旧命令终结重新污染已释放作用域或后续推进");
    }

    private static void CheckProjectionAndOutputFailures()
    {
        using var text = new StringWriter();
        using var logging = RuntimeLog.Capture(text, diagnostic: _ => { });
        using var game = new FarmGame(130, logging);
        var driver = new SimulationDriver(game.Log!.Time);
        var projection = new CountingException("投影故障原异常", failProjection: true);
        Require(ReferenceEquals(Caught(() => driver.Advance(1, game, _ => ThrowObservedCommand(game, projection))), projection),
            "异常文本投影故障替换原异常");
        Require(projection.Projections == 1 && logging.Health.FailureCount > 0, "投影失败被外层重试或未计健康故障");
        Require(Events(text, "CommandFinished").Single().Contains("CommandStatus: \"Faulted\""),
            "异常投影失败连带丢失独立命令终结");

        using var writer = new ExceptionFailingWriter();
        using var broken = RuntimeLog.Capture(writer, diagnostic: _ => { });
        using var brokenGame = new FarmGame(130, broken);
        var brokenDriver = new SimulationDriver(brokenGame.Log!.Time);
        var original = new CountingException("输出故障原异常");
        Require(ReferenceEquals(Caught(() => brokenDriver.Advance(1, brokenGame,
            _ => ThrowObservedCommand(brokenGame, original))), original), "写入故障替换原异常");
        Require(writer.ExceptionAttempts == 1 && original.Projections == 1 && broken.Health.FailureCount > 0,
            "异常输出失败触发外层重投影/重试或遗漏健康故障");
        Require(Events(writer, "CommandFinished").Single().Contains("CommandStatus: \"Faulted\""),
            "异常写入失败遗漏独立终结");
    }

    private static void CheckNormalAndDisabled()
    {
        using var text = new StringWriter();
        using var logging = RuntimeLog.Capture(text);
        using var disabled = RuntimeLog.Disabled();
        using var traced = new FarmGame(130, logging);
        using var muted = new FarmGame(130, disabled);
        using var plain = new FarmGame(130);
        var games = new[] { traced, muted, plain };
        SimulationAdvanceResult? expected = null;
        foreach (var game in games)
        {
            SimulationAdvanceResult result = game.AdvanceTicks(120);
            expected ??= result;
            Require(result == expected.Value && game.MoneyCents == traced.MoneyCents &&
                game.Calendar.ElapsedSeconds == traced.Calendar.ElapsedSeconds, "日志开关改变正常推进");
        }
        // 先完成同一正常终点的对照；检查点异常发生前会真实推进一秒。
        foreach (var game in games)
        {
            var original = new InvalidOperationException("关闭与开启日志均保留原异常");
            Require(ReferenceEquals(Caught(() => new SimulationDriver(game.Log?.Time).Advance(1, game,
                _ => ThrowAtCheckpoint(original))), original), "关闭日志改变异常传播");
        }
        Require(Events(text, "BusinessException").Length == 1 && Events(text, "CommandReceived").Length == 0 &&
            Events(text, "CommandFinished").Length == 0, "自动推进伪造命令或正常推进产生异常");
        string[] names = text.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => Regex.Match(line, @"EventName=(\w+)").Groups[1].Value).Distinct().ToArray();
        Require(names.All(name => name is "SessionStarted" or "GameInitialized" or "BusinessException"),
            "未开启专项诊断的正常短推进新增逐 tick 成功事件：" + string.Join(",", names));
    }

    // 独立方法用于核对 throw; 保留原抛出位置，不以调用方栈顶代替原堆栈。
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool ThrowAtCheckpoint(Exception original) => throw original;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static uint ThrowAtBudget(Exception original) => throw original;

    private static bool ThrowObservedCommand(FarmGame game, Exception original)
    {
        var command = game.Log!.Trading.BeginBuy(Wheat, 1)!;
        try { return ThrowAtCheckpoint(original); }
        catch (Exception error) { command.Faulted(error); throw; }
    }

    private static Exception Caught(Action action)
    {
        try { action(); }
        catch (Exception error) { return error; }
        throw new InvalidOperationException("预期原业务异常没有传播");
    }

    private static void AssertException(string line, Exception error, string phase, uint seconds, bool command)
    {
        Require(line.Contains("ExceptionType: \"" + error.GetType().FullName + "\"") && line.Contains(error.Message) &&
            line.Contains("Exception:") && line.Contains("Phase: \"" + phase + "\"") &&
            line.Contains("SimulationSeconds: " + seconds + ",") && line.Contains("GameDate:") &&
            line.Contains("GameInstanceId:") && line.Contains("IsPaused:"), "异常原字段/真实阶段/经营上下文缺失：" + line);
        Require(command == line.Contains("CommandId:"), "自动推进伪造命令关联或命令丢失关联");
        Require(error.StackTrace != null && line.Contains(error.StackTrace.Split('\n')[0].Trim().Split(" in ")[0]),
            "日志未保留原异常抛出堆栈");
    }

    private sealed class CountingException : Exception
    {
        private readonly bool _failProjection;
        internal int Projections { get; private set; }
        internal CountingException(string message, bool failProjection = false) : base(message) => _failProjection = failProjection;
        public override string ToString()
        {
            Projections++;
            if (_failProjection) throw new IOException("受控异常文本投影失败");
            return base.ToString();
        }
    }

    private sealed class ExceptionFailingWriter : StringWriter
    {
        internal int ExceptionAttempts { get; private set; }
        public override void WriteLine(string? value)
        {
            if (value?.Contains("EventName=BusinessException ") == true)
            {
                ExceptionAttempts++;
                throw new IOException("受控异常输出故障");
            }
            base.WriteLine(value);
        }
    }

    private static string Field(string line, string name) => Regex.Match(line, name + @": (""[^""]*""|[^, }]+)").Groups[1].Value;
    private static string[] Events(StringWriter text, string name) => text.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Where(line => line.Contains("EventName=" + name + " ")).ToArray();
    private static void Require(bool passed, string message) { if (!passed) throw new InvalidOperationException(message); }
}
