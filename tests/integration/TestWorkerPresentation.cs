using System.Threading.Tasks;
using Godot;
using FarmExchange.Characters;
using FarmExchange.Gameplay;
using FarmExchange.UI;
using FarmExchange.World;

public partial class TestWorkerPresentation : Node
{
    public override async void _Ready()
    {
        bool passed = await RunChecksAsync(this);
        if (passed) GD.Print("集成测试：三工人快照、插值、暂停与地图变换通过");
        GetTree().Quit(passed ? 0 : 1);
    }

    public static async Task<bool> RunChecksAsync(Node parent)
    {
        bool passed = CheckInterpolation(parent) && CheckFrameIndependence(parent);
        if (!passed) return false;
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
        parent.AddChild(main);
        main.GetNode<Timer>("TickTimer").Stop();
        main.Game.SetPaused(true);
        await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        WorkerPresentation presentation = main.GetNode<WorkerPresentation>("WorldMap/WorkerPresentation");
        passed = presentation.GetChildCount() == 3 &&
            main.GetNode<Control>("CanvasLayer/UiRoot").FindChild("WorkerCountLabel", true, false)
                is Label { Text: "3 · 自动照料" };
        for (int i = 0; i < main.Game.GetWorkers().Count; i++)
        {
            var snapshot = main.Game.GetWorkers()[i];
            passed &= presentation.GetNode<NpcCharacter>($"Worker{snapshot.WorkerNumber}").GlobalPosition
                .IsEqualApprox(main.GetNode<WorldMap>("WorldMap").GetGridWorldPosition(snapshot.GridPosition));
        }
        main.QueueFree();
        await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        return passed || Fail("主场景没有组合三名真实角色，或人数/起始位置与经营快照不一致");
    }

    private static FarmGame CreateGame()
    {
        var game = new FarmGame(12345);
        Vector2I[] initial = { new(63, 63), new(64, 63), new(65, 63), new(63, 64), new(64, 64) };
        foreach (Vector2I cell in initial) game.RemoveBuilding(cell);
        Vector2I[] farms = { new(64, 64), new(67, 64), new(62, 65) };
        foreach (Vector2I farm in farms) game.BuildFarm(farm);
        return game;
    }

    private static WorkerPresentation CreatePresentation(Node parent, FarmGame game, out WorldMap map)
    {
        map = new WorldMap();
        parent.AddChild(map);
        var presentation = new WorkerPresentation();
        map.AddChild(presentation);
        presentation.SetGame(game, map);
        // 逐帧输入由测试显式提供，真实节点仍在树上；不让自动 _Process 干扰指定帧序列。
        presentation.SetProcess(false);
        return presentation;
    }

