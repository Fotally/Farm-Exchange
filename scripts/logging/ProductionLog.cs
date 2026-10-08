using System;
using System.Collections.Generic;
using FarmExchange.Farming;
using FarmExchange.Gameplay;
using FarmExchange.Market;

namespace FarmExchange.Logging;

/**
 * <summary>按局累计真实生产提交，并在完整命令或推进请求结束后输出资源窗口。</summary>
 * <remarks>只持有观察计数和边界快照，不执行生产；现实预算不拆分经营批次。</remarks>
 */
internal sealed class ProductionLog
{
    private static readonly LogEventDescriptor Summary = new(50, "ProductionSummary", "FarmExchange.Gameplay.FarmGame");
    private const long WindowMs = 60_000;
    private readonly GameLog _context;
    private readonly FarmGame _game;
    private readonly Func<long> _uptime;
    private readonly long[] _harvested = new long[CropCatalog.Crops.Count];
    private readonly long[] _consumed = new long[CropCatalog.Crops.Count];
    private readonly long[] _produced = new long[CropCatalog.Crops.Count];
    private readonly long[] _cleared = new long[CropCatalog.Crops.Count];
    private readonly HashSet<string> _reasons = new(StringComparer.Ordinal);
    private Boundary? _start;
    private long? _startMs;
    private uint _startSeconds;
    private long _windowId;
    private long _sown;
    private long _watered;
    private bool _advancing;
    private bool _ended;

    internal ProductionLog(GameLog context, FarmGame game, Func<long>? uptime = null)
    { _context = context; _game = game; _uptime = uptime ?? (() => context.Output.UptimeMs); }

    internal void Start() => Observe(() =>
    {
        _startSeconds = _game.Calendar.ElapsedSeconds;
        _start = CaptureBoundary();
        _startMs = _uptime();
    });

    internal void Harvested(CropKind crop, int quantity) => Observe(() => _harvested[(int)crop] += quantity);
    internal void Consumed(CropKind crop) => Observe(() => _consumed[(int)crop]++);
    internal void Produced(CropKind crop) => Observe(() => _produced[(int)crop]++);
    internal void WorkerCompleted(FarmWorkKind kind) => Observe(() =>
    {
        if (kind == FarmWorkKind.Sow) _sown++;
        else _watered++;
    });
    internal void Cleared(CropClearResult result) => Observe(() =>
    {
        foreach (var crop in CropCatalog.Crops) _cleared[(int)crop.Kind] += result.Count(crop.Kind);
    });
    internal void UnregisteredMutation() => Observe(() => _reasons.Add("UnregisteredMutation"));

    // AdvanceTicks 的检查点可以提交命令；这些命令的结束不会拆开外层请求。
    internal void BeginAdvance() => _advancing = true;
    internal void EndAdvance(bool completed)
    {
        _advancing = false;
        if (!completed) Observe(() => _reasons.Add("BoundaryMissing"));
        CheckBoundary();
    }

    internal void CheckBoundary()
    {
        if (_advancing) return;
        Observe(() =>
        {
            long now = _uptime();
            if (!_startMs.HasValue || now - _startMs.Value >= WindowMs) Flush(now, false);
        });
    }

    internal void End()
    {
        if (_ended) return;
        Observe(() => Flush(_uptime(), true));
        _ended = true;
    }

    private void Observe(Action action)
    {
        if (_ended) return;
        _context.Observe(() =>
        {
            try { action(); }
            catch
            {
                _reasons.Add("CollectorFailure");
                throw;
            }
        });
    }

    private Boundary? CaptureBoundary()
    {
        try
        {
            var inventory = new Dictionary<string, object?>();
            foreach (var item in CommodityCatalog.All)
                inventory[item.Id.Crop + "." + item.Id.Kind] = new Dictionary<string, object?>
                {
                    ["Total"] = _game.GetStock(item.Id),
                    ["Available"] = _game.GetAvailableStock(item.Id),
                    ["Frozen"] = _game.GetFrozenStock(item.Id),
                };
            return new(inventory, _game.MoneyCents, _game.AvailableMoneyCents, _game.FrozenMoneyCents);
        }
        catch
        {
            _reasons.Add("BoundaryMissing");
            _reasons.Add("CollectorFailure");
            return null;
        }
    }

    private void Flush(long now, bool partial)
    {
        Boundary? end = CaptureBoundary();
        if (!_startMs.HasValue || _start == null || end == null) _reasons.Add("BoundaryMissing");
        var fields = _context.Context("Shutdown");
        if (!partial) fields.Remove("Phase");
        fields["WindowId"] = ++_windowId;
        fields["IntervalStartUptimeMs"] = _startMs;
        fields["IntervalEndUptimeMs"] = now;
        fields["SimulationSecondsStart"] = _startSeconds;
        fields["SimulationSecondsEnd"] = _game.Calendar.ElapsedSeconds;
        fields["IsPartialWindow"] = partial;
        fields["CoverageStatus"] = _start == null && end == null ? "Unknown" : _reasons.Count == 0 ? "Complete" : "Partial";
        fields["CoverageReasons"] = new List<string>(_reasons);
        fields["SownCount"] = _sown;
        fields["WateredCount"] = _watered;
        fields["HarvestedByCrop"] = Project(_harvested);
        fields["RawConsumedByCrop"] = Project(_consumed);
        fields["ProducedByCrop"] = Project(_produced);
        fields["ClearedByCrop"] = Project(_cleared);
        AddBoundary(fields, "Start", _start);
        AddBoundary(fields, "End", end);
        _context.Output.Submit(Summary, "经营生产与资源窗口汇总", fields);
        _start = end;
        _startMs = now;
        _startSeconds = _game.Calendar.ElapsedSeconds;
        _sown = _watered = 0;
        Array.Clear(_harvested);
        Array.Clear(_consumed);
        Array.Clear(_produced);
        Array.Clear(_cleared);
        _reasons.Clear();
    }

    private static Dictionary<string, object?> Project(long[] values)
    {
        var result = new Dictionary<string, object?>();
        foreach (var crop in CropCatalog.Crops) result[crop.Kind.ToString()] = values[(int)crop.Kind];
        return result;
    }

    private static void AddBoundary(Dictionary<string, object?> fields, string suffix, Boundary? value)
    {
        fields["Inventory" + suffix] = value?.Inventory;
        fields["Money" + suffix + "Cents"] = value?.Money;
        fields["AvailableMoney" + suffix + "Cents"] = value?.Available;
        fields["FrozenMoney" + suffix + "Cents"] = value?.Frozen;
    }

    private sealed record Boundary(Dictionary<string, object?> Inventory, int Money, int Available, int Frozen);
}
