using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
using FarmExchange.Logging;
using FarmExchange.Time;
#if DEBUG
using FarmExchange.Development;
#endif

public partial class TestScenarioLogging : Node
{
    public override void _Ready() => GetTree().Quit(RunChecks() ? 0 : 1);

    public static bool RunChecks()
    {
#if DEBUG
        string directory = Path.Combine(Path.GetTempPath(), "farm-scenario-logging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            CheckIdentityAndReports(directory);
            CheckOutcomes(directory);
            CheckCurrentAndSameRate(directory);
            CheckIsolation(directory);
            CheckAdapterAndInvalidPurpose();
            CheckFiles(directory);
            return true;
        }
        catch (Exception error) { GD.PrintErr("流程日志检查失败：" + error); return false; }
        finally
        {
            if (Path.GetFullPath(directory).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
                Directory.Delete(directory, true);
        }
#else
        return true;
#endif
    }

#if DEBUG
    private static ScenarioConfiguration Load(string directory, string target = "independent", int quantity = 1,
        int limit = 27, string date = "01-02-01", int anchor = 0)
    {
        string file = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(file, "{\"schemaVersion\":1,\"caseId\":\"logging\",\"revision\":7,\"flow\":\"buy-process-sell\"," +
            "\"run\":{\"target\":\"" + target + "\"" + (target == "independent" ? ",\"seed\":12345" : "") + "}," +
            "\"execution\":{\"timePlan\":[{\"endDate\":\"" + date + "\",\"rate\":1}]}," +
            "\"parameters\":{\"rawCommodity\":\"Radish.Raw\",\"quantity\":" + quantity +
            ",\"processorAnchor\":{\"x\":" + anchor + ",\"y\":0},\"processingWaitLimitTicks\":" + limit + ",\"orderWaitLimitTicks\":1}}");
        return ScenarioConfiguration.Load(file);
    }

    private static BuyProcessSellScenario Start(string directory, ScenarioConfiguration config, RuntimeLog? log,
        FarmGame? current = null) => BuyProcessSellScenario.Start(config,
            ScenarioReport.CreateRunDirectory(Path.Combine(directory, "runs")), current, logging: log);

    private static void Advance(BuyProcessSellScenario scenario)
    {
        var driver = new SimulationDriver(scenario.Game.Log?.Time);
        driver.SetDevelopmentRate(16, SimulationRateSource.Scenario);
        driver.Advance(1000, scenario.Game, scenario.ObserveCheckpoint, scenario.GetMaxAdvanceTicks);
    }

    private static FarmGame Current(RuntimeLog log, uint seconds = 0)
    {
        var game = new FarmGame(12345, seconds, log);
        foreach (var space in game.GetBuildingSpaces()) game.RemoveBuilding(space.AnchorCell);
        Require(game.BuildProcessor(Vector2I.Zero, CropKind.Radish) == null, "现场场地准备失败");
        return game;
    }

    private static void CheckIdentityAndReports(string directory)
    {
        using var text = new StringWriter();
        using var log = RuntimeLog.Capture(text);
        using var main = new FarmGame(12345, log);
        ScenarioConfiguration config = Load(directory);
        string hash = config.Sha256;
        File.WriteAllText(config.SourcePath, "之后的外部修改，不是本次配置");
        try
        {
            BuyProcessSellScenario.Start(config, Path.Combine(directory, "not-created"), logging: log);
            throw new Exception("没有预建目录仍启动了流程");
        }
        catch (DirectoryNotFoundException) { }
        Require(Events(text, "ScenarioStarted").Length == 0 && Events(text, "GameInitialized").Length == 1,
            "目录准备失败后创建流程/独立局");
        string runDirectory = ScenarioReport.CreateRunDirectory(Path.Combine(directory, "runs"));
        var scenario = BuyProcessSellScenario.Start(config, runDirectory, logging: log);
        using var game = scenario.Game;
        string runId = Path.GetFileName(runDirectory);
        Require(scenario.Report.RunId == runId && !File.Exists(Path.Combine(runDirectory, "report.json")), "RunId未在命令前固定");
        string started = Events(text, "ScenarioStarted").Single();
        string[] initialized = Events(text, "GameInitialized");
        Require(initialized.Length == 2 && initialized[0].Contains("GamePurpose: \"Main\"") &&
            initialized[1].Contains("GamePurpose: \"ScenarioIndependent\"") &&
            Field(initialized[0], "GameInstanceId") != Field(initialized[1], "GameInstanceId") &&
            Field(initialized[1], "GameInstanceId") == Field(started, "GameInstanceId") &&
            Header(initialized[0], "SessionId") == Header(started, "SessionId"), "独立局身份/用途/会话关联错误");
        Require(started.Contains("ConfigurationRevision: 7") && started.Contains("ConfigurationHash: \"" + hash + "\"") &&
            started.Contains("RunId: \"" + runId + "\"") && started.Contains("RunMode: \"Independent\""), "开始未使用实际加载凭据");
        Require(Sequence(started) < Sequence(Events(text, "CommandReceived").First()), "首个流程命令早于开始事件");
        Require(Lines(text).Where(line => !line.Contains("EventName=Scenario")).All(line => !line.Contains("RunId:")),
            "把流程RunId隐式附加到其他经营事件");
        try { scenario.WriteReport(); throw new Exception("运行中报告被保存"); }
        catch (InvalidOperationException) { }
        Require(Events(text, "ScenarioReportSaveFailed").Length == 0, "运行中调用错误被当作文件保存失败");
        Advance(scenario);
        AssertOutcome(text, scenario, ScenarioOutcome.Passed);
        ScenarioSnapshot final = scenario.Report.Final!;
        string file = scenario.WriteReport();
        using (JsonDocument json = JsonDocument.Parse(File.ReadAllText(file)))
        {
            Require(json.RootElement.GetProperty("runId").GetString() == runId &&
                json.RootElement.GetProperty("configuration").GetProperty("sha256").GetString() == hash &&
                json.RootElement.GetProperty("configuration").GetProperty("revision").GetInt32() == 7,
                "报告与日志配置/运行身份不一致");
        }
        string saved = Events(text, "ScenarioReportSaved").Single();
        Require(saved.Contains("ReportFile: \"" + runId + "/report.json\"") && !saved.Contains(directory), "成功路径不是实际相对文件");
        try { scenario.WriteReport(); throw new Exception("重复文件写入没有拒绝"); }
        catch (IOException) { }
        Require(Events(text, "ScenarioReportSaveFailed").Length == 1 &&
            Events(text, "ScenarioReportSaveFailed")[0].Contains("ExceptionType: \"System.IO.IOException\"") &&
            scenario.Outcome == ScenarioOutcome.Passed && ReferenceEquals(final, scenario.Report.Final), "保存失败覆盖终结结果或重读现场");
        scenario.Abort("不得覆盖已完成结果");
        Require(Events(text, "ScenarioFinished").Length == 1, "终结后中止重复记事件");
        game.Dispose();
        Require(Events(text, "GameEnded").Length == 1 && Sequence(saved) < Sequence(Events(text, "GameEnded")[0]),
            "独立局在报告事件前关闭或同时关闭主局");
        main.AdvanceTick();
        Require(main.Calendar.ElapsedSeconds == 1, "独立局释放影响主局推进");
    }

    private static void CheckOutcomes(string directory)
    {
        using var text = new StringWriter();
        using var log = RuntimeLog.Capture(text);
        foreach (var item in new[]
        {
            (Load(directory, anchor: 383), ScenarioOutcome.PreconditionsRejected),
            (Load(directory, quantity: 1000), ScenarioOutcome.OperationRejected),
            (Load(directory, limit: 26), ScenarioOutcome.WaitLimitExceeded),
        })
        {
            var scenario = Start(directory, item.Item1, log);
            using var game = scenario.Game;
            Advance(scenario);
            AssertOutcome(text, scenario, item.Item2);
        }
        using (var game = Current(log, 25))
        {
            var scenario = Start(directory, Load(directory, "current", date: "01-01-02"), log, game);
            Advance(scenario);
            AssertOutcome(text, scenario, ScenarioOutcome.TimeRangeExhausted);
        }
        var failed = Start(directory, Load(directory), log);
        using (failed.Game)
        {
            Require(failed.Game.Buy(new(CropKind.Radish, CommodityKind.Product), 1).Success, "检查失败夹具买入失败");
            Advance(failed);
            AssertOutcome(text, failed, ScenarioOutcome.CheckFailed);
        }
        var aborted = Start(directory, Load(directory), log);
        using (aborted.Game)
        {
            aborted.Abort("首次实际中断");
            aborted.Abort("重复中断");
            AssertOutcome(text, aborted, ScenarioOutcome.Aborted);
            Require(Events(text, "ScenarioFinished").Last().Contains("FinishReason: \"首次实际中断\""), "重复中断覆盖首次原因");
        }
    }

    private static void CheckCurrentAndSameRate(string directory)
    {
        using var text = new StringWriter();
        using var log = RuntimeLog.Capture(text);
        using var game = Current(log);
        var scenario = Start(directory, Load(directory, "current"), log, game);
        Advance(scenario);
        AssertOutcome(text, scenario, ScenarioOutcome.CompletedWithInsufficientEvidence);
        scenario.WriteReport();
        Require(Events(text, "GameEnded").Length == 0, "原地流程完成释放了主局");
        game.AdvanceTick();
        Require(game.Calendar.ElapsedSeconds == 29, "原地完成后主局未继续推进");
        var interrupted = Start(directory, Load(directory, "current"), log, game);
        var driver = new SimulationDriver(game.Log!.Time);
        driver.RateChanged += (_, source) =>
        {
            if (source == SimulationRateSource.Player) interrupted.Abort("玩家主动改速，中断自动流程");
        };
        game.SetPaused(true);
        driver.SetRate(1);
        AssertOutcome(text, interrupted, ScenarioOutcome.Aborted);
        string selected = Events(text, "SimulationRateSelected").Last();
        string finished = Events(text, "ScenarioFinished").Last();
        Require(selected.Contains("ValueChanged: false") && Sequence(selected) < Sequence(finished) && game.IsPaused,
            "同值选择遗漏/晚于中断或改变暂停");
        interrupted.WriteReport();
        Require(Events(text, "GameEnded").Length == 0, "原地中断释放了主局");
    }

    private static void CheckIsolation(string directory)
    {
        using var text = new StringWriter();
        using var log = RuntimeLog.Capture(text);
        using var disabled = RuntimeLog.Disabled();
        using var brokenWriter = new BrokenWriter();
        using var broken = RuntimeLog.Capture(brokenWriter, diagnostic: _ => { });
        ScenarioSnapshot? baseline = null;
        foreach (RuntimeLog? observer in new RuntimeLog?[] { log, disabled, broken, null })
        {
            var scenario = Start(directory, Load(directory), observer);
            using var game = scenario.Game;
            Advance(scenario);
            Require(scenario.Outcome == ScenarioOutcome.Passed, "采集开关/故障改变流程结果");
            ScenarioSnapshot final = scenario.Report.Final!;
            baseline ??= final;
            Require(final.BalanceCents == baseline.BalanceCents && final.Calendar.ElapsedSeconds == baseline.Calendar.ElapsedSeconds &&
                final.Raw == baseline.Raw && final.Product == baseline.Product && scenario.Report.Produced == 1,
                "采集开关/故障改变经营资源或推进量");
        }
        Require(broken.Health.FailureCount > 0, "故障输出未观察到健康变化");
    }

    private static void CheckAdapterAndInvalidPurpose()
    {
        using var text = new StringWriter();
        using var log = RuntimeLog.Capture(text);
        foreach (RuntimeLog? observer in new RuntimeLog?[] { log, null })
        {
            try { using var invalid = new FarmGame(17, observer, (GamePurpose)99); throw new Exception("非法用途未拒绝"); }
            catch (ArgumentOutOfRangeException) { }
        }
        Require(Events(text, "GameInitialized").Length == 0, "非法用途创建了日志局");
        using var game = new FarmGame(17, log);
        var run = game.Log!.Scenario.Begin("adapter-run", "Current", 1, "hash")!;
        run.Finish("Aborted", null);
        run.Finish("Passed", "不得覆盖");
        Require(Events(text, "ScenarioFinished").Length == 1 && Events(text, "ScenarioFinished")[0].Contains("FinishReason: null"),
            "日志关联没有幂等终结或丢失null");
        game.Dispose();
        Require(game.Log.Scenario.Begin("closed", "Current", 1, "hash") == null, "已释放局仍开始流程观察");
    }

    private static void CheckFiles(string directory)
    {
        string output = ProjectSettings.GlobalizePath("res://build/logging-samples/issue-129-flow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        using (var log = RuntimeLog.OpenFile(output, true, new LogEnvironment()))
        {
            using var main = new FarmGame(12345, log);
            var passed = Start(output, Load(directory), log);
            using (passed.Game)
            {
                Advance(passed);
                passed.WriteReport();
                try { passed.WriteReport(); } catch (IOException) { }
            }
            var rejected = Start(output, Load(directory, quantity: 1000), log);
            using (rejected.Game) rejected.WriteReport();
            using var current = Current(log);
            var observed = Start(output, Load(directory, "current"), log, current);
            Advance(observed);
            observed.WriteReport();
            var interrupted = Start(output, Load(directory, "current"), log, current);
            var driver = new SimulationDriver(current.Log!.Time);
            driver.RateChanged += (_, _) => interrupted.Abort("玩家同值选择中断");
            driver.SetRate(1);
            interrupted.WriteReport();
        }
        string runtime = string.Join('\n', Directory.GetFiles(Path.Combine(output, "runtime"), "runtime*.log").Select(File.ReadAllText));
        string debug = string.Join('\n', Directory.GetFiles(Path.Combine(output, "debug"), "debug*.log").Select(File.ReadAllText));
        foreach (string name in new[] { "ScenarioStarted", "ScenarioFinished", "ScenarioReportSaved", "ScenarioReportSaveFailed", "PauseChanged", "SimulationRateSelected" })
            Require(runtime.Contains("EventName=" + name + " ") && debug.Contains("EventName=" + name + " "), "真实双文件缺少" + name);
        Require(runtime.Contains("GamePurpose: \"Main\"") && runtime.Contains("GamePurpose: \"ScenarioIndependent\"") &&
            runtime.Contains("ScenarioOutcome: \"Passed\"") && runtime.Contains("ScenarioOutcome: \"OperationRejected\"") &&
            runtime.Contains("ScenarioOutcome: \"CompletedWithInsufficientEvidence\"") && runtime.Contains("ScenarioOutcome: \"Aborted\""),
            "真实文件缺少独立身份/结果区别");
        GD.Print("#129 时间/流程日志双文件样例：" + output);
    }

    private static void AssertOutcome(StringWriter text, BuyProcessSellScenario scenario, ScenarioOutcome expected)
    {
        string[] finished = Events(text, "ScenarioFinished").Where(line => line.Contains("RunId: \"" + scenario.Report.RunId + "\"")).ToArray();
        Require(scenario.Outcome == expected && finished.Length == 1 && finished[0].Contains("ScenarioOutcome: \"" + expected + "\""),
            "真实流程结果与一次终结日志不一致：" + expected + " / " + scenario.Outcome);
    }

    private sealed class BrokenWriter : StringWriter
    {
        public override void WriteLine(string? value) => throw new IOException("流程测试输出故障");
    }

    private static string[] Lines(StringWriter text) => text.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
    private static string[] Events(StringWriter text, string name) => Lines(text).Where(line => line.Contains("EventName=" + name + " ")).ToArray();
    private static long Sequence(string line) => long.Parse(Header(line, "Sequence"));
    private static string Header(string line, string name) => line.Split(name + "=", 2)[1].Split(' ', 2)[0];
    private static string Field(string line, string name) => line.Split(name + ": \"", 2)[1].Split('"', 2)[0];
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
#endif
}
