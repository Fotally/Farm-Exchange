using System.Threading.Tasks;
using Godot;
using FarmExchange.Characters;
using FarmExchange.Gameplay;
using FarmExchange.UI;
using FarmExchange.World;
using FarmExchange.Time;

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
        bool passed = CheckInterpolation(parent) && CheckFrameIndependence(parent) && CheckRates(parent) &&
            CheckSuccessfulActions(parent);
        if (!passed) return false;
        if (!await CheckCompleteActionClips(parent)) return false;
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
        parent.AddChild(main);
        main.SetProcess(false);
        main.Game.SetPaused(true);
        await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        WorkerPresentation presentation = (WorkerPresentation)main.GetNode<WorldMap>("WorldMap").FindChild("WorkerPresentation", true, false);
        passed = presentation.GetChildCount() == 3 &&
            main.GetNode<Control>("CanvasLayer/UiRoot").FindChild("WorkerCountLabel", true, false)
                is Label { Text: "3 名工人" };
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
        foreach (var space in game.GetBuildingSpaces()) game.RemoveBuilding(space.AnchorCell);
        Vector2I[] farms = { new(192, 192), new(201, 192), new(186, 195) };
        foreach (Vector2I farm in farms) game.BuildFarm(farm);
        return game;
    }

    private static WorkerPresentation CreatePresentation(Node parent, FarmGame game, out WorldMap map, SimulationDriver? driver = null)
    {
        map = new WorldMap();
        parent.AddChild(map);
        var presentation = new WorkerPresentation();
        map.AddChild(presentation);
        presentation.SetGame(game, map, driver);
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
            string[] sheets = { "npc001", "npc002", "npc005" };
            for (int i = 0; i < sheets.Length; i++)
            {
                NpcCharacter character = presentation.GetNode<NpcCharacter>($"Worker{i + 1}");
                AnimatedSprite2D sprite = character.GetNode<AnimatedSprite2D>("AnimatedSprite2D");
                if (character.CharacterFrames.ResourcePath != $"res://assets/gameplay/npc/{sheets[i]}/{sheets[i]}_sprite_frames.tres" ||
                    !sprite.IsPlaying() || sprite.Position != new Vector2(0, -28) ||
                    ((AtlasTexture)sprite.SpriteFrames.GetFrameTexture("run_left", 0)).Region.Size != new Vector2(64, 64))
                    return Fail("三名工人未使用清亮版三套不同四向动作，或移动动画没有播放");
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
            Vector2 pausedPosition = map.GetGridWorldPosition(moved[0].GridPosition);
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
            Vector2 resumed = moved[0].GridPosition;
            if (!first.GlobalPosition.IsEqualApprox(map.GetGridWorldPosition(resumed)))
                return Fail("暂停恢复未保持对齐后的真实经营位置");
            presentation._Process(0.25);
            if (!first.GlobalPosition.IsEqualApprox(map.GetGridWorldPosition(moved[0].GridPosition)) ||
                !thirdSprite.IsPlaying() || thirdSprite.Animation != "idle_left")
                return Fail("插值结束未到快照位置或停步没有播放独立待机");
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
            Vector2I[] farms = { new(192, 192), new(201, 192), new(186, 195) };
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

    private static bool CheckRates(Node parent)
    {
        foreach (double rate in new[] { 0.5, 1, 2, 20 })
        {
            FarmGame game = CreateGame();
            var driver = new SimulationDriver();
            driver.SetDevelopmentRate(rate);
            WorkerPresentation presentation = CreatePresentation(parent, game, out WorldMap map, driver);
            try
            {
                var initial = game.GetWorkers();
                game.AdvanceTick();
                var moved = game.GetWorkers();
                presentation._Process(0.25 / rate);
                NpcCharacter character = presentation.GetNode<NpcCharacter>("Worker1");
                Vector2 expected = rate > 2 ? moved[0].GridPosition :
                    initial[0].GridPosition.Lerp(moved[0].GridPosition, 0.25f);
                AnimatedSprite2D sprite = character.GetNode<AnimatedSprite2D>("AnimatedSprite2D");
                if (!character.GlobalPosition.IsEqualApprox(map.GetGridWorldPosition(expected)) ||
                    !Mathf.IsEqualApprox(sprite.SpeedScale, rate > 2 ? 1 : (float)rate))
                    return Fail("经营倍率未同比控制位置/公共动画，高倍率未显示最新真实位置并保留正常动画速度");
            }
            finally
            {
                parent.RemoveChild(map);
                map.Free();
            }
        }
        return true;
    }

    private static bool CheckSuccessfulActions(Node parent)
    {
        var game = new FarmGame(12345);
        foreach (var space in game.GetBuildingSpaces()) game.RemoveBuilding(space.AnchorCell);
        Vector2I farm = new(189, 189);
        game.TryPlace(farm, BuildingKind.Farm, CropKind.Radish);
        WorkerPresentation presentation = CreatePresentation(parent, game, out WorldMap map);
        try
        {
            var sprite = presentation.GetNode<AnimatedSprite2D>("Worker1/AnimatedSprite2D");
            presentation._Process(.1);
            if (!sprite.Animation.ToString().StartsWith("idle_")) return Fail("待执行作业被当作成功");
            game.AdvanceTick();
            presentation._Process(.1);
            if (!sprite.Animation.ToString().StartsWith("sow_")) return Fail("真正播种没有播放合成动作");
            sprite.SetFrameAndProgress(3, .4f);
            presentation._Process(.1);
            if (sprite.Frame != 3 || !Mathf.IsEqualApprox(sprite.FrameProgress, .4f))
                return Fail("同一真实结果被每帧重播");
            game.SetPaused(true);
            presentation._Process(10);
            game.SetPaused(false);
            presentation._Process(0);
            if (sprite.Frame != 3 || !Mathf.IsEqualApprox(sprite.FrameProgress, .4f))
                return Fail("暂停恢复重播了成功作业");
            game.SetFarmCrop(farm, CropKind.Radish);
            presentation._Process(0);
            if (!sprite.Animation.ToString().StartsWith("idle_")) return Fail("改种后旧动作没有取消");
            game.AdvanceTick();
            presentation._Process(0);
            game.AdvanceTick();
            presentation._Process(0);
            if (!sprite.Animation.ToString().StartsWith("water_")) return Fail("真正供水没有合成动作");
            game.SetPaused(true);
            game.RemoveBuilding(farm);
            game.TryPlace(farm, BuildingKind.Farm, CropKind.Radish);
            presentation._Process(0);
            if (!sprite.Animation.ToString().StartsWith("idle_") || sprite.IsPlaying())
                return Fail("暂停拆改后仍向新实例播旧浇水");
            game.SetPaused(false);
            game.AdvanceTicks(5);
            presentation._Process(0);
            if (!sprite.Animation.ToString().StartsWith("idle_")) return Fail("快速批量结束补播过期动作");
            game.SetFarmCrop(farm, CropKind.Radish);
            game.AdvanceTick();
            var fresh = new WorkerPresentation();
            map.AddChild(fresh);
            fresh.SetGame(game, map);
            fresh.SetProcess(false);
            fresh._Process(0);
            if (!fresh.GetNode<AnimatedSprite2D>("Worker1/AnimatedSprite2D").Animation.ToString().StartsWith("idle_"))
                return Fail("初次挂接重播了之前的作业结果");
            return true;
        }
        finally { parent.RemoveChild(map); map.Free(); }
    }

    private static async Task<bool> CheckCompleteActionClips(Node parent)
    {
        foreach (bool sow in new[] { true, false })
        {
            var game = new FarmGame(12345);
            foreach (var space in game.GetBuildingSpaces()) game.RemoveBuilding(space.AnchorCell);
            Vector2I farm = new(189, 189);
            game.TryPlace(farm, BuildingKind.Farm, CropKind.Radish);
            var driver = new SimulationDriver();
            driver.SetDevelopmentRate(2);
            var presentation = CreatePresentation(parent, game, out WorldMap map, driver);
            try
            {
                var character = presentation.GetNode<NpcCharacter>("Worker1");
                var sprite = character.GetNode<AnimatedSprite2D>("AnimatedSprite2D");
                game.AdvanceTick(isRaining: sow);
                if (!sow) game.AdvanceTick();
                presentation._Process(0);
                string expected = sow ? "sow_down" : "water_down";
                if (sprite.Animation != expected || sprite.Frame != 0)
                    return Fail("完整片段验证没有从真正成功动作首帧开始");
                bool crossed = false;
                ulong start = Time.GetTicksMsec();
                while (!character.WorkAnimationFinished && Time.GetTicksMsec() - start < 5000)
                {
                    await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
                    // 真实引擎播放到末两帧时推进经营秒，不能因结果窗口更新截掉动作结尾。
                    if (!crossed && sprite.Frame >= 6 && !character.WorkAnimationFinished)
                    {
                        int currentFrame = sprite.Frame;
                        game.AdvanceTick();
                        presentation._Process(0);
                        crossed = true;
                        if (sprite.Animation != expected || sprite.Frame != currentFrame || !sprite.IsPlaying())
                            return Fail("新经营秒截断了已开始的成功动作尾帧");
                    }
                }
                if (!crossed || !character.WorkAnimationFinished || sprite.Frame != 7)
                    return Fail("成功动作没有跨经营秒自然播至 AnimationFinished");
                presentation._Process(0);
                if (sprite.Animation != "idle_down") return Fail("自然结束没有返回独立待机");
                // 同一过期记录不能被开发高倍率补播或保留。
                game.SetFarmCrop(farm, CropKind.Radish);
                game.AdvanceTick(isRaining: true);
                driver.SetDevelopmentRate(20);
                presentation._Process(0);
                if (sprite.Animation != "sow_down") return Fail("高倍率未呈现最新有效成功结果");
                game.AdvanceTick();
                presentation._Process(0);
                if (sprite.Animation != "idle_down") return Fail("高倍率没有跳过过期动作");
                driver.SetDevelopmentRate(2);
                game.SetFarmCrop(farm, CropKind.Radish);
                game.AdvanceTick(isRaining: true);
                presentation._Process(0);
                game.TryPlace(new Vector2I(186, 189), BuildingKind.Farm, CropKind.Radish);
                game.AdvanceTick();
                presentation._Process(.1);
                if (!sprite.Animation.ToString().StartsWith("run_"))
                    return Fail("真实移动到新目标时仍播放上一处成功作业");
            }
            finally { parent.RemoveChild(map); map.Free(); }
        }
        return true;
    }

    private static bool Fail(string message)
    {
        GD.PushError(message);
        return false;
    }
}
