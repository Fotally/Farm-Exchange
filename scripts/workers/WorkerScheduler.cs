using System;
using System.Collections.Generic;
using Godot;
using FarmExchange.Farming;
using FarmExchange.Gameplay;
using FarmExchange.Land;
using FarmExchange.Logging;
using FarmExchange.Time;
using FarmExchange.Terrain;

namespace FarmExchange.Workers;

internal sealed class WorkerScheduler
{
    private const int CellsPerSecond = 3;
    private readonly TerrainReadView _terrain;
    private readonly WorkerState[] _workers;
    private readonly HashSet<int> _claimedCells = new();
    private int _nextFarmIndex;

    internal WorkerScheduler(TerrainReadView terrain, params Vector2I[] startingCells)
    {
        _terrain = terrain;
        if (startingCells.Length == 0)
            throw new ArgumentException("至少需要一名工人", nameof(startingCells));
        _workers = new WorkerState[startingCells.Length];
        for (int i = 0; i < startingCells.Length; i++)
            _workers[i] = new WorkerState(startingCells[i]);
    }

    internal WorkerDiagnostics? Diagnostics { get; set; }

    internal bool AdvanceOneSecond(FarmingSystem farming, CalendarSnapshot calendar,
        Action<int, FarmWorkRequest>? completed = null)
    {
        if (calendar.IsPaused)
            return false;

        for (int workerIndex = 0; workerIndex < _workers.Length; workerIndex++)
        {
            WorkerState worker = _workers[workerIndex];
            worker.Movement = null;
            if (worker.Work is FarmWorkRequest work &&
                farming.GetWorkNeed(work.CellIndex, calendar) != work)
                ReleaseWork(worker, workerIndex + 1);
        }

        bool acted = false;
        for (int workerIndex = 0; workerIndex < _workers.Length; workerIndex++)
        {
            WorkerState worker = _workers[workerIndex];
            if (worker.Work == null)
            {
                FarmWorkRequest? selected = SelectNextWork(worker, farming, calendar, out LandPath? path);
                if (selected == null)
                    continue;
                BeginWork(worker, selected.Value, path!, workerIndex + 1);
            }

            if (worker.RemainingTravelSeconds > 0)
            {
                AdvanceTravel(worker, workerIndex + 1, checked(calendar.ElapsedSeconds + 1));
                continue;
            }

            FarmWorkRequest current = worker.Work!.Value;
            if (!farming.TryCompleteWork(current, calendar))
            {
                ReleaseWork(worker, workerIndex + 1);
                continue;
            }
            acted = true;
            completed?.Invoke(workerIndex + 1, current);
            FarmWorkRequest? next = farming.GetWorkNeed(current.CellIndex, calendar);
            if (current.Kind == FarmWorkKind.Sow && next is { Kind: FarmWorkKind.Water })
            {
                WorkerTaskDiagnosticState? before = TaskBefore(worker, workerIndex + 1);
                worker.Work = next;
                TaskAfter(worker, workerIndex + 1, before);
            }
            else
                ReleaseWork(worker, workerIndex + 1);
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
            snapshots[i] = new WorkerSnapshot(i + 1, worker.Position.ToVector2(),
                worker.Work is FarmWorkRequest work ? CellOf(work.CellIndex) : null, activity)
            {
                ExactPosition = worker.Position,
                Path = worker.Path,
                TravelTicks = worker.TravelTicks,
                Movement = worker.Movement,
            };
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
     * <param name="elapsedSeconds">本区间开始前的累计完整经营秒。</param>
     */
    internal void AdvanceQuietSeconds(uint seconds, uint elapsedSeconds)
    {
        for (int workerIndex = 0; workerIndex < _workers.Length; workerIndex++)
        {
            WorkerState worker = _workers[workerIndex];
            worker.Movement = null;
            if (worker.Work != null && worker.RemainingTravelSeconds > 0)
                AdvanceTravel(worker, workerIndex + 1, checked(elapsedSeconds + seconds), checked((int)seconds));
        }
    }

    private FarmWorkRequest? SelectNextWork(WorkerState worker, FarmingSystem farming,
        CalendarSnapshot calendar, out LandPath? path)
    {
        path = null;
        IReadOnlyList<int> indices = farming.Indices;
        int start = 0;
        while (start < indices.Count && indices[start] < _nextFarmIndex)
            start++;
        for (int offset = 0; offset < indices.Count; offset++)
        {
            int index = indices[(start + offset) % indices.Count];
            if (_claimedCells.Contains(index) || farming.GetWorkNeed(index, calendar) is not FarmWorkRequest work)
                continue;
            LandPathResult result = _terrain.FindLandPath(worker.Position, LandPosition.FromCell(CellOf(index)));
            if (result.Status != LandPathStatus.Success)
                continue;
            path = result.Path;
            _nextFarmIndex = (index + 1) % farming.CellCount;
            _claimedCells.Add(index);
            return work;
        }
        return null;
    }

    private void BeginWork(WorkerState worker, FarmWorkRequest work, LandPath path, int number)
    {
        WorkerTaskDiagnosticState? before = TaskBefore(worker, number);
        worker.Work = work;
        worker.Path = path;
        worker.RemainingTravelSeconds = path.GetTravelTicks(CellsPerSecond);
        worker.TravelTicks = 0;
        TaskAfter(worker, number, before);
    }

    private void AdvanceTravel(WorkerState worker, int number, uint elapsedSeconds, int seconds = 1)
    {
        WorkerTaskDiagnosticState? taskBefore = TaskBefore(worker, number);
        LandPosition? before = Diagnostics?.CanObserveMove(number) == true ? worker.Position : null;
        worker.RemainingTravelSeconds -= seconds;
        worker.TravelTicks = checked(worker.TravelTicks + seconds);
        LandPath path = worker.Path!;
        int distance = checked(worker.TravelTicks * CellsPerSecond);
        worker.Position = path.GetPositionAtDistance(distance);
        FarmWorkRequest work = worker.Work!.Value;
        worker.Movement = new WorkerMovementSnapshot(path, elapsedSeconds,
            Math.Min(distance - CellsPerSecond, path.TotalLength), Math.Min(distance, path.TotalLength),
            CellOf(work.CellIndex),
            work.Kind == FarmWorkKind.Sow ? WorkerActivity.Sowing : WorkerActivity.Watering)
        {
            Work = work,
        };
        if (before.HasValue) Diagnostics?.Moved(number, before.Value, worker.Position);
        TaskAfter(worker, number, taskBefore);
    }

    private void ReleaseWork(WorkerState worker, int number)
    {
        WorkerTaskDiagnosticState? before = TaskBefore(worker, number);
        _claimedCells.Remove(worker.Work!.Value.CellIndex);
        worker.Work = null;
        worker.RemainingTravelSeconds = 0;
        worker.Path = null;
        worker.TravelTicks = 0;
        TaskAfter(worker, number, before);
    }

    private WorkerTaskDiagnosticState? TaskBefore(WorkerState worker, int number) =>
        Diagnostics?.CanObserveTask(number) == true ? new(ActivityOf(worker), worker.Work?.CellIndex) : null;

    private void TaskAfter(WorkerState worker, int number, WorkerTaskDiagnosticState? before)
    {
        if (before.HasValue && Diagnostics?.CanObserveTask(number) == true)
            Diagnostics?.TaskChanged(number, before.Value, new(ActivityOf(worker), worker.Work?.CellIndex));
    }

    private static WorkerActivity ActivityOf(WorkerState worker) => worker.Work == null ? WorkerActivity.Idle :
        worker.RemainingTravelSeconds > 0 ? WorkerActivity.Moving :
        worker.Work.Value.Kind == FarmWorkKind.Sow ? WorkerActivity.Sowing : WorkerActivity.Watering;

    private static Vector2I CellOf(int index) => BuildingFootprint.WorkCell(
        new Vector2I(index % FarmGame.MapSize, index / FarmGame.MapSize), BuildingKind.Farm);

    private sealed class WorkerState
    {
        internal LandPosition Position;
        internal FarmWorkRequest? Work;
        internal int RemainingTravelSeconds;
        internal LandPath? Path;
        internal int TravelTicks;
        internal WorkerMovementSnapshot? Movement;

        internal WorkerState(Vector2I startingCell) => Position = LandPosition.FromCell(startingCell);
    }
}
