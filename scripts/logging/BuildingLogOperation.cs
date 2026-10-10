using System;
using Godot;
using FarmExchange.Land;

namespace FarmExchange.Logging;

/**
 * <summary>保存一次建造或拆除的观察关联，不执行经营命令。</summary>
 */
public sealed class BuildingLogOperation
{
    private readonly GameplayLog _owner;
    private readonly CommandObservation _command;
    private readonly Vector2I _cell;
    private readonly BuildingObservation? _before;

    internal BuildingLogOperation(GameplayLog owner, CommandObservation command, Vector2I cell, BuildingObservation? before)
    { _owner = owner; _command = command; _cell = cell; _before = before; }

    /**
     * <summary>在原建造流程完整完成后记录真实放置结果；重复终结无副作用。</summary>
     * <param name="result">本次真实建造返回值，不能使用预检替代。</param>
     */
    public void Complete(PlacementResult result) => _owner.BuildingCompleted(_command, _cell, _before, result, result.ErrorMessage);

    /**
     * <summary>在原拆除完成后记录真实返回值；重复终结无副作用。</summary>
     * <param name="rejectionReason">成功为 null；拒绝保留原中文原因。</param>
     */
    public void Complete(string? rejectionReason) => _owner.BuildingCompleted(_command, _cell, _before, null, rejectionReason);

    /**
     * <summary>记录原业务异常并终结关联，不消费异常或重试。</summary>
     * <param name="error">原业务异常，调用方保持原传播。</param>
     */
    public void Faulted(Exception error) => _command.Faulted(error);
}
