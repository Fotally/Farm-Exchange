using System.Collections.Generic;
using Godot;

namespace FarmExchange.Characters;

public partial class NpcCharacter : CharacterBody2D
{
    private const int FrameSize = 64;
    private const int FramesPerDirection = 6;
    private const float AnimationSpeed = 8f;
    private const float MoveSpeed = 180f;

    [Export] public Texture2D CharacterSheet { get; set; } = null!;

    private readonly List<AtlasTexture> _atlasFrames = new(18);
    private AnimatedSprite2D _sprite = null!;
    private Vector2 _moveDirection;
    private Vector2 _facingDirection = Vector2.Down;
    private bool _usesExternalPosition;

    public override void _Ready()
    {
        _sprite = GetNode<AnimatedSprite2D>("AnimatedSprite2D");
        var frames = new SpriteFrames();
        frames.RemoveAnimation("default");
        AddDirection(frames, "run_down", 7);
        AddDirection(frames, "run_left", 8);
        AddDirection(frames, "run_up", 9);
        _sprite.SpriteFrames = frames;
        UpdateAnimation(Vector2.Zero, moving: false);
    }

    public void SetCharacter(Texture2D sheet)
    {
        CharacterSheet = sheet;
        foreach (AtlasTexture frame in _atlasFrames)
            frame.Atlas = sheet;
    }

    public void SetMoveDirection(Vector2 direction)
    {
        _moveDirection = direction.LimitLength();
        UpdateAnimation(_moveDirection, _moveDirection != Vector2.Zero);
    }

    public void ShowAt(Vector2 localPosition, Vector2 direction, bool moving, bool paused)
    {
        _usesExternalPosition = true;
        SetPhysicsProcess(false);
        CollisionLayer = 0;
        CollisionMask = 0;
        Position = localPosition;
        if (paused)
            _sprite.Pause();
        else
            UpdateAnimation(direction, moving);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_usesExternalPosition)
            return;
        Velocity = _moveDirection * MoveSpeed;
        MoveAndSlide();
    }

    private void AddDirection(SpriteFrames frames, StringName name, int row)
    {
        frames.AddAnimation(name);
        frames.SetAnimationSpeed(name, AnimationSpeed);
        frames.SetAnimationLoopMode(name, SpriteFrames.LoopMode.Linear);
        for (int column = 0; column < FramesPerDirection; column++)
        {
            var frame = new AtlasTexture
            {
                Atlas = CharacterSheet,
                Region = new Rect2(column * FrameSize, row * FrameSize, FrameSize, FrameSize),
            };
            _atlasFrames.Add(frame);
            frames.AddFrame(name, frame);
        }
    }

    private void UpdateAnimation(Vector2 direction, bool moving)
    {
        if (direction != Vector2.Zero)
            _facingDirection = direction;

        StringName animation = "run_down";
        _sprite.FlipH = false;
        if (Mathf.Abs(_facingDirection.X) > Mathf.Abs(_facingDirection.Y))
        {
            animation = "run_left";
            _sprite.FlipH = _facingDirection.X > 0f;
        }
        else if (_facingDirection.Y < 0f)
            animation = "run_up";

        if (_sprite.Animation != animation)
            _sprite.Play(animation);
        if (!moving)
        {
            _sprite.Pause();
            _sprite.Frame = 0;
        }
        else
            _sprite.Play();
    }
}
