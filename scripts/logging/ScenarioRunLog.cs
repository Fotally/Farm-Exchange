using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace FarmExchange.Logging;

/**
 * <summary>只保存一次流程的日志关联及终结去重，不拥有执行和报告写入。</summary>
 */
public sealed class ScenarioRunLog
{
    private const string Source = "FarmExchange.Development.BuyProcessSellScenario";
    private static readonly LogEventDescriptor StartedEvent = new(80, "ScenarioStarted", Source);
    private static readonly LogEventDescriptor FinishedEvent = new(81, "ScenarioFinished", Source);
    private static readonly LogEventDescriptor SavedEvent = new(82, "ScenarioReportSaved", Source);
    private static readonly LogEventDescriptor FailedEvent = new(83, "ScenarioReportSaveFailed", Source, LogLevel.Error);
    private readonly GameLog _context;
    private readonly string _runId;
    private readonly string _runMode;
    private bool _finished;

    internal ScenarioRunLog(GameLog context, string runId, string runMode)
    { _context = context; _runId = runId; _runMode = runMode; }

    internal void Started(int revision, string hash) => _context.Observe(() =>
    {
        var fields = Fields();
        fields["ConfigurationRevision"] = revision;
        fields["ConfigurationHash"] = hash;
        _context.Output.Submit(StartedEvent, "开发流程开始", fields);
    });

    /**
     * <summary>记录已完成的真实终结；重复通知不重复发出事件。</summary>
     * <param name="outcome">流程既有结果枚举的名字，不接收开发类型。</param>
     * <param name="reason">真实结束说明。</param>
     */
    public void Finish(string outcome, string? reason)
    {
        if (_finished) return;
        _finished = true;
        _context.Observe(() =>
        {
            var fields = Fields();
            fields["ScenarioOutcome"] = outcome;
            fields["FinishReason"] = reason;
            _context.Output.Submit(FinishedEvent, "开发流程结束", fields);
        });
    }

    /**
     * <summary>记录实际保存成功的报告，不改变流程结果。</summary>
     * <param name="relativeFile">相对报告根目录的真实报告文件路径。</param>
     */
    public void ReportSaved(string relativeFile) => _context.Observe(() =>
    {
        var fields = Fields();
        fields["ReportFile"] = relativeFile;
        _context.Output.Submit(SavedEvent, "开发流程报告已保存", fields);
    });

    /**
     * <summary>观察实际报告保存异常，不消费异常或覆盖流程结果。</summary>
     * <param name="error">报告写入的原异常。</param>
     */
    public void ReportSaveFailed(Exception error) => _context.Observe(() =>
    {
        var fields = Fields();
        ExceptionProjection.Add(fields, error);
        _context.Output.Submit(FailedEvent, "开发流程报告保存失败", fields);
    });

    private Dictionary<string, object?> Fields()
    {
        var fields = _context.Context("Command");
        fields["RunId"] = _runId;
        fields["RunMode"] = _runMode;
        return fields;
    }
}
