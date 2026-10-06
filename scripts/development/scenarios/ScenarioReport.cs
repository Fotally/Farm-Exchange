using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using FarmExchange.Gameplay;
using FarmExchange.Market;
using FarmExchange.Time;
using FarmExchange.Trading;

namespace FarmExchange.Development;

public enum ScenarioOutcome
{
    Running, Passed, CompletedWithInsufficientEvidence, PreconditionsRejected, OperationRejected,
    WaitLimitExceeded, TimeRangeExhausted, CheckFailed, Aborted,
}
public enum ScenarioCheckStatus { NotExecuted, Passed, Failed, InsufficientEvidence }
public enum ScenarioStage { Preparing, ObservingProcessing, WaitingForProducts, WaitingForOrder, Finished }

/**
 * <summary>固定流程的配置引用、实际操作、稳定检查点与检查结果。</summary>
 */
public sealed class ScenarioReport
{
    public int SchemaVersion => 1;
    public string Flow => "buy-process-sell";
    public int FlowVersion => 1;
    public string RunId { get; internal set; } = "";
    public required ScenarioConfigurationReference Configuration { get; init; }
    public string ConfigurationValidation => "Passed";
    public required string Target { get; init; }
    public int? ActualSeed { get; init; }
    public ScenarioOutcome Outcome { get; internal set; } = ScenarioOutcome.Running;
    public string? Reason { get; internal set; }
    public ScenarioStage LastStage { get; internal set; }
    public uint AdvancedTicks { get; internal set; }
    public uint ProcessingWaitTicks { get; internal set; }
    public uint OrderWaitTicks { get; internal set; }
    public long Produced { get; internal set; }
    public long Harvested { get; internal set; }
    public int? OrderId { get; internal set; }
    public uint? FillFirstObservedTicks { get; internal set; }
    public ScenarioSnapshot? Baseline { get; internal set; }
    public ScenarioSnapshot? Final { get; internal set; }
    public List<ScenarioOperation> Operations { get; } = new();
    public List<ScenarioPreparation> Initialization { get; } = new();
    public List<ScenarioInterval> Intervals { get; } = new();
    public List<ScenarioTimeObservation> TimeObservations { get; } = new();
    public List<ScenarioCheck> Checks { get; } = new();

    /**
     * <summary>按仓库或导出包位置解析唯一报告根目录。</summary>
     * <param name="projectRoot">非导出运行的项目根目录。</param>
     * <param name="executablePath">导出程序可执行文件绝对路径。</param>
     * <param name="exported">是否为dev导出包运行。</param>
     * <returns>绝对目录；macOS写在.app父目录。</returns>
     */
    public static string ResolveRoot(string projectRoot, string executablePath, bool exported)
    {
        if (!exported)
            return Path.Combine(Path.GetFullPath(projectRoot), "build", "test-runs");
        string executable = Path.GetFullPath(executablePath);
        string directory = Path.GetDirectoryName(executable) ?? throw new IOException("无法确定导出程序目录");
        for (DirectoryInfo? current = new(directory); current != null; current = current.Parent)
            if (current.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                return Path.Combine(current.Parent?.FullName ?? throw new IOException("无法确定.app父目录"), "reports", "test-runs");
        return Path.Combine(directory, "reports", "test-runs");
    }

    /**
     * <summary>运行前创建唯一目录并验证可写；失败不更换输出位置。</summary>
     * <param name="root">已选报告根目录。</param>
     * <returns>仅供本次运行的绝对目录。</returns>
     */
    public static string CreateRunDirectory(string root)
    {
        string directory = Path.Combine(Path.GetFullPath(root), DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        ProbeWritable(directory);
        return directory;
    }

    /**
     * <summary>验证指定运行目录可写，删除临时探针；I/O失败直接报告调用方。</summary>
     * <param name="directory">本次唯一运行目录。</param>
     */
    public static void ProbeWritable(string directory)
    {
        string probe = Path.Combine(directory, ".write-probe-" + Guid.NewGuid().ToString("N"));
        using (var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            stream.Flush();
        File.Delete(probe);
    }

    internal string Write(string directory)
    {
        RunId = Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)));
        string destination = Path.Combine(directory, "report.json");
        var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new JsonStringEnumConverter());
        string json = JsonSerializer.Serialize(this, options);
        using var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
        writer.Write(json);
        return Path.GetFullPath(destination);
    }
}

public sealed record ScenarioConfigurationReference(string Path, string CaseId, int Revision, string Sha256);
public sealed record ScenarioCheck(string Name, ScenarioCheckStatus Status, string Evidence);
public sealed record ScenarioOperation(string Name, uint ElapsedTicks, object Request, object Result, ScenarioSnapshot Before, ScenarioSnapshot After);
public sealed record ScenarioPreparation(string Name, object Request, object Result);
public sealed record ScenarioInterval(uint StartTicks, uint EndTicks, uint AdvancedTicks, bool IsEvent, TickResult Result);
public sealed record ScenarioTimeObservation(uint ElapsedTicks, double RequestedRate, double ActualRate, string Source, bool IsPaused);
public sealed record ScenarioStock(int Total, int Available, int Frozen);
public sealed record ScenarioProcessor(int X, int Y, BuildingKind Building, CropKind Crop, int RemainingSeconds, ProcessorStatus? Status);
public sealed record ScenarioSnapshot(CalendarSnapshot Calendar, int BalanceCents, int AvailableCents, int FrozenCents,
    ScenarioStock Raw, ScenarioStock Product, int RawReserve, ScenarioProcessor Processor,
    int BuildingCount, MarketQuoteSnapshot RawQuote, MarketQuoteSnapshot ProductQuote, IReadOnlyList<TradeOrderSnapshot> Orders);
