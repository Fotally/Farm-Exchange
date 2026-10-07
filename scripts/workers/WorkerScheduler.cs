using System;
using System.Collections.Generic;
using Godot;
using FarmExchange.Farming;
using FarmExchange.Gameplay;
using FarmExchange.Land;
using FarmExchange.Time;

namespace FarmExchange.Workers;

internal sealed class WorkerScheduler
{
    private const float CellsPerSecond = 3f;
    private readonly WorkerState[] _workers;
    private readonly HashSet<int> _claimedCells = new();
    private int _nextFarmIndex;

    internal WorkerScheduler(params Vector2I[] startingCells)
    {
        if (startingCells.Length == 0)
            throw new ArgumentException("至少需要一名工人", nameof(startingCells));
        _workers = new WorkerState[startingCells.Length];
        for (int i = 0; i < startingCells.Length; i++)
            _workers[i] = new WorkerState(startingCells[i]);
    }

    internal bool AdvanceOneSecond(FarmingSystem farming, CalendarSnapshot calendar,
        Action<int, FarmWorkRequest>? completed = null)
    {
        if (calendar.IsPaused)
            return false;

        foreach (WorkerState worker in _workers)
        {
            if (worker.Work is FarmWorkRequest work &&
                farming.GetWorkNeed(work.CellIndex, calendar) != work)
                ReleaseWork(worker);
        }

        bool acted = false;
        for (int workerIndex = 0; workerIndex < _workers.Length; workerIndex++)
        {
            WorkerState worker = _workers[workerIndex];
            if (worker.Work == null)
            {
                FarmWorkRequest? selected = SelectNextWork(farming, calendar);
                if (selected == null)
                    continue;
                BeginWork(worker, selected.Value);
            }

            if (worker.RemainingTravelSeconds > 0)
            {
                AdvanceTravel(worker);
                continue;
            }

            FarmWorkRequest current = worker.Work!.Value;
            if (!farming.TryCompleteWork(current, calendar))
            {
                ReleaseWork(worker);
                continue;
            }
            acted = true;
            completed?.Invoke(workerIndex + 1, current);
            FarmWorkRequest? next = farming.GetWorkNeed(current.CellIndex, calendar);
            if (current.Kind == FarmWorkKind.Sow && next is { Kind: FarmWorkKind.Water })
                worker.Work = next;
            else
                ReleaseWork(worker);
        }
        return acted;
    }

    internal IReadOnlyList<WorkerSnapshot> GetSnapshots()
    {
        var snapshots = new WorkerSnapshot[_workers.Length];
        for (int i = 0; i < _workers.Length; i++)
        {
            WorkerState worker = _workers[i];
            WorkerActivity activity = worker.Work == null ? WorkerActivity.Idle :
                worker.RemainingTravelSeconds > 0 ? WorkerActivity.Moving :
                worker.Work.Value.Kind == FarmWorkKind.Sow ? WorkerActivity.Sowing : WorkerActivity.Watering;
            snapshots[i] = new WorkerSnapshot(i + 1, worker.GridPosition,
                worker.Work is FarmWorkRequest work ? CellOf(work.CellIndex) : null, activity);
        }
        return Array.AsReadOnly(snapshots);
    }

    /**
     * <summary>查询下一次任务选择、失效、抵达或动作的经营秒距离。</summary>
     * <param name="farming">本局农田状态。</param>
     * <param name="calendar">当前完整经营步结束时的日历。</param>
     * <returns>无待处理事件时为 uint.MaxValue，其余为至少一秒。</returns>
     */
    internal uint GetNextEventSeconds(FarmingSystem farming, CalendarSnapshot calendar)
    {
        uint next = uint.MaxValue;
        bool idle = false;
        foreach (WorkerState worker in _workers)
        {
            if (worker.Work is not FarmWorkRequest work)
            {
                idle = true;
                continue;
            }
            if (farming.GetWorkNeed(work.CellIndex, calendar) != work || worker.RemainingTravelSeconds == 0)
                return 1;
            next = Math.Min(next, (uint)worker.RemainingTravelSeconds);
        }
        if (idle)
            foreach (int index in farming.Indices)
                if (!_claimedCells.Contains(index) && farming.GetWorkNeed(index, calendar) != null)
                    return 1;
        return next;
    }

    /**
     * <summary>累计无任务边界的行程，沿用单秒推进的位置公式。</summary>
     * <param name="seconds">严格位于下一工人事件之前的完整经营秒数。</param>
     */
    internal void AdvanceQuietSeconds(uint seconds)
    {
        foreach (WorkerState worker in _workers)
            if (worker.Work != null && worker.RemainingTravelSeconds > 0)
                AdvanceTravel(worker, checked((int)seconds));
    }

    private FarmWorkRequest? SelectNextWork(FarmingSystem farming, CalendarSnapshot calendar)
    {
        IReadOnlyList<int> indices = farming.Indices;
        int start = 0;
        while (start < indices.Count && indices[start] < _nextFarmIndex)
            start++;
        for (int offset = 0; offset < indices.Count; offset++)
        {
            int index = indices[(start + offset) % indices.Count];
            if (_claimedCells.Contains(index) || farming.GetWorkNeed(index, calendar) is not FarmWorkRequest work)
                continue;
            _nextFarmIndex = (index + 1) % farming.CellCount;
            _claimedCells.Add(index);
            return work;
        }
        return null;
    }

    private static void BeginWork(WorkerState worker, FarmWorkRequest work)
    {
        worker.Work = work;
        Vector2 distance = (Vector2)CellOf(work.CellIndex) - worker.GridPosition;
        float gridDistance = Mathf.Max(Mathf.Abs(distance.X), Mathf.Abs(distance.Y));
        worker.RemainingTravelSeconds = Mathf.CeilToInt(gridDistance / CellsPerSecond);
        worker.TravelStart = worker.GridPosition;
        worker.TravelTicks = 0;
        worker.TravelPerSecond = gridDistance == 0f ? Vector2.Zero : distance * (CellsPerSecond / gridDistance);
    }

    private static void AdvanceTravel(WorkerState worker, int seconds = 1)
    {
        worker.RemainingTravelSeconds -= seconds;
        worker.TravelTicks += seconds;
        worker.GridPosition = worker.TravelStart + worker.TravelPerSecond * worker.TravelTicks;
        if (worker.RemainingTravelSeconds == 0)
            worker.GridPosition = CellOf(worker.Work!.Value.CellIndex);
    }

    private void ReleaseWork(WorkerState worker)
    {
        _claimedCells.Remove(worker.Work!.Value.CellIndex);
        worker.Work = null;
        worker.RemainingTravelSeconds = 0;
        worker.TravelPerSecond = Vector2.Zero;
    }

    private static Vector2I CellOf(int index) => BuildingFootprint.WorkCell(
        new Vector2I(index % FarmGame.MapSize, index / FarmGame.MapSize), BuildingKind.Farm);

    private sealed class WorkerState
    {
        internal Vector2 GridPosition;
        internal FarmWorkRequest? Work;
        internal int RemainingTravelSeconds;
        internal Vector2 TravelPerSecond;
        internal Vector2 TravelStart;
        internal int TravelTicks;

        internal WorkerState(Vector2I startingCell) => GridPosition = startingCell;
    }
}
