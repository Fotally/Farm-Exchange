using System;
using Godot;
using FarmExchange.Characters;

namespace FarmExchange.UI;

public partial class NpcPreview : Node2D
{
    private static readonly Rect2 WorldRect = new(-960f, -540f, 1920f, 1080f);
    private static readonly int[] CharacterIds =
    {
        1, 2, 5, 6, 7, 9, 10, 11, 12, 13,
        14, 15, 16, 17, 18, 19, 20, 21, 22, 23,
    };

    private NpcCharacter _character = null!;
    private Camera2D _camera = null!;
    private Label _characterName = null!;
    private int _characterIndex;
    private NpcAction _previewAction = NpcAction.Run;

    public override void _Ready()
    {
        _character = GetNode<NpcCharacter>("NpcCharacter");
        _camera = GetNode<Camera2D>("Camera2D");
        _characterName = GetNode<Label>("CanvasLayer/CharacterName");
        ApplyCharacter();
        QueueRedraw();
    }

    public override void _PhysicsProcess(double delta)
    {
        Vector2 direction = Vector2.Zero;
        if (Input.IsPhysicalKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left))
            direction.X -= 1f;
        if (Input.IsPhysicalKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right))
            direction.X += 1f;
        if (Input.IsPhysicalKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up))
            direction.Y -= 1f;
        if (Input.IsPhysicalKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down))
            direction.Y += 1f;
        _character.SetMoveDirection(direction, _previewAction);
    }

    public override void _Process(double delta)
    {
        _camera.GlobalPosition = _character.GlobalPosition;
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventKey { Pressed: true, Echo: false } key)
            return;
        if (key.PhysicalKeycode == Key.Q)
            ChangeCharacter(-1);
        else if (key.PhysicalKeycode == Key.E)
            ChangeCharacter(1);
        else if (key.PhysicalKeycode is >= Key.Key1 and <= Key.Key5)
        {
            _previewAction = (NpcAction)((int)key.PhysicalKeycode - (int)Key.Key1);
            ApplyCharacter();
        }
        else
            return;
        GetViewport().SetInputAsHandled();
    }

    public override void _Draw()
    {
        DrawRect(WorldRect, new Color("182536"), true);
        for (int x = (int)WorldRect.Position.X; x <= WorldRect.End.X; x += 64)
            DrawLine(new Vector2(x, WorldRect.Position.Y), new Vector2(x, WorldRect.End.Y),
                new Color("23364a"), 1f);
        for (int y = (int)WorldRect.Position.Y; y <= WorldRect.End.Y; y += 64)
            DrawLine(new Vector2(WorldRect.Position.X, y), new Vector2(WorldRect.End.X, y),
                new Color("23364a"), 1f);
        DrawRect(WorldRect, new Color("5b7896"), false, 4f);
    }

    private void ChangeCharacter(int step)
    {
        _characterIndex = (_characterIndex + step + CharacterIds.Length) % CharacterIds.Length;
        ApplyCharacter();
    }

    private void ApplyCharacter()
    {
        int id = CharacterIds[_characterIndex];
        _character.SetCharacter(id);
        _characterName.Text = $"{_characterIndex + 1} / {CharacterIds.Length}  npc{id:D3}  {_previewAction}  1待机 2行走 3跑步 4播种 5浇水";
    }
}
