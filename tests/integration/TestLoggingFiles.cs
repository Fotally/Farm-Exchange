using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
using FarmExchange.Logging;
using static TestLogging;

public static class TestLoggingFiles
{
    public static bool RunChecks()
    {
        try
        {
            string root = ProjectSettings.GlobalizePath("res://build/test-results/logging/" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            CheckSharedFiles(Path.Combine(root, "shared"));
            CheckRetention(Path.Combine(root, "retention"));
            CheckUnavailable(Path.Combine(root, "blocked"));
            CheckRuntimeFileFailure(Path.Combine(root, "runtime-failure"));
            CheckIndependentInitialization(Path.Combine(root, "independent-initialization"));
            CheckDescriptorRouting(Path.Combine(root, "descriptor-routing"));
            using var invalidConfiguration = RuntimeLog.OpenFile(Path.Combine(root, "invalid"), false,
                new LogEnvironment(), new LogFileRetention(RuntimeBytes: 0), _ => { });
            using var game = new FarmGame(17, invalidConfiguration);
            Require(game.Buy(new CommodityId(CropKind.Wheat, CommodityKind.Raw), 1).Success &&
                invalidConfiguration.Health.Health == LoggingHealth.Unavailable, "日志初始化故障未隔离");
            GD.Print("日志真实 File 样例：" + root);
            return true;
        }
        catch (Exception error) { GD.PrintErr("日志 File 集成测试失败：" + error); return false; }
    }

    private static void CheckSharedFiles(string directory)
    {
        var retention = new LogFileRetention(4096, 100, 4096, 100);
        using (var first = RuntimeLog.OpenFile(directory, true,
            new LogEnvironment(GameVersion: "", BuildKind: "Debug", WindowWidth: 0, WindowHeight: 0), retention))
        using (var second = RuntimeLog.OpenFile(directory, true, new LogEnvironment(BuildKind: "Debug"), retention))
        {
            var games = new[] { new FarmGame(17, first), new FarmGame(17, first), new FarmGame(17, second) };
            Parallel.For(0, games.Length, index =>
            {
                for (int i = 0; i < 20; i++) games[index].Buy(new CommodityId(CropKind.Wheat, CommodityKind.Raw), 0);
            });
            foreach (var game in games) game.Dispose();
            Require(first.Health.Health == LoggingHealth.Healthy && second.Health.Health == LoggingHealth.Healthy, "共享输出故障");
        }
        string[] runtime = ReadFiles(Path.Combine(directory, "runtime"));
        string[] debug = ReadFiles(Path.Combine(directory, "debug"));
        Require(runtime.Length == 193 && debug.Length == runtime.Length &&
            runtime.Count(line => HasEvent(line, "ProductionSummary")) == 3, "File 共享丢失/重复事件");
        Require(runtime.OrderBy(line => line).SequenceEqual(debug.OrderBy(line => line)), "两目标不是同一事件头/时间/内容");
        Require(runtime.All(line => !line.Contains("EventState:") && !line.Contains("OriginalFormat")) &&
            runtime.Where(line => HasEvent(line, "SessionStarted")).All(line =>
                line.Contains("GameVersion: null") && line.Contains("WindowSize: null")), "File provider 输出了伪字段或假环境值");
        AssertSequence(runtime);
        AssertSequence(debug);
        Require(runtime.Select(line => Field(line, "SessionId")).Distinct().Count() == 2, "共享文件混淆会话");
        foreach (string path in Directory.GetFiles(Path.Combine(directory, "runtime"), "*.log"))
        {
            using var released = new FileStream(path, FileMode.Open, System.IO.FileAccess.ReadWrite, FileShare.None);
            Require(released.Length > 0, "正常关闭未释放文件");
        }
    }

    private static void CheckRetention(string directory)
    {
        var retention = new LogFileRetention(1024, 3, 1024, 2);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            using var log = RuntimeLog.OpenFile(directory, false, new LogEnvironment(BuildKind: "Release"), retention);
            using var game = new FarmGame(17, log);
            for (int i = 0; i < 30; i++) game.Buy(new CommodityId(CropKind.Wheat, CommodityKind.Raw), -1);
            Require(log.Health.Health == LoggingHealth.Healthy, "小阈值滚动故障");
        }
        Require(Directory.GetFiles(Path.Combine(directory, "runtime"), "*.log").Length == 3, "跨关闭重开保留预算未生效");
        Require(!Directory.Exists(Path.Combine(directory, "debug")), "发布构建开启debug目标");
        string[] lines = ReadFiles(Path.Combine(directory, "runtime"));
        Require(lines.Length > 0 && lines.Last().Contains("EventName=SessionEnded"), "滚动截断正常末条");
        AssertSequence(lines);
    }

