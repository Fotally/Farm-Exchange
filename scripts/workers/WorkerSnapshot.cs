using Godot;
using FarmExchange.Terrain;
using FarmExchange.Farming;

namespace FarmExchange.Workers;

public enum WorkerActivity { Idle, Moving, Sowing, Watering }

public readonly record struct WorkerSnapshot(
    int WorkerNumber, Vector2 GridPosition, Vector2I? TargetCell, WorkerActivity Activity)
{
    /**
     * <summary>权威 double 连续位置；GridPosition 仅为显示投影，不可用于接续行程。</summary>
     */
    public LandPosition ExactPosition { get; init; }
    public LandPath? Path { get; init; }
    public int TravelTicks { get; init; }
    public WorkerMovementSnapshot? Movement { get; init; }
}

/**
 * <summary>最近完整经营秒内已实际发生的移动，距离仅供沿同一不可变路径展示。</summary>
 */
public readonly record struct WorkerMovementSnapshot(
    LandPath Path, uint ElapsedSeconds, double FromDistance, double ToDistance,
    Vector2I TargetCell, WorkerActivity WorkActivity)
{
    internal FarmWorkRequest Work { get; init; }
}
