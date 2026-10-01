using Godot;

namespace FarmExchange.Workers;

public enum WorkerActivity { Idle, Moving, Sowing, Watering }

public readonly record struct WorkerSnapshot(
    int WorkerNumber, Vector2 GridPosition, Vector2I? TargetCell, WorkerActivity Activity);