    private static void CheckDescriptorRouting(string directory)
    {
        var included = new LogEventDescriptor(9001, "TestRuntimeObservation", "Tests.Logging");
        var diagnostic = new LogEventDescriptor(9002, "TestDiagnosticObservation", "Tests.Logging", RuntimeIncluded: false);
        var verbose = included with { Number = 9003, Name = "TestVerboseObservation", Level = Microsoft.Extensions.Logging.LogLevel.Trace };
        using (var log = LogOutput.OpenFile(directory, true))
        {
            log.Submit(included, "两目标事件", new());
            log.Submit(diagnostic, "仅开发事件", new());
            log.Submit(verbose, "详细事件", new());
            Require(log.Health.Health == LoggingHealth.Healthy, "领域描述导致输出故障");
        }
        string[] runtime = ReadFiles(Path.Combine(directory, "runtime"));
        string[] debug = ReadFiles(Path.Combine(directory, "debug"));
        Require(runtime.Length == 1 && HasEvent(runtime[0], included.Name) && debug.Length == 3 &&
            runtime[0] == debug[0] && HasEvent(debug[1], diagnostic.Name) && HasEvent(debug[2], verbose.Name),
            "事件采集资格和级别没有独立控制真实 File 路由");
        string releaseDirectory = Path.Combine(directory, "release");
        using (var log = LogOutput.OpenFile(releaseDirectory, false))
        {
            log.Submit(diagnostic, "禁止发布事件", new());
            log.Submit(verbose, "禁止发布详细事件", new());
            log.Submit(included, "发布事件", new());
        }
        string[] released = ReadFiles(Path.Combine(releaseDirectory, "runtime"));
        Require(released.Length == 1 && Field(released[0], "Sequence") == "1" &&
            !Directory.Exists(Path.Combine(releaseDirectory, "debug")), "发布过滤前分配序号或创建开发文件");
    }

    private static void CheckUnavailable(string directory)
    {
        File.WriteAllText(directory, "不可作为目录");
        int diagnostics = 0;
        using var log = RuntimeLog.OpenFile(directory, true, new LogEnvironment(), diagnostic: _ => diagnostics++);
        using var game = new FarmGame(17, log);
        Require(game.Buy(new CommodityId(CropKind.Wheat, CommodityKind.Raw), 1).Success && game.GetRawStock(CropKind.Wheat) == 1,
            "不可写目录阻止真实交易");
        Require(log.Health.Health == LoggingHealth.Unavailable && diagnostics > 0 && File.ReadAllText(directory) == "不可作为目录",
            "没有独立诊断或擅自改了路径");
    }

    private static void CheckRuntimeFileFailure(string directory)
    {
        int diagnostics = 0;
        using var log = RuntimeLog.OpenFile(directory, true, new LogEnvironment(),
            new LogFileRetention(128, 10, 128, 10), diagnostic: _ => diagnostics++);
        Require(log.Health.Health == LoggingHealth.Healthy, "运行中失效夹具初始文件不健康");
        string runtimeDirectory = Path.Combine(directory, "runtime");
        string current = Directory.GetFiles(runtimeDirectory, "*.log").Single();
        string prefix = Path.GetFileNameWithoutExtension(current);
        for (int sequence = 1; sequence <= 3; sequence++)
            Directory.CreateDirectory(Path.Combine(runtimeDirectory, prefix + "_" + sequence.ToString("000") + ".log"));
        log.InitializationFailed(new InvalidOperationException("真实滚动文件目标在运行中失效"));
        long failures = log.Health.FailureCount;
        Require(log.Health.Health == LoggingHealth.Degraded && failures > 0 && diagnostics == 1,
            "runtime 运行中写入/滚动失败未独立报告，或假称两目标健康");
        log.Output.Submit(new(9002, "TestDiagnosticObservation", "Tests.Logging", RuntimeIncluded: false),
            "仅可写开发目标的事件", new());
        Require(log.Health.Health == LoggingHealth.Degraded, "被过滤的目标被误判为恢复");
        log.InitializationFailed(new InvalidOperationException("失效目标的后续真实输出"));
        Require(log.Health.Health == LoggingHealth.Degraded && log.Health.FailureCount > failures && diagnostics == 1,
            "失败目标内部通知未接通、误判恢复或重复无界通知");
        log.Dispose();
        string[] debug = ReadFiles(Path.Combine(directory, "debug"));
        Require(debug.Any(line => HasEvent(line, "FatalException")) && debug.Any(line => HasEvent(line, "LoggingHealthChanged")),
            "仍可写目标丢失实际异常或健康变化");
    }

