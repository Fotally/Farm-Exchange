using Godot;
using FarmExchange.Characters;
using FarmExchange.Gameplay;
using FarmExchange.Workers;

namespace FarmExchange.World;

public partial class WorkerPresentation : Node2D
{
    private static readonly int[] CharacterIds = { 1, 2, 5 };
    private FarmGame _game = null!;
    private WorldMap _map = null!;
    private WorkerVisual[] _workers = System.Array.Empty<WorkerVisual>();
    private uint _snapshotSecond;
    private double _interpolationSeconds;

    public void SetGame(FarmGame game, WorldMap map)
    {
        _game = game;
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
            character.SetCharacter(GD.Load<Texture2D>(
                $"res://assets/gameplay/npc/npc_animation_{CharacterIds[i]:D3}.png"));
            AddChild(character);
            character.GetNode<AnimatedSprite2D>("AnimatedSprite2D").Position = new Vector2(0, -20);
            _workers[i] = new WorkerVisual(character, snapshot.GridPosition);
            character.ShowAt(ToLocal(map.GetGridWorldPosition(snapshot.GridPosition)), Vector2.Zero,
                moving: false, paused: game.IsPaused);
        }
    }

    public override void _Process(double delta)
    {
        if (_game.IsPaused)
        {
            foreach (WorkerVisual worker in _workers)
                worker.Character.ShowAt(ToLocal(_map.GetGridWorldPosition(worker.Position)),
                    Vector2.Zero, moving: false, paused: true);
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
            _interpolationSeconds = 0;
        }
        _interpolationSeconds = System.Math.Min(1, _interpolationSeconds + delta);
        float progress = (float)_interpolationSeconds;
        foreach (WorkerVisual worker in _workers)
        {
            worker.Position = worker.From.Lerp(worker.To, progress);
            Vector2 direction = MapCoordinates.GridPositionToLocal(worker.To) -
                MapCoordinates.GridPositionToLocal(worker.From);
            worker.Character.ShowAt(ToLocal(_map.GetGridWorldPosition(worker.Position)), direction,
                moving: progress < 1 && direction != Vector2.Zero, paused: false);
        }
    }

    private sealed class WorkerVisual
    {
        internal readonly NpcCharacter Character;
        internal Vector2 From;
        internal Vector2 To;
        internal Vector2 Position;

        internal WorkerVisual(NpcCharacter character, Vector2 position)
        {
            Character = character;
            From = To = Position = position;
        }
    }
}
