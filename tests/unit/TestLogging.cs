using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
using FarmExchange.Logging;
using FarmExchange.Trading;

public static class TestLogging
{
    private static readonly CommodityId Wheat = new(CropKind.Wheat, CommodityKind.Raw);

    public static bool RunChecks()
    {
        try
        {
            CheckLifecycleAndTrades();
            CheckDisabledParity();
            CheckSourceValidation();
            CheckPublicObservation();
            CheckExecutedPriceFacts();
            CheckFailureAndRecovery();
            CheckTextAndExceptionBudgets();
            CheckProviderEnvironment();
            CheckGenericSubmission();
            return true;
        }
        catch (Exception error) { GD.PrintErr("日志公开接口测试失败：" + error); return false; }
    }

    private static void CheckLifecycleAndTrades()
    {
        using var output = new StringWriter();
        var log = RuntimeLog.Capture(output);
        var game = new FarmGame(17, log);
        var second = new FarmGame(17, log);
        int price = game.GetQuote(Wheat).PriceCents;
        Require(game.Buy(Wheat, 2).Success, "真实买入失败");
        Require(game.Buy(Wheat, -3).Failure == FarmExchange.Trading.TradeFailure.InvalidQuantity, "负量未正常拒绝");
        Require(!game.Buy(new CommodityId((CropKind)123, (CommodityKind)456), 1).Success, "非法枚举未拒绝");
        Require(!game.Buy(Wheat, int.MaxValue).Success, "不足金币未拒绝");
        Require(game.MoneyCents == 5000 - price * 2 && game.GetStock(Wheat) == 2, "拒绝改变了资源");
        second.Buy(Wheat, 1, CommandOrigin.Scenario);
        game.Dispose();
        game.Dispose();
        int releasedLength = output.ToString().Length;
        game.Buy(Wheat, 0);
        Require(output.ToString().Length == releasedLength, "已释放的局仍输出经营事件");
        log.Dispose();
        log.Dispose();
        second.Dispose();
        string[] lines = Lines(output.ToString());
        Require(lines.Length == 25, "生命周期或每笔买入的三条事件数量错误");
        Require(lines.Count(line => HasEvent(line, "SessionStarted")) == 1 &&
            lines.Count(line => HasEvent(line, "SessionEnded")) == 1 &&
            lines.Count(line => HasEvent(line, "GameInitialized")) == 2 &&
            lines.Count(line => HasEvent(line, "GameEnded")) == 2 &&
            lines.Count(line => HasEvent(line, "ProductionSummary")) == 2 &&
            lines.Count(line => HasEvent(line, "PerformanceSummary")) == 2, "关闭不是幂等或局未自动结束");
        string[] initializations = lines.Where(line => HasEvent(line, "GameInitialized")).ToArray();
        foreach (string initialized in initializations)
        {
            string id = Field(initialized, "GameInstanceId");
            string summary = lines.Single(line => HasEvent(line, "PerformanceSummary") && Field(line, "GameInstanceId") == id);
            Require(summary.Contains("SummaryScope: \"GameAdvance\"") && summary.Contains("BatchCount: 0") &&
                summary.Contains("IsPartialWindow: true") && summary.Contains("BatchMaxMs: null"), "未推进局的性能尾段不正确");
            Require(Array.IndexOf(lines, summary) < Array.FindIndex(lines, line => HasEvent(line, "GameEnded") && Field(line, "GameInstanceId") == id), "性能尾段晚于局结束");
        }

        Require(Field(initializations[0], "GameInstanceId") != Field(initializations[1], "GameInstanceId"), "两局身份重复");
        Require(initializations[0].Contains("Seed: 17") && initializations[0].Contains("MoneyCents: 5000") &&
            initializations[0].Contains("InitialFacilities: [") && initializations[0].Contains("Wheat.Raw:") &&
            initializations[0].Contains("GameDate: { Year: 1, Month: 1, Day: 1 }") &&
            initializations[0].Contains("SourceContext=FarmExchange.Gameplay.FarmGame"), "初始化基线、来源或日期投影错误");
        Require(!initializations[0].Contains("ElapsedDays:") && !initializations[0].Contains("DayOfWeek:"), "输出了未登记日期成员");
        string success = lines.Single(line => HasEvent(line, "TradeFinished") && Field(line, "RequestedQuantity") == "2");
        Require(success.Contains("StockBefore: 0") && success.Contains("StockAfter: 2") &&
            success.Contains("MoneyBeforeCents: 5000") && success.Contains("MoneyAfterCents: " + (5000 - price * 2)) &&
            success.Contains("FeeCents: 0") && success.Contains("RejectionReason: null"), "结算前后不对应真实资源");
        string invalid = lines.Single(line => HasEvent(line, "TradeFinished") && line.Contains("Commodity: \"123.456\""));
        Require(invalid.Contains("StockBefore: null") && !invalid.Contains("UnitPriceCents:"), "非法商品伪造库存或价格");
        foreach (string line in lines.Where(line => HasEvent(line, "CommandFinished")))
            Require(!line.Contains("MoneyBeforeCents:") && !line.Contains("ValueCents:"), "通用结束重复记录收支");
        Require(lines.Where(line => line.Contains("CommandOrigin: \"Scenario\"")).Count() == 3, "开发来源未贯通三个事件");
        foreach (var command in lines.Where(line => line.Contains("CommandId:")).GroupBy(line =>
            Field(line, "GameInstanceId") + ":" + Field(line, "CommandId")))
            Require(command.Count() == 3 && HasEvent(command.First(), "CommandReceived") &&
                HasEvent(command.Last(), "CommandFinished"), "同秒多命令或两局同号命令关联错误");
        Require(Field(lines.Last(line => HasEvent(line, "GameEnded")), "EndReason") == "Shutdown", "会话关闭未自动结束剩余局");
        AssertSequence(lines);
        Require(log.Health.Health == LoggingHealth.Healthy, "正常采集被判故障");
    }

