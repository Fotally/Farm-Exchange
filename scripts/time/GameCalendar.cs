namespace FarmExchange.Time;

public enum Season { Spring, Summer, Autumn, Winter }

public readonly record struct CalendarSnapshot(
    uint ElapsedSeconds, uint ElapsedDays, int Year, int Month, int Day,
    int DayOfWeek, Season Season, bool IsPaused);

public sealed class GameCalendar
{
    private const uint DaysPerMonth = 28;
    private const uint DaysPerYear = 336;
    private uint _elapsedSeconds;

    public bool IsPaused { get; private set; }

    public GameCalendar(uint elapsedSeconds = 0) => _elapsedSeconds = elapsedSeconds;

    public CalendarSnapshot Snapshot
    {
        get
        {
            uint elapsedDays = (uint)((ulong)_elapsedSeconds * GameTimeUnits.PerSecond /
                GameTimeUnits.PerDay);
            GameDate date = GetDate(elapsedDays);
            return new CalendarSnapshot(
                _elapsedSeconds, elapsedDays, date.Year, date.Month, date.Day,
                (int)(elapsedDays % 7) + 1, date.Season, IsPaused);
        }
    }

    public static GameDate GetDate(uint elapsedDays)
    {
        uint dayOfYear = elapsedDays % DaysPerYear;
        int monthIndex = (int)(dayOfYear / DaysPerMonth);
        return new GameDate(elapsedDays, (int)(elapsedDays / DaysPerYear) + 1,
            monthIndex + 1, (int)(dayOfYear % DaysPerMonth) + 1, (Season)(monthIndex / 3));
    }

    public void SetPaused(bool paused) => IsPaused = paused;

    /**
     * <summary>查询下一季节起点的完整经营秒距离。</summary>
     * <returns>严格晚于当前时刻的季节边界距离。</returns>
     */
    internal uint SecondsUntilNextSeason => 4320u - _elapsedSeconds % 4320u;

    /**
     * <summary>将未来游戏日起点转换为首个到达它的经营秒距离。</summary>
     * <param name="elapsedDay">严格晚于当前游戏日的累计日数。</param>
     * <returns>按七比三百六十整数比例向上取整的秒距离。</returns>
     */
    internal uint SecondsUntilDay(uint elapsedDay) => checked((uint)(
        ((ulong)elapsedDay * GameTimeUnits.PerDay + GameTimeUnits.PerSecond - 1) /
        GameTimeUnits.PerSecond - _elapsedSeconds));

    public bool TryAdvanceSeconds(uint seconds)
    {
        if (IsPaused)
            return true;
        if (seconds > uint.MaxValue - _elapsedSeconds)
            return false;
        _elapsedSeconds += seconds;
        return true;
    }
}
