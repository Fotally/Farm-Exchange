using System;
using System.Collections.Generic;
using FarmExchange.Land;

namespace FarmExchange.Logging;

/**
 * <summary>保存一次整批道路操作的命令关联，记录真实结果且不执行业务。</summary>
 */
public sealed class RoadLogOperation
{
    private readonly GameplayLog _owner;
    private readonly CommandObservation _command;
    private readonly RoadObservation? _before;

    internal RoadLogOperation(GameplayLog owner, CommandObservation command, RoadObservation? before)
    { _owner = owner; _command = command; _before = before; }

    /**
     * <summary>在整批道路建造结束后记录实际格数与扣费。</summary>
     * <param name="result">真实执行结果，不使用预检代替。</param>
     */
    public void Complete(RoadPlacementResult result) =>
        _owner.RoadsCompleted(_command, _before, result.Failure, result.CellCount, result.ChargedCents, null);

    /**
     * <summary>在范围拆路结束后记录实际移除目标与零退款。</summary>
     * <param name="result">真实执行结果。</param>
     * <param name="removed">实际拆除道路的原不可变快照，不重新推断范围。</param>
     */
    public void Complete(RoadRemovalResult result, IReadOnlyList<BuildingSpaceSnapshot> removed) =>
        _owner.RoadsCompleted(_command, _before, result.Failure, result.RemovedCount, 0, removed);

    /**
     * <summary>一次记录原异常并终结命令，调用方保持原传播。</summary>
     * <param name="error">原业务异常。</param>
     */
    public void Faulted(Exception error) => _command.Faulted(error);
}