    private static void CheckDisabledParity()
    {
        using var disabled = RuntimeLog.Disabled();
        using var memory = new StringWriter();
        using var log = RuntimeLog.Capture(memory);
        using var plain = new FarmGame(41, disabled);
        using var traced = new FarmGame(41, log);
        foreach (int quantity in new[] { 3, -1, 2, int.MaxValue })
            Require(plain.Buy(Wheat, quantity) == traced.Buy(Wheat, quantity), "开关日志改变交易结果");
        Require(plain.AdvanceTicks(120) == traced.AdvanceTicks(120), "开关日志改变经营推进");
        Require(plain.MoneyCents == traced.MoneyCents && plain.GetStock(Wheat) == traced.GetStock(Wheat), "开关日志改变资源");
        Require(disabled.Health.FailureCount == 0, "关闭采集仍执行输出");
    }

    private static void CheckExecutedPriceFacts()
    {
        using var output = new StringWriter();
        using var log = RuntimeLog.Capture(output);
        using var game = new FarmGame(17, log);
        int actualPrice = game.GetQuote(Wheat).PriceCents;
        var negative = game.Buy(Wheat, -1);
        var zero = game.Buy(Wheat, 0);
        var insufficient = game.Buy(Wheat, int.MaxValue);
        var success = game.Buy(Wheat, 1);
        var capacity = game.Buy(Wheat, int.MaxValue);
        Require(negative.UnitPriceCents == null && zero.UnitPriceCents == null &&
            capacity.Failure == TradeFailure.InventoryCapacityExceeded && capacity.UnitPriceCents == null,
            "报价读取前拒绝结果伪造单价");
        Require(insufficient.Failure == TradeFailure.InsufficientFunds && insufficient.UnitPriceCents == actualPrice &&
            success.Success && success.UnitPriceCents == actualPrice, "结果没有携带执行实际读取单价");
        Require(game.MoneyCents == 5000 - actualPrice && game.GetStock(Wheat) == 1, "报价事实改变拒绝资源或金额");
        string[] trades = Lines(output.ToString()).Where(line => HasEvent(line, "TradeFinished")).ToArray();
        Require(trades.Length == 5 && !trades[0].Contains("UnitPriceCents:") && !trades[1].Contains("UnitPriceCents:") &&
            !trades[4].Contains("UnitPriceCents:"), "日志把可查询价冒充未执行的报价读取");
        Require(trades[2].Contains("UnitPriceCents: " + actualPrice) && trades[3].Contains("UnitPriceCents: " + actualPrice),
            "日志丢失真实成功或报价后拒绝的单价事实");
    }

