using System;
using Microsoft.Extensions.Logging;

namespace FarmExchange.Logging;

/**
 * <summary>观察一次同步推进的实际阶段和异常；释放仅解除调用关联。</summary>
 * <remarks>调用方使用 using，在实际步骤前标记阶段；异常交给 Faulted 后仍由业务原样传播。</remarks>
 */
public sealed class SimulationLogOperation : IDisposable
{
    private readonly GameLog _context;
    private readonly ExceptionObservation _observation;
    private readonly LogEventDescriptor _exceptionEvent;
    private readonly string _message;
    private SimulationLogPhase _phase;
    private bool _faulted;
    private bool _disposed;

    internal SimulationLogOperation(GameLog context, string source, string message, SimulationLogPhase phase)
    {
        _context = context;
        _message = message;
        _phase = phase;
        _exceptionEvent = new(8, "BusinessException", source, LogLevel.Error);
        _observation = context.BeginExceptionScope();
    }

    /**
     * <summary>标记即将实际执行的推进阶段，不输出成功事件。</summary>
     * <param name="phase">对应真实执行位置的具名阶段。</param>
     */
    public void EnterPhase(SimulationLogPhase phase) => _phase = phase;

    /**
     * <summary>观察本次推进原异常，沿活跃父调用传播时只提交最内层记录。</summary>
     * <param name="error">业务捕获的原异常；记录不消费、包装或重抛。</param>
     */
    public void Faulted(Exception error)
    {
        if (_disposed || _faulted) return;
        _faulted = true;
        _observation.Faulted(error, _exceptionEvent, _message, () => _context.Context(_phase.ToString()));
    }

    /**
     * <summary>幂等清理本次传播凭据，不推断成功或记录新的终结事实。</summary>
     */
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _observation.Close();
        _context.EndExceptionScope();
    }
}
