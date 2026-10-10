using System;
using FarmExchange.Trading;

namespace FarmExchange.Logging;

/**
 * <summary>关联订单命令与其真实结果，不执行、重试或改变业务。</summary>
 */
public sealed class OrderLogOperation
{
    private readonly TradeOrderLog _log;
    private readonly CommandObservation _command;
    private readonly OrderCommandObservation _before;

    internal OrderLogOperation(TradeOrderLog log, CommandObservation command, OrderCommandObservation before)
    { _log = log; _command = command; _before = before; }

    /**
     * <summary>记录原订单命令的真实结果；重复终结不重复记录。</summary>
     * <param name="result">业务实际返回的成功或拒绝。</param>
     */
    public void Complete(TradeOrderCommandResult result) => _log.Completed(_command, _before, result);

    /**
     * <summary>关联原异常并终结观察，调用方继续传播原异常。</summary>
     * <param name="error">真实业务异常。</param>
     */
    public void Faulted(Exception error) => _command.Faulted(error);
}
