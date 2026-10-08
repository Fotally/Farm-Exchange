using System;

namespace FarmExchange.Logging;

/**
 * <summary>一次暂停设置的观察，只有实际变化才生成状态事件。</summary>
 */
public sealed class PauseLogOperation
{
    private readonly CommandObservation _command;
    private readonly bool _previous;
    internal PauseLogOperation(CommandObservation command, bool previous) { _command = command; _previous = previous; }

    /**
     * <summary>记录实际设置结果并结束命令；重复终结无副作用。</summary>
     * <param name="effective">设置后的真实暂停状态。</param>
     */
    public void Complete(bool effective) => _command.Complete(fields =>
    {
        fields["IsPaused"] = effective;
        return new(_previous != effective ? TimeLog.PauseChanged : null, "经营暂停状态变化", true, null);
    });

    /**
     * <summary>观察原异常；调用方保留原传播方式。</summary>
     * <param name="error">业务原异常。</param>
     */
    public void Faulted(Exception error) => _command.Faulted(error);
}
