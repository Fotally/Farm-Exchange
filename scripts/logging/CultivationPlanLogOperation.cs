using System;
using FarmExchange.Cultivation;

namespace FarmExchange.Logging;

/**
 * <summary>年度表保存或删除的一次观察关联，不执行耕作命令。</summary>
 */
public sealed class CultivationPlanLogOperation
{
    private readonly CultivationLog _owner;
    private readonly CommandObservation _command;
    private readonly PlanObservation? _before;

    internal CultivationPlanLogOperation(CultivationLog owner, CommandObservation command, PlanObservation? before)
    { _owner = owner; _command = command; _before = before; }

    /**
     * <summary>观察真实创建或更新结果，重复终结不重复输出。</summary>
     * <param name="result">原命令实际返回结果。</param>
     */
    public void Complete(CultivationCommandResult result) => _owner.PlanCompleted(_command, _before, result);

    /**
     * <summary>观察实际删除结果，成功后读取已解除的引用事实。</summary>
     * <param name="error">成功为 null，拒绝保留原原因。</param>
     */
    public void Complete(string? error) => Complete(new CultivationCommandResult(_before?.Id ?? 0, error));

    /**
     * <summary>关联原业务异常并结束观察，调用方继续传播。</summary>
     * <param name="error">实际抛出的原异常。</param>
     */
    public void Faulted(Exception error) => _command.Faulted(error);
}
