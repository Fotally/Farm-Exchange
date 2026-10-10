using System.Collections.Generic;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Workers;
using Microsoft.Extensions.Logging;

namespace FarmExchange.Logging;

/**
 * <summary>观察选定工人的实际任务转换与经营位置，不读取视觉插值或执行任务。</summary>
 */
internal sealed class WorkerDiagnostics
{
    private static readonly LogEventDescriptor TaskEvent = new(102, "WorkerTaskChanged", "FarmExchange.Workers.WorkerScheduler", LogLevel.Debug, false);
    private static readonly LogEventDescriptor MoveEvent = new(103, "WorkerMoved", "FarmExchange.Workers.WorkerScheduler", LogLevel.Trace, false);
    private readonly GameLog _context;

    internal WorkerDiagnostics(GameLog context) => _context = context;
    internal bool CanObserveTask(int number) => _context.Diagnostics.ShouldCapture(TaskEvent.Name, workerNumber: number);
    internal bool CanObserveMove(int number) => _context.Diagnostics.ShouldCapture(MoveEvent.Name, workerNumber: number);

    internal void TaskChanged(int number, WorkerTaskDiagnosticState before, WorkerTaskDiagnosticState after)
    {
        if (before == after || !CanObserveTask(number)) return;
        CaptureTaskChanged(number, before, after);
    }

    private void CaptureTaskChanged(int number, WorkerTaskDiagnosticState before, WorkerTaskDiagnosticState after)
    {
        _context.Observe(() =>
        {
            var fields = _context.Context("Workers");
            fields["WorkerNumber"] = number;
            fields["ActivityBefore"] = before.Activity.ToString();
            fields["ActivityAfter"] = after.Activity.ToString();
            fields["TargetAnchorBefore"] = Anchor(before.AnchorIndex);
            fields["TargetAnchorAfter"] = Anchor(after.AnchorIndex);
            _context.Diagnostics.Capture(TaskEvent, "选定工人实际任务转换", fields);
        });
    }

    internal void Moved(int number, Vector2 before, Vector2 after)
    {
        if (before == after || !CanObserveMove(number)) return;
        CaptureMoved(number, before, after);
    }

    private void CaptureMoved(int number, Vector2 before, Vector2 after)
    {
        _context.Observe(() =>
        {
            var fields = _context.Context("Workers");
            fields["WorkerNumber"] = number;
            fields["PositionBefore"] = Position(before);
            fields["PositionAfter"] = Position(after);
            _context.Diagnostics.Capture(MoveEvent, "选定工人经营位置变化", fields);
        });
    }

    private static Dictionary<string, object?>? Anchor(int? index) => index.HasValue ? new()
    {
        ["X"] = index.Value % FarmGame.MapSize,
        ["Y"] = index.Value / FarmGame.MapSize,
    } : null;
    private static Dictionary<string, object?> Position(Vector2 position) => new() { ["X"] = position.X, ["Y"] = position.Y };
}

internal readonly record struct WorkerTaskDiagnosticState(WorkerActivity Activity, int? AnchorIndex);