    private static void CheckFailureAndRecovery()
    {
        using var writer = new SwitchableWriter();
        int diagnostics = 0;
        using var log = RuntimeLog.Capture(writer, diagnostic: _ => diagnostics++);
        using var game = new FarmGame(17, log);
        writer.ThrowWrites = true;
        int before = game.MoneyCents;
        var result = game.Buy(Wheat, 1);
        Require(result.Success && game.MoneyCents == before - result.TotalCents && game.GetStock(Wheat) == 1, "写入故障干扰业务");
        Require(log.Health.Health == LoggingHealth.Unavailable && log.Health.FailureCount > 0 &&
            log.Health.FirstFailureUptimeMs.HasValue && log.Health.KnownLostCount == null && diagnostics == 1, "独立健康诊断不明确");
        for (int i = 0; i < 30; i++) game.Buy(Wheat, 0);
        Require(log.Health.FailureCount == 93 && diagnostics == 1, "连续故障未完整累计或独立诊断无界");
        Require(writer.AttemptedWrites == 95, "稳定不可用仍重复尝试额外健康事件");
        writer.ThrowWrites = false;
        game.Buy(Wheat, 1);
        Require(log.Health.Health == LoggingHealth.Healthy && game.GetStock(Wheat) == 2, "恢复未观察或重试了业务");
        Require(writer.ToString().Contains("EventName=LoggingHealthChanged"), "缺少可输出的健康变化");
        Require(diagnostics == 2 && log.Health.FailureCount == 93, "恢复未仅通知一次或抑制被当作日志丢失");
        using var failing = new SwitchableWriter { ThrowWrites = true };
        using var isolated = RuntimeLog.Capture(failing, diagnostic: _ => throw new IOException("诊断目标故障"));
        using var isolatedGame = new FarmGame(17, isolated);
        Require(isolatedGame.Buy(Wheat, 1).Success, "独立诊断异常进入业务");
    }

    private static void CheckSourceValidation()
    {
        using var output = new StringWriter();
        using var log = RuntimeLog.Capture(output);
        using var game = new FarmGame(17, log);
        int length = output.ToString().Length;
        try { game.Buy(Wheat, 1, (CommandOrigin)42); Require(false, "非法来源被默认转换或接受"); }
        catch (ArgumentOutOfRangeException error) { Require(error.ParamName == "origin", "来源错误没有明确参数"); }
        Require(game.MoneyCents == 5000 && game.GetStock(Wheat) == 0 && output.ToString().Length == length,
            "非法来源仍提交业务或无契约事件");
        Require(game.Buy(Wheat, 1).Success && Lines(output.ToString()).Last().Contains("CommandId: 1"),
            "非法来源影响有效输入的指令身份");
        using var plain = new FarmGame(17);
        try { plain.Buy(Wheat, 1, (CommandOrigin)42); Require(false, "关闭日志后来源约束失效"); }
        catch (ArgumentOutOfRangeException) { }
        Require(plain.MoneyCents == 5000 && plain.GetStock(Wheat) == 0, "关闭采集仍提交非法来源业务");
    }