    private static bool CheckInterpolation(Node parent)
    {
        FarmGame game = CreateGame();
        WorkerPresentation presentation = CreatePresentation(parent, game, out WorldMap map);
        try
        {
            if (presentation.GetChildCount() != 3) return Fail("工人表现没有创建三实例");
            var initial = game.GetWorkers();
            game.AdvanceTick();
            var moved = game.GetWorkers();
            presentation._Process(0.5);
            string[] sheets = { "npc_animation_001.png", "npc_animation_002.png", "npc_animation_005.png" };
            for (int i = 0; i < sheets.Length; i++)
            {
                NpcCharacter character = presentation.GetNode<NpcCharacter>($"Worker{i + 1}");
                AnimatedSprite2D sprite = character.GetNode<AnimatedSprite2D>("AnimatedSprite2D");
                if (character.CharacterSheet.ResourcePath != $"res://assets/gameplay/npc/{sheets[i]}" ||
                    !sprite.IsPlaying())
                    return Fail("三名工人未使用既有三张不同图集，或移动动画没有播放");
                sprite.Frame = 3;
            }
            NpcCharacter first = presentation.GetNode<NpcCharacter>("Worker1");
            Vector2 halfway = initial[0].GridPosition.Lerp(moved[0].GridPosition, 0.5f);
            if (!first.GlobalPosition.IsEqualApprox(map.GetGridWorldPosition(halfway)) ||
                moved[0].GridPosition == initial[0].GridPosition)
                return Fail("显示未沿经营快照移动段插值");
            var thirdSprite = presentation.GetNode<AnimatedSprite2D>("Worker3/AnimatedSprite2D");
            if (thirdSprite.Animation != "run_left" || thirdSprite.FlipH || !thirdSprite.IsPlaying())
                return Fail("经营格移动方向没有映射为等距画面的左向动画");

            map.Position = new Vector2(113, -67);
            map.Rotation = 0.2f;
            map.Scale = new Vector2(1.1f, 0.9f);
            presentation._Process(0);
            if (!first.GlobalPosition.IsEqualApprox(map.GetGridWorldPosition(halfway)))
                return Fail("平移旋转缩放地图后，工人显示没有应用相同节点变换");
            Vector2 pausedPosition = first.GlobalPosition;
            game.SetPaused(true);
            presentation._Process(100);
            first._PhysicsProcess(1);
            if (!first.GlobalPosition.IsEqualApprox(pausedPosition) || thirdSprite.Frame != 3 ||
                thirdSprite.IsPlaying() || first.IsPhysicsProcessing() ||
                first.CollisionLayer != 0 || first.CollisionMask != 0 || game.Calendar.ElapsedSeconds != 1)
                return Fail("暂停未冻结位置/动画帧，或外部角色仍有独立物理移动");
            for (int i = 1; i <= 3; i++)
            {
                AnimatedSprite2D sprite = presentation.GetNode<AnimatedSprite2D>($"Worker{i}/AnimatedSprite2D");
                if (sprite.IsPlaying() || sprite.Frame != 3)
                    return Fail("暂停没有保留三人的当前动画帧");
            }

            game.SetPaused(false);
            presentation._Process(0.25);
            Vector2 resumed = initial[0].GridPosition.Lerp(moved[0].GridPosition, 0.75f);
            if (!first.GlobalPosition.IsEqualApprox(map.GetGridWorldPosition(resumed)))
                return Fail("暂停恢复补算了现实时间或丢失原插值段");
            presentation._Process(0.25);
            if (!first.GlobalPosition.IsEqualApprox(map.GetGridWorldPosition(moved[0].GridPosition)) ||
                thirdSprite.IsPlaying() || thirdSprite.Frame != 0)
                return Fail("插值结束未到快照位置或停步没有保留首帧");
            return true;
        }
        finally
        {
            parent.RemoveChild(map);
            map.Free();
        }
    }

    private static bool CheckFrameIndependence(Node parent)
    {
        FarmGame slowGame = CreateGame();
        FarmGame fastGame = CreateGame();
        WorkerPresentation slow = CreatePresentation(parent, slowGame, out WorldMap slowMap);
        WorkerPresentation fast = CreatePresentation(parent, fastGame, out WorldMap fastMap);
        // 工人和整张地图在镜头外依旧由经营秒推进，表现不可见也不影响状态。
        fastMap.Position = new Vector2(1000, 1000);
        fastMap.Hide();
        try
        {
            for (int second = 0; second < 12; second++)
            {
                slowGame.AdvanceTick();
                fastGame.AdvanceTick();
                slow._Process(1);
                for (int frame = 0; frame < 60; frame++) fast._Process(1.0 / 60);
                var slowWorkers = slowGame.GetWorkers();
                var fastWorkers = fastGame.GetWorkers();
                for (int i = 0; i < slowWorkers.Count; i++)
                {
                    if (slowWorkers[i] != fastWorkers[i])
                        return Fail("不同显示帧序列改变了经营工人状态");
                    NpcCharacter slowCharacter = slow.GetNode<NpcCharacter>($"Worker{i + 1}");
                    NpcCharacter fastCharacter = fast.GetNode<NpcCharacter>($"Worker{i + 1}");
                    if (!slowCharacter.Position.IsEqualApprox(fastCharacter.Position))
                        return Fail("相同显示经过时间在不同帧率下产生不同位置");
                }
            }
            Vector2I[] farms = { new(64, 64), new(67, 64), new(62, 65) };
            foreach (Vector2I farm in farms)
                if (slowGame.GetPlot(farm) != fastGame.GetPlot(farm) ||
                    slowGame.GetPlot(farm).Crop != CropStage.Growing)
                    return Fail("镜头外或显示帧率改变了播种/供水与生长");
            if (slowGame.Calendar != fastGame.Calendar || slowGame.MoneyCents != fastGame.MoneyCents ||
                slowGame.GetRawStock(CropKind.Wheat) != fastGame.GetRawStock(CropKind.Wheat))
                return Fail("表现帧推进改变了经营日历、金币或库存");
            return true;
        }
        finally
        {
            parent.RemoveChild(slowMap);
            parent.RemoveChild(fastMap);
            slowMap.Free();
            fastMap.Free();
        }
    }

    private static bool Fail(string message)
    {
        GD.PushError(message);
        return false;
    }
}
