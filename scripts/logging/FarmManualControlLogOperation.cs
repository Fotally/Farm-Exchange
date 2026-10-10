using System;

namespace FarmExchange.Logging;

/**
 * <summary>单田手动接管的一次观察，保留同种重启与已有轮次事实。</summary>
 */
public sealed class FarmManualControlLogOperation
{
    private readonly CultivationLog _owner;
    private readonly CommandObservation _command;
    private readonly ManualObservation? _before;

    internal FarmManualControlLogOperation(CultivationLog owner, CommandObservation command, ManualObservation? before)
    { _owner = owner; _command = command; _before = before; }

    /**
     * <summary>观察原手动命令的真实成功或拒绝，重复终结不重复输出。</summary>
     * <param name="error">成功为 null，拒绝保留原原因。</param>
     */
    public void Complete(string? error) => _owner.ManualCompleted(_command, _before, error);

    /**
     * <summary>关联原业务异常并结束观察，调用方继续传播。</summary>
     * <param name="error">实际抛出的原异常。</param>
     */
    public void Faulted(Exception error) => _command.Faulted(error);
}
