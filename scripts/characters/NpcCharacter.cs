using Godot;

namespace FarmExchange.Characters;

public enum NpcAction { Idle, Walk, Run, Sow, Water }

public partial class NpcCharacter : CharacterBody2D
{
    private const float MoveSpeed = 180f;
    [Export] public SpriteFrames CharacterFrames { get; set; } = null!;
    private AnimatedSprite2D _sprite = null!;
    private Vector2 _moveDirection;
    private Vector2 _facingDirection = Vector2.Down;
    private bool _usesExternalPosition;
    private bool _paused;
    private bool _workAnimationFinished;

    /**
     * <summary>当前单次作业片段已自然播完；只表示视觉完成，不推进经营。</summary>
     */
    public bool WorkAnimationFinished => _workAnimationFinished;

    public override void _Ready()
    {
        _sprite = GetNode<AnimatedSprite2D>("AnimatedSprite2D");
        _sprite.SpriteFrames = CharacterFrames;
        _sprite.AnimationFinished += () => _workAnimationFinished = true;
        UpdateAnimation(Vector2.Zero, NpcAction.Idle);
    }

    /**
     * <summary>选择已接入的角色，加载四向合成帧及逐帧时长。</summary>
     * <param name="characterId">来源目录中的角色编号；不连续编号按预览目录选择。</param>
     */
    public void SetCharacter(int characterId)
    {
        CharacterFrames = GD.Load<SpriteFrames>(
            $"res://assets/gameplay/npc/npc{characterId:D3}/npc{characterId:D3}_sprite_frames.tres");
        if (_sprite != null)
        {
            _sprite.SpriteFrames = CharacterFrames;
            UpdateAnimation(Vector2.Zero, NpcAction.Idle, restart: true);
        }
    }

    /**
     * <summary>独立预览按输入移动，并可检视指定合成动作。</summary>
     * <param name="direction">预览移动方向，长度限制为一。</param>
     * <param name="action">移动时的行走/跑步，或静态检视的播种/浇水。</param>
     */
    public void SetMoveDirection(Vector2 direction, NpcAction action = NpcAction.Run)
    {
        _moveDirection = direction.LimitLength();
        UpdateAnimation(direction, action is NpcAction.Sow or NpcAction.Water ? action :
            direction == Vector2.Zero ? NpcAction.Idle : action);
    }

    /**
     * <summary>按外部位置、朝向与真实动作显示角色，不推进自主移动或经营。</summary>
     * <param name="localPosition">脚根在父节点内的位置。</param>
     * <param name="direction">画面朝向；零向量保留前次朝向。</param>
     * <param name="moving">是否按真实位移播放跑步。</param>
     * <param name="paused">冻结当前帧及进度。</param>
     * <param name="animationRate">公共倍率；开发高倍率由调用方传一。</param>
     * <param name="workAction">本次有效成功作业；空值根据位移选择移动或待机。</param>
     * <param name="restartAction">首次消费新结果时从首帧播放一次。</param>
     */
    public void ShowAt(Vector2 localPosition, Vector2 direction, bool moving, bool paused,
        double animationRate = 1, NpcAction? workAction = null, bool restartAction = false)
    {
        _usesExternalPosition = true;
        SetPhysicsProcess(false);
        CollisionLayer = 0;
        CollisionMask = 0;
        Position = localPosition;
        _sprite.SpeedScale = (float)animationRate;
        if (paused)
        {
            _paused = true;
            _sprite.Pause();
            return;
        }
        UpdateAnimation(direction, workAction ?? (moving ? NpcAction.Run : NpcAction.Idle), restartAction);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_usesExternalPosition)
            return;
        Velocity = _moveDirection * MoveSpeed;
        MoveAndSlide();
    }

    private void UpdateAnimation(Vector2 direction, NpcAction action, bool restart = false)
    {
        if (direction != Vector2.Zero)
            _facingDirection = direction;
        string facing = Mathf.Abs(_facingDirection.X) > Mathf.Abs(_facingDirection.Y)
            ? (_facingDirection.X > 0 ? "right" : "left")
            : (_facingDirection.Y < 0 ? "up" : "down");
        StringName animation = $"{action.ToString().ToLowerInvariant()}_{facing}";
        _sprite.FlipH = false;
        if (_sprite.Animation != animation || restart)
        {
            _workAnimationFinished = false;
            _sprite.Play(animation);
            if (restart)
                _sprite.SetFrameAndProgress(0, 0);
        }
        else if (_paused)
            _sprite.Play();
        _paused = false;
    }
}
