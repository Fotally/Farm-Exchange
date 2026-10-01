using Godot;
using FarmExchange.Characters;
using FarmExchange.UI;

public partial class TestNpcPreview : Node
{
    public override void _Ready()
    {
        bool passed = RunChecks(this);
        if (passed)
            GD.Print("集成测试：NPC 动画与角色切换通过");
        GetTree().Quit(passed ? 0 : 1);
    }

    public static bool RunChecks(Node parent)
    {
        var scene = GD.Load<PackedScene>("res://scenes/npc_preview.tscn");
        NpcPreview preview = scene.Instantiate<NpcPreview>();
        parent.AddChild(preview);
        try
        {
            var character = preview.GetNode<NpcCharacter>("NpcCharacter");
            var sprite = character.GetNode<AnimatedSprite2D>("AnimatedSprite2D");
            var label = preview.GetNode<Label>("CanvasLayer/CharacterName");
            SpriteFrames frames = sprite.SpriteFrames;
            if (!frames.HasAnimation("run_down") || !frames.HasAnimation("run_left") ||
                !frames.HasAnimation("run_up") || frames.GetAnimationNames().Length != 3)
                return Fail("NPC 三方向动画未装配");
            foreach (StringName animation in frames.GetAnimationNames())
            {
                if (frames.GetFrameCount(animation) != 6 ||
                    frames.GetAnimationSpeed(animation) != 8f ||
                    frames.GetAnimationLoopMode(animation) != SpriteFrames.LoopMode.Linear)
                    return Fail("NPC 动画帧数、帧率或循环方式错误");
            }
            if (!FrameStartsAt(frames, "run_down", 448) ||
                !FrameStartsAt(frames, "run_left", 512) ||
                !FrameStartsAt(frames, "run_up", 576))
                return Fail("NPC 动画读取了错误的图集行");

            character.SetMoveDirection(Vector2.Right);
            if (sprite.Animation != "run_left" || !sprite.FlipH)
                return Fail("向右移动没有镜像左移动动画");
            character.SetMoveDirection(Vector2.Zero);
            if (sprite.Animation != "run_left" || !sprite.FlipH || sprite.Frame != 0)
                return Fail("停步没有保留朝向或回到首帧");
            character.SetMoveDirection(Vector2.Up);
            if (sprite.Animation != "run_up" || sprite.FlipH)
                return Fail("向上移动动画错误");
            character.SetMoveDirection(Vector2.Down);
            if (sprite.Animation != "run_down" || sprite.FlipH)
                return Fail("向下移动动画错误");

            for (int index = 0; index < 20; index++)
            {
                if (!label.Text.StartsWith($"{index + 1} / 20  ") ||
                    frames.GetFrameTexture("run_down", 0) is not AtlasTexture atlas || atlas.Atlas == null)
                    return Fail("NPC 角色图未按顺序载入");
                SendKey(preview, Key.E);
            }
            if (!label.Text.Contains("npc_animation_001.png"))
                return Fail("E 键没有从最后一位循环到第一位");
            SendKey(preview, Key.Q);
            if (!label.Text.Contains("npc_animation_023.png"))
                return Fail("Q 键没有从第一位循环到最后一位");
            character.ShowAt(new Vector2(10, 20), Vector2.Right, moving: true, paused: false);
            if (sprite.Animation != "run_left" || !sprite.FlipH || !sprite.IsPlaying())
                return Fail("外部位置模式未更新朝向和移动动画");
            sprite.Frame = 2;
            character.ShowAt(character.Position, Vector2.Up, moving: true, paused: true);
            character._PhysicsProcess(1);
            if (character.Position != new Vector2(10, 20) || sprite.Frame != 2 ||
                sprite.Animation != "run_left" || sprite.IsPlaying() || character.IsPhysicsProcessing())
                return Fail("外部位置模式继续物理移动或暂停未保留动画帧");
            character.ShowAt(character.Position, Vector2.Up, moving: false, paused: false);
            if (sprite.Animation != "run_up" || sprite.Frame != 0 || sprite.IsPlaying())
                return Fail("外部模式停步未保留目标朝向的首帧");
            return true;
        }
        finally
        {
            parent.RemoveChild(preview);
            preview.Free();
        }
    }

    private static bool FrameStartsAt(SpriteFrames frames, StringName animation, int y)
    {
        if (frames.GetFrameTexture(animation, 0) is not AtlasTexture first ||
            frames.GetFrameTexture(animation, 5) is not AtlasTexture last)
            return false;
        return first.Region == new Rect2(0, y, 64, 64) &&
            last.Region == new Rect2(320, y, 64, 64);
    }

    private static void SendKey(NpcPreview preview, Key key)
    {
        preview._UnhandledInput(new InputEventKey { PhysicalKeycode = key, Pressed = true });
    }

    private static bool Fail(string message)
    {
        GD.PushError(message);
        return false;
    }
}
