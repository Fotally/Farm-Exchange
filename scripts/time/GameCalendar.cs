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
            uint dayOfYear = elapsedDays % DaysPerYear;
            int monthIndex = (int)(dayOfYear / DaysPerMonth);
            return new CalendarSnapshot(
                _elapsedSeconds, elapsedDays, (int)(elapsedDays / DaysPerYear) + 1,
                monthIndex + 1, (int)(dayOfYear % DaysPerMonth) + 1,
                (int)(elapsedDays % 7) + 1, (Season)(monthIndex / 3), IsPaused);
        }
    }

    public void SetPaused(bool paused) => IsPaused = paused;

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