    private static void CheckPublicObservation()
    {
        using var output = new StringWriter();
        using var log = RuntimeLog.Capture(output);
        using var game = new FarmGame(17);
        using var context = log.BindGame(game, 17)!;
        var successful = context.Trading.BeginBuy(Wheat, 2, CommandOrigin.Scenario)!;
        var actual = game.Buy(Wheat, 2, CommandOrigin.Scenario);
        successful.Complete(actual);
        successful.Complete(actual);
        successful.Faulted(new InvalidOperationException("已经完成"));
        var faulted = context.Trading.BeginBuy(Wheat, 1)!;
        var original = new InvalidOperationException("公开观察原异常\r\n保留关联");
        Exception? received = null;
        try { ThrowObserved(faulted, original); }
        catch (InvalidOperationException error) { received = error; }
        Require(ReferenceEquals(received, original), "异常观察改变原异常引用或传播");
        faulted.Faulted(original);
        faulted.Complete(actual);
        string[] lines = Lines(output.ToString());
        string[] failedCommand = lines.Where(line => line.Contains("CommandId: 2")).ToArray();
        Require(failedCommand.Length == 3 && HasEvent(failedCommand[0], "CommandReceived") &&
            HasEvent(failedCommand[1], "BusinessException") && HasEvent(failedCommand[2], "CommandFinished") &&
            failedCommand[1].Contains("公开观察原异常\\r\\n保留关联") && failedCommand[2].Contains("CommandStatus: \"Faulted\""),
            "公开观察丢失异常关联或重复终结");
        Require(lines.Count(line => HasEvent(line, "TradeFinished")) == 1 && game.GetStock(Wheat) == 2,
            "异常观察伪造结算或执行业务");
        int beforeInvalid = output.ToString().Length;
        try { context.Trading.BeginBuy(Wheat, 1, (CommandOrigin)42); Require(false, "观察入口接受非法来源"); }
        catch (ArgumentOutOfRangeException) { }
        Require(output.ToString().Length == beforeInvalid, "观察入口输出非法来源事件");
        var unfinished = context.Trading.BeginBuy(Wheat, 1)!;
        context.Dispose();
        int released = output.ToString().Length;
        context.Dispose();
        unfinished.Complete(actual);
        Require(context.Trading.BeginBuy(Wheat, 1) == null && output.ToString().Length == released,
            "释放后的观察仍有结果或无法关闭");
        using var disabled = RuntimeLog.Disabled();
        Require(disabled.BindGame(game, 17) == null, "关闭采集仍绑定或投影局");
    }

    private static void ThrowObserved(TradeLogOperation observation, Exception original)
    {
        try { throw original; }
        catch (Exception error) { observation.Faulted(error); throw; }
    }

