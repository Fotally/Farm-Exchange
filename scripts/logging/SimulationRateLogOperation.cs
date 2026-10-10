using System;
using FarmExchange.Time;

namespace FarmExchange.Logging;

/**
 * <summary>一次已接受倍率选择的原请求和实际前后值。</summary>
 */
public sealed class SimulationRateLogOperation
{
    private readonly CommandObservation _command;
    private readonly double _requested;
    private readonly double _previous;
    private readonly SimulationRateSource _source;
    internal SimulationRateLogOperation(CommandObservation command, double requested, double previous, SimulationRateSource source)
    { _command = command; _requested = requested; _previous = previous; _source = source; }

    /**
     * <summary>记录已接受选择；同值仍输出且不解除暂停。</summary>
     * <param name="effective">驱动实际生效倍率。</param>
     */
    public void Complete(double effective) => _command.Complete(fields =>
    {
        fields["Source"] = _source.ToString();
        fields["RequestedRate"] = _requested;
        fields["PreviousRate"] = _previous;
        fields["EffectiveRate"] = effective;
        fields["ValueChanged"] = _previous != effective;
        return new(TimeLog.RateSelected, "经营倍率已选择", true, null);
    });

    /**
     * <summary>观察原异常；调用方保留原传播方式。</summary>
     * <param name="error">业务原异常。</param>
     */
    public void Faulted(Exception error) => _command.Faulted(error);
}
