using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Land;
using Microsoft.Extensions.Logging;

namespace FarmExchange.Logging;

/**
 * <summary>拥有本局有界诊断范围和预算，不拥有经营状态或业务事件名单。</summary>
 */
public sealed class DiagnosticCapture
{
    private static readonly LogEventDescriptor Started = new(90, "DiagnosticCaptureStarted", "FarmExchange.Logging.DiagnosticCapture", LogLevel.Information, false);
    private static readonly LogEventDescriptor Ended = new(91, "DiagnosticCaptureEnded", "FarmExchange.Logging.DiagnosticCapture", LogLevel.Information, false);
    private readonly GameLog _context;
    private readonly FarmGame _game;
    private readonly Func<long> _uptime;
    private HashSet<string> _events = new(StringComparer.Ordinal);
    private readonly Dictionary<int, BuildingSpaceSnapshot> _anchors = new();
    private HashSet<Vector2I> _cells = new();
    private HashSet<int> _orders = new();
    private HashSet<int> _workers = new();
    private Dictionary<string, object?> _scope = new();
    private string? _id;
    private long _start;
    private long _duration;
    private int _limit;
    private long _captured;
    private long _suppressed;
    private bool _hadObjects;
    private bool _includeGameEvents;

    internal DiagnosticCapture(GameLog context, FarmGame game, Func<long>? uptime)
    {
        _context = context;
        _game = game;
        _uptime = uptime ?? (() => context.Output.UptimeMs);
    }

    /**
     * <summary>查询本局是否仍持有采集预算。</summary>
     */
    public bool IsActive => _id != null;

    /**
     * <summary>开始一次限定范围的采集；发布、已结束或已有采集时返回 false。</summary>
     * <param name="request">显式事件集合及最多 32 个实例或坐标、8 张订单、3 名工人，最长 120 秒和最多 5000 条。</param>
     * <returns>范围及预算合法且成功开始时为 true；非法请求抛出参数异常。</returns>
     */
    public bool Start(DiagnosticCaptureRequest request)
    {
        if (!_context.CanObserve || !_context.Output.DevelopmentEnabled || IsActive) return false;
        ArgumentNullException.ThrowIfNull(request);
        if (request.EventNames == null || request.EventNames.Count is < 1 or > 32 ||
            request.EventNames.Any(string.IsNullOrWhiteSpace) ||
            (request.Anchors?.Count ?? 0) + (request.Cells?.Count ?? 0) > 32 ||
            (request.OrderIds?.Count ?? 0) > 8 || (request.WorkerNumbers?.Count ?? 0) > 3 ||
            request.DurationLimitMs is < 1 or > 120000 || request.EventLimit is < 1 or > 5000 ||
            !request.IncludeGameEvents && (request.Anchors?.Count ?? 0) + (request.Cells?.Count ?? 0) + (request.OrderIds?.Count ?? 0) + (request.WorkerNumbers?.Count ?? 0) == 0)
            throw new ArgumentException("诊断事件、对象或预算超出允许范围", nameof(request));
        var anchors = new Dictionary<int, BuildingSpaceSnapshot>();
        foreach (Vector2I anchor in request.Anchors ?? Array.Empty<Vector2I>())
        {
            var space = _game.GetBuildingSpace(anchor);
            if (space == null || space.AnchorCell != anchor) throw new ArgumentException("诊断锚点必须是现存实例的锚点", nameof(request));
            anchors[space.AnchorIndex] = space;
        }
        var orders = new HashSet<int>(request.OrderIds ?? Array.Empty<int>());
        if (orders.Any(id => !_game.IsDiagnosticOrderActive(id)))
            throw new ArgumentException("诊断订单必须存在", nameof(request));
        var workers = new HashSet<int>(request.WorkerNumbers ?? Array.Empty<int>());
        if (workers.Any(number => number is < 1 or > 3)) throw new ArgumentException("诊断工人编号无效", nameof(request));
        _context.Observe(() =>
        {
            _start = _uptime();
            _duration = request.DurationLimitMs;
            _limit = request.EventLimit;
            _events = new(request.EventNames, StringComparer.Ordinal);
            _anchors.Clear();
            foreach (var pair in anchors) _anchors.Add(pair.Key, pair.Value);
            _cells = new(request.Cells ?? Array.Empty<Vector2I>());
            _orders = orders;
            _workers = workers;
            _includeGameEvents = request.IncludeGameEvents;
            _hadObjects = _anchors.Count + _cells.Count + _orders.Count + _workers.Count > 0;
            _captured = _suppressed = 0;
            _id = Guid.NewGuid().ToString("N");
            _scope = new()
            {
                ["EventNames"] = _events.ToArray(),
                ["IncludeGameEvents"] = _includeGameEvents,
                ["Anchors"] = anchors.Values.Select(space => Cell(space.AnchorCell)).ToArray(),
                ["Cells"] = _cells.Select(Cell).ToArray(),
                ["OrderIds"] = _orders.ToArray(),
                ["WorkerNumbers"] = _workers.ToArray(),
            };
            var fields = Fields();
            _scope["GameInstanceId"] = fields["GameInstanceId"];
            _context.Output.Submit(Started, "开始有界专项诊断", fields);
        });
        return IsActive;
    }