    private static void CheckTextAndExceptionBudgets()
    {
        using var output = new StringWriter();
        string hostile = "中文\\\"\r\n\t2026-10-07T12:00:00.000+08:00 [INF] EventName=Fake";
        using var log = RuntimeLog.Capture(output, new LogEnvironment(GameVersion: hostile,
            BuildId: string.Concat(Enumerable.Repeat("🌾", 800)), BuildKind: "Debug"));
        var lineLimited = new InvalidOperationException(string.Join("\n", Enumerable.Repeat("异常行", 100)));
        var byteLimited = new InvalidOperationException(new string('中', 10000));
        var emojiLimited = new InvalidOperationException(string.Join("\n", Enumerable.Repeat("🌾", 100)));
        log.InitializationFailed(new InvalidOperationException(hostile));
        log.InitializationFailed(lineLimited);
        log.InitializationFailed(byteLimited);
        log.InitializationFailed(new InvalidOperationException(new string('\0', 16000)));
        log.InitializationFailed(emojiLimited);
        string[] lines = Lines(output.ToString());
        Require(lines.Length == 6, "自由文本注入物理行或事件头");
        Require(lines[0].Contains("中文") && lines[0].Contains("\\r\\n\\t") &&
            lines[0].Contains("TruncatedFields: [\"BuildId\"]") && !lines[0].Contains("�"), "字符串转义或Unicode截断错误");
        Require(lines[0].Contains("TruncatedOriginalCounts: { BuildId: { ScalarCount: 800 } }"),
            "普通 emoji 字符串原数量缺失或错误按 UTF16 长度计数");
        string buildId = JsonSerializer.Deserialize<string>(Regex.Match(lines[0], @"BuildId: (""(?:\\.|[^""\\])*"")").Groups[1].Value)!;
        Require(buildId == string.Concat(Enumerable.Repeat("🌾", 512)), "普通字符串截断拆开 Unicode 标量");
        Require(lines[1].Contains("[FTL]") && lines[1].Contains("ExceptionType: \"System.InvalidOperationException\"") &&
            lines[1].Contains("Exception:") && lines[1].Contains("Phase: \"Initialization\""), "异常归属或级别错误");
        Require(lines[2].Contains("TruncatedFields: [\"Exception\"]") &&
            lines[3].Contains("TruncatedFields: [\"Exception\"]"), "异常行数/UTF8预算未标记");
        Require(lines[2].Contains("Exception: { ScalarCount: " + lineLimited.ToString().Length +
            ", Utf8Bytes: " + Encoding.UTF8.GetByteCount(lineLimited.ToString()) + ", LineCount: 100 }") &&
            lines[3].Contains("Exception: { ScalarCount: " + byteLimited.ToString().Length +
            ", Utf8Bytes: " + Encoding.UTF8.GetByteCount(byteLimited.ToString()) + ", LineCount: 1 }"),
            "行数/中文字节截断没有保留原 Unicode、UTF8 和行数计数");
        Require(lines[4].Contains("EventName=EventPayloadRejected") &&
            lines[4].Contains("OriginalEventName: \"FatalException\"") && !lines[4].Contains("Exception:"), "核心事件超限貌似完整");
        Require(lines[5].Contains("TruncatedFields: [\"Exception\"]") && !lines[5].Contains("�") &&
            lines[5].Contains("Exception: { ScalarCount: " + (emojiLimited.ToString().Length - 100) +
            ", Utf8Bytes: " + Encoding.UTF8.GetByteCount(emojiLimited.ToString()) + ", LineCount: 100 }"),
            "异常 emoji 截断混淆原标量数量、字节或行数");
        string emojiExcerpt = JsonSerializer.Deserialize<string>(Regex.Match(lines[5], @"Exception: (""(?:\\.|[^""\\])*"")").Groups[1].Value)!;
        Require(emojiExcerpt == string.Join("\n", emojiLimited.ToString().Split('\n').Take(64)),
            "异常截断未保留完整 emoji 或实际 64 行边界");
        using var plain = new StringWriter();
        using (var plainLog = RuntimeLog.Capture(plain, new LogEnvironment(Renderer: new string('田', 600)))) { }
        string[] plainLines = Lines(plain.ToString());
        Require(plainLines[0].Contains("TruncatedFields: [\"Renderer\"]") &&
            plainLines[0].Contains("TruncatedOriginalCounts: { Renderer: { ScalarCount: 600 } }") &&
            !plainLines[1].Contains("TruncatedOriginalCounts:"), "普通中文原数量缺失或未截断事件仍附截断元数据");
        string renderer = JsonSerializer.Deserialize<string>(Regex.Match(plainLines[0], @"Renderer: (""(?:\\.|[^""\\])*"")").Groups[1].Value)!;
        Require(renderer == new string('田', 512), "中文截断未保留完整多字节字符");
        AssertSequence(lines);
    }

