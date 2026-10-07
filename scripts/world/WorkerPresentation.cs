using Godot;
using FarmExchange.Characters;
using FarmExchange.Gameplay;
using FarmExchange.Land;
using FarmExchange.Workers;
using FarmExchange.Time;

namespace FarmExchange.World;

public partial class WorkerPresentation : Node2D
{
    private static readonly int[] CharacterIds = { 1, 2, 5 };
    private FarmGame _game = null!;
    private WorldMap _map = null!;
    private WorkerVisual[] _workers = System.Array.Empty<WorkerVisual>();
    private uint _snapshotSecond;
    private double _interpolationSeconds;
    private SimulationDriver? _driver;

    /**
     * <summary>绑定真实经营快照、地图坐标与本局倍率。</summary>
     * <param name="game">只读经营对象。</param>
     * <param name="map">地图坐标表现。</param>
     * <param name="driver">同一局驱动；独立表现测试省略时按1×显示。</param>
     */
    public void SetGame(FarmGame game, WorldMap map, SimulationDriver? driver = null)
    {
        _game = game;
        _driver = driver;
        _map = map;
        Name = "WorkerPresentation";
        YSortEnabled = true;
        _snapshotSecond = game.Calendar.ElapsedSeconds;
        var snapshots = game.GetWorkers();
        _workers = new WorkerVisual[snapshots.Count];
        PackedScene scene = GD.Load<PackedScene>("res://scenes/npc_character.tscn");
        for (int i = 0; i < snapshots.Count; i++)
        {
            WorkerSnapshot snapshot = snapshots[i];
            NpcCharacter character = scene.Instantiate<NpcCharacter>();
            character.Name = $"Worker{snapshot.WorkerNumber}";
            character.SetCharacter(CharacterIds[i]);
            AddChild(character);
            _workers[i] = new WorkerVisual(character, snapshot.GridPosition);
            character.ShowAt(ToLocal(map.GetGridWorldPosition(snapshot.GridPosition)), Vector2.Zero,
                moving: false, paused: game.IsPaused);
        }
    }

    public override void _Process(double delta)
    {
        if (_game.IsPaused)
        {
            var snapshots = _game.GetWorkers();
            for (int i = 0; i < _workers.Length; i++)
            {
                WorkerVisual worker = _workers[i];
                worker.From = worker.To = worker.Position = snapshots[i].GridPosition;
                if (worker.Result is ProductionResult old && !_game.IsPresentationTargetCurrent(old))
                {
                    worker.Result = null;
                    worker.Character.ShowAt(ToLocal(_map.GetGridWorldPosition(worker.Position)),
                        Vector2.Zero, moving: false, paused: false);
                }
                worker.Character.ShowAt(ToLocal(_map.GetGridWorldPosition(worker.Position)),
                    Vector2.Zero, moving: false, paused: true);
            }
            _snapshotSecond = _game.Calendar.ElapsedSeconds;
            _interpolationSeconds = 1;
            return;
        }
        if (_snapshotSecond != _game.Calendar.ElapsedSeconds)
        {
            _snapshotSecond = _game.Calendar.ElapsedSeconds;
            var snapshots = _game.GetWorkers();
            for (int i = 0; i < _workers.Length; i++)
            {
                _workers[i].From = _workers[i].Position;
                _workers[i].To = snapshots[i].GridPosition;
            }
            foreach (ProductionResult result in _game.GetPresentationResults())
            {
                if (result.Kind is not (ProductionResultKind.Sow or ProductionResultKind.Water) ||
                    !_game.IsPresentationResultCurrent(result))
                    continue;
                WorkerVisual worker = _workers[result.WorkerNumber - 1];
                worker.Result = result;
                worker.From = worker.Position = worker.To;
                worker.NewAction = true;
            }
            _interpolationSeconds = 0;
        }
        double rate = _driver?.Rate ?? 1;
        bool accelerated = !SimulationDriver.IsPublicRateAllowed(rate);
        _interpolationSeconds = System.Math.Min(1, _interpolationSeconds + delta * rate);
        float progress = accelerated ? 1 : (float)_interpolationSeconds;
        var current = _game.GetWorkers();
        for (int i = 0; i < _workers.Length; i++)
        {
            WorkerVisual worker = _workers[i];
            worker.Position = worker.From.Lerp(worker.To, progress);
            Vector2 direction = MapCoordinates.GridPositionToLocal(worker.To) -
                MapCoordinates.GridPositionToLocal(worker.From);
            if (worker.Result is ProductionResult old)
            {
                Vector2 workCell = BuildingFootprint.WorkCell(old.AnchorCell, BuildingKind.Farm);
                if (!_game.IsPresentationTargetCurrent(old) ||
                    current[i].GridPosition != workCell ||
                    (current[i].TargetCell is Vector2I target && (Vector2)target != workCell) ||
                    (accelerated && old.ElapsedSeconds != _game.Calendar.ElapsedSeconds) ||
                    (!worker.NewAction && worker.Character.WorkAnimationFinished))
                    worker.Result = null;
            }
            NpcAction? action = worker.Result?.Kind switch
            {
                ProductionResultKind.Sow => NpcAction.Sow,
                ProductionResultKind.Water => NpcAction.Water,
                _ => null,
            };
            worker.Character.ShowAt(ToLocal(_map.GetGridWorldPosition(worker.Position)), direction,
                moving: accelerated ? current[i].Activity == WorkerActivity.Moving :
                    progress < 1 && direction != Vector2.Zero,
                paused: false, animationRate: accelerated ? 1 : rate,
                workAction: action, restartAction: worker.NewAction);
            worker.NewAction = false;
        }
    }

    private sealed class WorkerVisual
    {
        internal readonly NpcCharacter Character;
        internal Vector2 From;
        internal Vector2 To;
        internal Vector2 Position;
        internal ProductionResult? Result;
        internal bool NewAction;

        internal WorkerVisual(NpcCharacter character, Vector2 position)
        {
            Character = character;
            From = To = Position = position;
        }
    }
}