    private static void CheckIndependentInitialization(string directory)
    {
        CheckConfigurationFailure(Path.Combine(directory, "debug-failed"),
            new LogFileRetention(DebugBytes: 0), "runtime", LoggingHealth.Degraded, 1);
        CheckConfigurationFailure(Path.Combine(directory, "runtime-failed"),
            new LogFileRetention(RuntimeBytes: 0), "debug", LoggingHealth.Degraded, 1);
        CheckConfigurationFailure(Path.Combine(directory, "both-failed"),
            new LogFileRetention(RuntimeBytes: 0, DebugBytes: 0), null, LoggingHealth.Unavailable, 2);
    }

    private static void CheckConfigurationFailure(string directory, LogFileRetention retention,
        string? survivingTarget, LoggingHealth expectedHealth, long expectedFailures)
    {
        int diagnostics = 0;
        int expectedMoney = 0;
        long expectedValue = 0;
        using (var log = RuntimeLog.OpenFile(directory, true, new LogEnvironment(), retention, _ => diagnostics++))
        {
            Require(log.Health.Health == expectedHealth && log.Health.FailureCount == expectedFailures,
                "独立目标配置故障未保留真实健康或关闭了可用目标");
            using (var game = new FarmGame(17, log))
            {
                var result = game.Buy(new CommodityId(CropKind.Wheat, CommodityKind.Raw), 1);
                Require(result.Success && game.GetRawStock(CropKind.Wheat) == 1, "目标初始化故障阻止了真实买入");
                expectedMoney = game.MoneyCents;
                expectedValue = result.TotalCents;
                game.Buy(new CommodityId(CropKind.Wheat, CommodityKind.Raw), 0);
            }
            Require(log.Health.Health == expectedHealth && log.Health.FailureCount == expectedFailures && diagnostics == 1,
                "未配置目标被输出恢复判定误当健康，或故障通知没有限频");
        }
        Require(diagnostics == 1, "正常关闭添加了错误的恢复通知");
        if (survivingTarget == null)
        {
            Require(!Directory.Exists(Path.Combine(directory, "runtime")) && !Directory.Exists(Path.Combine(directory, "debug")),
                "两个配置均拒绝仍创建了输出或备用路径");
            return;
        }
        string survivingDirectory = Path.Combine(directory, survivingTarget);
        string[] lines = ReadFiles(survivingDirectory);
        foreach (string eventName in new[] { "SessionStarted", "GameInitialized", "GameEnded", "SessionEnded", "LoggingHealthChanged" })
            Require(lines.Any(line => HasEvent(line, eventName)), "可用目标缺少真实生命周期或健康事件 " + eventName);
        string trade = lines.Single(line => HasEvent(line, "TradeFinished") && Field(line, "RequestedQuantity") == "1");
        Require(trade.Contains("StockBefore: 0") && trade.Contains("StockAfter: 1") &&
            trade.Contains("MoneyBeforeCents: 5000") && trade.Contains("MoneyAfterCents: " + expectedMoney) &&
            trade.Contains("ValueCents: " + expectedValue), "可用目标没有记录真实买入结算");
        Require(lines.Where(line => HasEvent(line, "LoggingHealthChanged")).All(line => Field(line, "LoggingHealth") == "Degraded"),
            "配置失败目标被错误报告为已恢复");
        AssertSequence(lines);
        foreach (string path in Directory.GetFiles(survivingDirectory, "*.log"))
        {
            using var released = new FileStream(path, FileMode.Open, System.IO.FileAccess.ReadWrite, FileShare.None);
            Require(released.Length > 0, "关闭未释放可用目标的成熟 File sink");
        }
    }

    private static string[] ReadFiles(string directory) => Directory.GetFiles(directory, "*.log").OrderBy(path => path, StringComparer.Ordinal)
        .SelectMany(path => Lines(File.ReadAllText(path))).ToArray();
}