    private static void CheckProviderEnvironment()
    {
        using var unavailable = new StringWriter();
        using (var log = RuntimeLog.Capture(unavailable, new LogEnvironment(GameVersion: "", WindowWidth: 0, WindowHeight: 0)))
            log.InitializationFailed(new InvalidOperationException("环境投影测试"));
        string[] unknown = Lines(unavailable.ToString());
        Require(unknown[0].Contains("GameVersion: null") && unknown[0].Contains("WindowSize: null"),
            "空版本或无窗口被伪造为已知环境值");
        Require(unknown.All(line => !line.Contains("EventState:") && !line.Contains("OriginalFormat")),
            "MEL 传输状态泄漏为未登记业务字段");
        using var available = new StringWriter();
        using (var log = RuntimeLog.Capture(available, new LogEnvironment(GameVersion: "v0.2.1", WindowWidth: 1920, WindowHeight: 1080)))
            Require(log.Health.Health == LoggingHealth.Healthy, "真实正尺寸环境触发故障");
        string known = Lines(available.ToString())[0];
        Require(known.Contains("GameVersion: \"v0.2.1\"") && known.Contains("WindowSize: { Width: 1920, Height: 1080 }"),
            "实际版本或正尺寸被错误归一化");
    }

    private static void CheckGenericSubmission()
    {
        using var output = new StringWriter();
        using var log = LogOutput.Capture(output, diagnostic: _ => { });
        var description = new LogEventDescriptor(9001, "TestObservation", "Tests.Logging",
            Microsoft.Extensions.Logging.LogLevel.Debug, RuntimeIncluded: false);
        log.Submit(description, "合法测试事件", new()
        {
            ["ObservedCount"] = 0,
            ["UnknownValue"] = null,
            ["NestedObservation"] = new System.Collections.Generic.Dictionary<string, object?> { ["Label"] = "中文" },
            ["SessionId"] = "不可覆盖会话",
            ["Sequence"] = -10,
            ["EventName"] = "不可覆盖事件描述",
        });
        string line = Lines(output.ToString()).Single();
        Require(HasEvent(line, "TestObservation") && line.Contains("[DBG]") &&
            line.Contains("SourceContext=Tests.Logging") && line.Contains("ObservedCount: 0") &&
            line.Contains("UnknownValue: null") && line.Contains("NestedObservation: { Label: \"中文\" }"),
            "合法独立事件不能通过通用提交输出");
        Require(Field(line, "Sequence") == "1" && !line.Contains("不可覆盖") &&
            !line.Contains("RuntimeIncluded") && !line.Contains("EventId:"), "公共头被覆盖或内部路由属性泄漏");
        log.Observe(() => throw new InvalidOperationException("测试领域投影失败"));
        Require(log.Health.FailureCount == 1, "通用观察未隔离领域投影失败");
        log.Submit(description with { Number = 9002, Name = "TestFollowup" }, "继续观察", new());
        Require(HasEvent(Lines(output.ToString()).Last(), "TestFollowup"), "投影失败阻止后续合法事件");
    }

    internal static string[] Lines(string text) => text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
    internal static bool HasEvent(string line, string name) => line.Contains(" EventName=" + name + " ");
    internal static string Field(string line, string name)
    {
        var match = Regex.Match(line, @"\b" + name + "(?:=|: )\"?(-?[A-Za-z0-9]+)");
        Require(match.Success, "缺少字段 " + name);
        return match.Groups[1].Value;
    }
    internal static void AssertSequence(string[] lines)
    {
        foreach (var session in lines.GroupBy(line => Field(line, "SessionId")))
        {
            long previous = 0;
            foreach (string line in session)
            {
                long current = long.Parse(Field(line, "Sequence"));
                Require(current > previous, "会话物理输出序号未递增");
                previous = current;
            }
        }
    }
    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private sealed class SwitchableWriter : StringWriter
    {
        internal bool ThrowWrites;
        internal int AttemptedWrites;
        public override void WriteLine(string? value)
        {
            AttemptedWrites++;
            if (ThrowWrites) throw new IOException("模拟不可写目标");
            base.WriteLine(value);
        }
    }
}
