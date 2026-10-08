using FarmExchange.Gameplay;
using FarmExchange.Time;

namespace FarmExchange.Logging;

/**
 * <summary>观察实际暂停状态与已接受的倍率选择，不拥有时间推进。</summary>
 */
public sealed class TimeLog
{
    private readonly GameLog _context;
    private readonly FarmGame _game;
    private static readonly CommandDescription PauseCommand = new("SetPaused", "FarmExchange.Gameplay.FarmGame",
        "收到暂停指令", "暂停指令结束", "暂停设置抛出异常", "暂停指令异常结束");
    private static readonly CommandDescription RateCommand = new("SelectSimulationRate", "FarmExchange.Time.SimulationDriver",
        "收到倍率选择", "倍率选择结束", "倍率选择抛出异常", "倍率选择异常结束");
    internal static readonly LogEventDescriptor PauseChanged = new(70, "PauseChanged", "FarmExchange.Gameplay.FarmGame");
    internal static readonly LogEventDescriptor RateSelected = new(71, "SimulationRateSelected", "FarmExchange.Time.SimulationDriver");

    internal TimeLog(GameLog context, FarmGame game) { _context = context; _game = game; }

    /**
     * <summary>保存暂停原请求与修改前实际状态。</summary>
     * <param name="requested">希望设置的暂停状态。</param>
     * <param name="origin">实际指令来源。</param>
     * <returns>业务完成后终结的观察；采集关闭为 null。</returns>
     */
    public PauseLogOperation? BeginPause(bool requested, CommandOrigin origin = CommandOrigin.Player)
    {
        if (!_context.CanObserve) return null;
        var command = _context.NewCommand(PauseCommand, origin);
        bool previous = _game.IsPaused;
        _context.Observe(() => command.Received(new() { ["IsPaused"] = requested }));
        return new(command, previous);
    }

    /**
     * <summary>观察已通过原倍率校验的选择，包括同值选择。</summary>
     * <param name="requested">本次已接受倍率。</param>
     * <param name="previous">驱动修改前的真实倍率。</param>
     * <param name="source">实际玩家或流程来源。</param>
     * <returns>赋值后、发出倍率通知前终结的观察；采集关闭为 null。</returns>
     */
    public SimulationRateLogOperation? BeginRate(double requested, double previous, SimulationRateSource source)
    {
        if (!_context.CanObserve) return null;
        var command = _context.NewCommand(RateCommand, (CommandOrigin)source);
        _context.Observe(() => command.Received(new() { ["RequestedRate"] = requested, ["Source"] = source.ToString() }));
        return new(command, requested, previous, source);
    }
}
