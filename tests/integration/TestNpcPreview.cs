using Godot;
using FarmExchange.Characters;
using FarmExchange.UI;

public partial class TestNpcPreview : Node
{
    public override void _Ready() => GetTree().Quit(RunChecks(this) ? 0 : 1);

    public static bool RunChecks(Node parent)
    {
        var preview = GD.Load<PackedScene>("res://scenes/npc_preview.tscn").Instantiate<NpcPreview>();
        parent.AddChild(preview);
        try
        {
            var character = preview.GetNode<NpcCharacter>("NpcCharacter");
            var sprite = character.GetNode<AnimatedSprite2D>("AnimatedSprite2D");
            var label = preview.GetNode<Label>("CanvasLayer/CharacterName");
            string[] directions = { "down", "left", "up", "right" };
            (string Name, double[] Durations)[] actions =
            {
                ("idle", new[] { .28, .14, .14, .18, .28, .10, .12, .28 }),
                ("walk", new[] { .125, .125, .125, .125, .125, .125 }),
                ("run", new[] { 1d/12, 1d/12, 1d/12, 1d/12, 1d/12, 1d/12 }),
                ("sow", new[] { .16, .12, .12, .14, .14, .12, .12, .16 }),
                ("water", new[] { .16, .12, .12, .18, .18, .18, .12, .16 }),
            };
            for (int index = 0; index < 20; index++)
            {
                SpriteFrames frames = sprite.SpriteFrames;
                if (!label.Text.StartsWith($"{index + 1} / 20  ") ||
                    frames.GetAnimationNames().Length != 20 || sprite.Position != new Vector2(0, -28))
                    return Fail("20位角色目录、四向五动作或(32,60)脚根不一致");
                foreach (var action in actions)
                    for (int row = 0; row < directions.Length; row++)
                    {
                        StringName clip = $"{action.Name}_{directions[row]}";
                        if (frames.GetFrameCount(clip) != action.Durations.Length ||
                            frames.GetAnimationSpeed(clip) != 1 ||
                            (frames.GetAnimationLoopMode(clip) == SpriteFrames.LoopMode.Linear) !=
                                (action.Name is "idle" or "walk" or "run"))
                            return Fail("动作帧数或一次播放/循环约定错误");
                        for (int col = 0; col < action.Durations.Length; col++)
                        {
                            string evidence = $"角色资源={frames.ResourcePath} 动作方向={clip} 帧={col}";
                            float duration = frames.GetFrameDuration(clip, col);
                            // Godot 的逐帧时长接口返回 Single，按相同表示严格比较来源秒数，不使用额外容差。
                            float expectedDuration = (float)action.Durations[col];
                            if (duration != expectedDuration)
                                return Fail($"逐帧时长不符：{evidence} 来源秒={action.Durations[col]:R} 预期Single={expectedDuration:R} 实际Single={duration:R}");
                            if (frames.GetFrameTexture(clip, col) is not AtlasTexture atlas)
                                return Fail($"帧纹理不是 AtlasTexture：{evidence}");
                            Rect2 region = new(col * 64, row * 64, 64, 64);
                            if (atlas.Region != region)
                                return Fail($"图集切片不符：{evidence} 预期={region} 实际={atlas.Region}");
                            Vector2 size = new(action.Durations.Length * 64, 256);
                            if (atlas.Atlas.GetSize() != size)
                                return Fail($"图集尺寸不符：{evidence} 纹理={atlas.Atlas.ResourcePath} 预期={size} 实际={atlas.Atlas.GetSize()}");
                        }
                    }
                SendKey(preview, Key.E);
            }
            if (!label.Text.Contains("npc001")) return Fail("E切换未循环回首位");
            SendKey(preview, Key.Q);
            if (!label.Text.Contains("npc023")) return Fail("Q切换未循环到末位");
            character.SetMoveDirection(Vector2.Right);
            if (sprite.Animation != "run_right" || sprite.FlipH) return Fail("右向未使用烘焙的第四行");
            character.SetMoveDirection(Vector2.Zero);
            if (sprite.Animation != "idle_right" || !sprite.IsPlaying()) return Fail("停步没有独立待机动作");
            character.SetMoveDirection(Vector2.Left, NpcAction.Walk);
            if (sprite.Animation != "walk_left") return Fail("预览行走动作错误");
            character.SetMoveDirection(Vector2.Up);
            if (sprite.Animation != "run_up") return Fail("向上动画错误");
            character.SetMoveDirection(Vector2.Down);
            if (sprite.Animation != "run_down") return Fail("向下动画错误");
            foreach (Key key in new[] { Key.Key1, Key.Key2, Key.Key3, Key.Key4, Key.Key5 }) SendKey(preview, key);
            preview._PhysicsProcess(0);
            if (sprite.Animation != "water_down") return Fail("预览动作键未连接合成浇水");
            character.ShowAt(new Vector2(10, 20), Vector2.Right, true, false);
            sprite.SetFrameAndProgress(2, .4f);
            character.ShowAt(character.Position, Vector2.Up, true, true);
            character._PhysicsProcess(1);
            if (character.Position != new Vector2(10, 20) || sprite.Frame != 2 ||
                !Mathf.IsEqualApprox(sprite.FrameProgress, .4f) || sprite.Animation != "run_right" ||
                sprite.IsPlaying() || character.IsPhysicsProcessing())
                return Fail("外部位置模式继续移动，或暂停丢失帧进度");
            character.ShowAt(character.Position, Vector2.Up, false, false, workAction: NpcAction.Sow, restartAction: true);
            sprite.SetFrameAndProgress(3, .5f);
            character.ShowAt(character.Position, Vector2.Up, false, false, workAction: NpcAction.Sow);
            if (sprite.Animation != "sow_up" || sprite.Frame != 3 || !Mathf.IsEqualApprox(sprite.FrameProgress, .5f))
                return Fail("同一成功作业被每帧调用重播");
            character.ShowAt(character.Position, Vector2.Zero, false, false);
            if (sprite.Animation != "idle_up") return Fail("作业结束未返回原朝向待机");
            return true;
        }
        finally { parent.RemoveChild(preview); preview.Free(); }
    }

    private static void SendKey(NpcPreview preview, Key key) =>
        preview._UnhandledInput(new InputEventKey { PhysicalKeycode = key, Pressed = true });
    private static bool Fail(string message) { GD.PushError(message); return false; }
}
