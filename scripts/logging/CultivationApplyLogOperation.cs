using System;
using System.Collections.Generic;

namespace FarmExchange.Logging;

/**
 * <summary>批量年度表应用观察，消费原验证循环真实完成的目标列表。</summary>
 */
public sealed class CultivationApplyLogOperation
{
    private readonly CultivationLog _owner;
    private readonly CommandObservation _command;
    private readonly ApplyObservation? _before;

    internal CultivationApplyLogOperation(CultivationLog owner, CommandObservation command, ApplyObservation? before)
    { _owner = owner; _command = command; _before = before; }

    /**
     * <summary>观察完整应用结果，目标只在本次同步投影中采样，不复制全量列表。</summary>
     * <param name="error">原命令实际错误；成功为 null。</param>
     * <param name="resolvedIndices">原验证已完整去重的锚点索引；提前拒绝为 null，已知空输入为空列表。</param>
     */
    public void Complete(string? error, IReadOnlyList<int>? resolvedIndices) =>
        _owner.ApplyCompleted(_command, _before, error, resolvedIndices);

    /**
     * <summary>关联原业务异常并结束观察，调用方继续传播。</summary>
     * <param name="error">实际抛出的原异常。</param>
     */
    public void Faulted(Exception error) => _command.Faulted(error);
}