    /**
     * <summary>主动结束当前采集；重复调用不产生结束记录。</summary>
     */
    public void Stop() => End("UserStopped");

    /**
     * <summary>在主帧、真实命令和推进边界检查现实预算及实例失效。</summary>
     */
    public void Poll()
    {
        if (!IsActive) return;
        _context.Observe(() =>
        {
            if (_uptime() - _start >= _duration) { End("DurationLimit"); return; }
            foreach (var pair in _anchors.ToArray())
                if (!ReferenceEquals(pair.Value, _game.GetBuildingSpace(pair.Value.AnchorCell))) _anchors.Remove(pair.Key);
            if (_orders.Count > 0)
            {
                _orders.RemoveWhere(id => !_game.IsDiagnosticOrderActive(id));
            }
            if (!_includeGameEvents && _hadObjects && _anchors.Count + _cells.Count + _orders.Count + _workers.Count == 0) End("ObjectsEnded");
        });
    }

    internal bool ShouldCapture(string eventName, int? anchorIndex = null, int? orderId = null,
        int? workerNumber = null, Vector2I? cell = null)
    {
        if (!IsActive || !_context.Output.DevelopmentEnabled || !_events.Contains(eventName)) return false;
        // 大地图范围外候选只做集合查询，不能逐实例复制或检查整个观察范围。
        if (!Matches(anchorIndex, orderId, workerNumber, cell)) return false;
        Poll();
        if (!IsActive) return false;
        return Matches(anchorIndex, orderId, workerNumber, cell);
    }

    private bool Matches(int? anchorIndex, int? orderId, int? workerNumber, Vector2I? cell) =>
        (_includeGameEvents || anchorIndex.HasValue || orderId.HasValue || workerNumber.HasValue || cell.HasValue) &&
            (!anchorIndex.HasValue || _anchors.ContainsKey(anchorIndex.Value)) &&
            (!orderId.HasValue || _orders.Contains(orderId.Value)) &&
            (!workerNumber.HasValue || _workers.Contains(workerNumber.Value)) &&
            (!cell.HasValue || _cells.Contains(cell.Value));

    internal void Capture(LogEventDescriptor description, string message, Dictionary<string, object?> fields)
    {
        if (!IsActive || !_context.Output.DevelopmentEnabled) return;
        _context.Observe(() =>
        {
            if (_uptime() - _start >= _duration) { _suppressed++; End("DurationLimit"); return; }
            fields["CaptureId"] = _id;
            _captured++;
            _context.Output.Submit(description, message, fields);
            if (_captured >= _limit) End("EventLimit");
        });
    }

    internal void End(string reason)
    {
        if (!IsActive) return;
        try
        {
            _context.Observe(() =>
            {
                var fields = Fields();
                fields["ActualDurationMs"] = Math.Max(0, _uptime() - _start);
                fields["CapturedCount"] = _captured;
                fields["SuppressedCount"] = _suppressed;
                fields["StopReason"] = reason;
                _context.Output.Submit(Ended, "结束有界专项诊断", fields);
            });
        }
        finally
        {
            _id = null;
            _anchors.Clear();
            _orders.Clear();
            _workers.Clear();
            _cells.Clear();
        }
    }

    private Dictionary<string, object?> Fields()
    {
        var fields = _context.Context("Command");
        fields["CaptureId"] = _id;
        fields["CaptureScope"] = _scope;
        fields["DurationLimitMs"] = _duration;
        fields["EventLimit"] = _limit;
        return fields;
    }

    private static Dictionary<string, object?> Cell(Vector2I cell) => new() { ["X"] = cell.X, ["Y"] = cell.Y };
}
