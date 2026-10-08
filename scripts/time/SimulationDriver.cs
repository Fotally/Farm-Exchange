using System;
using FarmExchange.Gameplay;
using FarmExchange.Logging;

namespace FarmExchange.Time;

/**
 * <summary>把现实帧时间按倍率转换为同一经营对象的完整批量 tick。</summary>
 * <remarks>每局由宿主保存唯一实例；暂停与改速保留未完成 tick，暂停期间不累计。</remarks>
 */
public sealed class SimulationDriver
{
    private double _progress;
    private readonly TimeLog? _logging;

    /**
     * <summary>为一局组装唯一驱动及可选时间观察。</summary>
     * <param name="logging">同一经营局的时间日志入口；省略时不采集。</param>
     */
    public SimulationDriver(TimeLog? logging = null) => _logging = logging;
    /**
     * <summary>当前每现实秒推进的经营 tick 数。</summary>
     */
    public double Rate { get; private set; } = 1;
    /**
     * <summary>已积累但未完成的经营 tick 小数进度。</summary>
     */
    public double Progress => _progress;

    /**
     * <summary>通知实际倍率及发起来源；玩家重复选择同一倍率也发出主动操作通知。</summary>
     */
    public event Action<double, SimulationRateSource>? RateChanged;

    /**
     * <summary>检查发布版本允许的倍率。</summary>
     * <param name="rate">经营 tick / 现实秒。</param>
     * <returns>仅 0.5、1、2 返回 true。</returns>
     */
    public static bool IsPublicRateAllowed(double rate) => rate is 0.5 or 1 or 2;

    /**
     * <summary>设置公共经营倍率，保留未完成 tick。</summary>
     * <param name="rate">0.5、1 或 2。</param>
     * <param name="source">发起来源。</param>
     */
    public void SetRate(double rate, SimulationRateSource source = SimulationRateSource.Player)
    {
        if (!IsPublicRateAllowed(rate)) throw new ArgumentOutOfRangeException(nameof(rate));
        ChangeRate(rate, source);
    }

#if DEBUG
    /**
     * <summary>检查开发版本允许的公共倍率或有限正整数倍率。</summary>
     * <param name="rate">经营 tick / 现实秒。</param>
     * <returns>符合开发倍率规则时返回 true。</returns>
     */
    public static bool IsDevelopmentRateAllowed(double rate) => IsPublicRateAllowed(rate) ||
        double.IsFinite(rate) && rate > 0 && rate == Math.Truncate(rate);

    /**
     * <summary>在开发构建设置额外倍率，保留未完成 tick。</summary>
     * <param name="rate">公共倍率或有限正整数。</param>
     * <param name="source">发起来源。</param>
     */
    public void SetDevelopmentRate(double rate, SimulationRateSource source = SimulationRateSource.Player)
    {
        if (!IsDevelopmentRateAllowed(rate)) throw new ArgumentOutOfRangeException(nameof(rate));
        ChangeRate(rate, source);
    }
#endif

    private void ChangeRate(double rate, SimulationRateSource source)
    {
        SimulationRateLogOperation? observation = _logging?.BeginRate(rate, Rate, source);
        Rate = rate;
        observation?.Complete(Rate);
        RateChanged?.Invoke(rate, source);
    }

    /**
     * <summary>按现实帧时间推进经营，在真实事件及请求终点交给宿主观察。</summary>
     * <param name="delta">有限非负现实秒数。</param>
     * <param name="game">本驱动唯一绑定的经营对象。</param>
     * <param name="checkpoint">稳定检查点；返回 false 结束本段，随后重新读取限额与倍率。</param>
     * <param name="maxTicks">宿主允许的下一段完整 tick 数；用于日期与流程预算。</param>
     * <returns>本帧实际完成 tick 数。</returns>
     */
    public uint Advance(double delta, FarmGame game, Func<SimulationCheckpoint, bool>? checkpoint = null,
        Func<uint>? maxTicks = null)
    {
        if (!double.IsFinite(delta) || delta < 0) throw new ArgumentOutOfRangeException(nameof(delta));
        if (game.IsPaused) return 0;
        double remainingSeconds = delta;
        uint advanced = 0;
        while (!game.IsPaused)
        {
            double startRate = Rate;
            double progress = _progress + remainingSeconds * startRate;
            if (!double.IsFinite(progress) || progress > uint.MaxValue)
                throw new InvalidOperationException("本帧请求的经营 tick 超出容量。");
            uint ticks = (uint)Math.Floor(progress);
            if (ticks == 0)
            {
                _progress = progress;
                break;
            }
            if (maxTicks != null) ticks = Math.Min(ticks, maxTicks());
            if (ticks == 0) break;
            SimulationAdvanceResult result = game.AdvanceTicks(ticks, point =>
            {
                bool keepGoing = checkpoint?.Invoke(point) ?? true;
                return keepGoing && Rate == startRate && !game.IsPaused;
            });
            if (result.AdvancedTicks == 0) break;
            remainingSeconds = Math.Max(0, remainingSeconds -
                (result.AdvancedTicks - _progress) / startRate);
            _progress = 0;
            advanced = checked(advanced + result.AdvancedTicks);
            // 检查点可能建单、换速或结束流程，下一段重新读取预算与倍率。
        }
        return advanced;
    }
}
