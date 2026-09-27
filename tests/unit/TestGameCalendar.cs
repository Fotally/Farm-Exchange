using Godot;
using FarmExchange.Time;

public partial class TestGameCalendar : Node
{
    public override void _Ready()
    {
        bool passed = RunChecks();
        if (passed)
            GD.Print("独立日历换算检查通过");
        GetTree().Quit(passed ? 0 : 1);
    }

    public static bool RunChecks() => CheckDateBoundaries() && CheckAdvanceAndPause() && CheckLimit();

    private static bool CheckDateBoundaries()
    {
        if (!Matches(0, 0, 1, 1, 1, 1, Season.Spring) ||
            !Matches(51, 0, 1, 1, 1, 1, Season.Spring) ||
            !Matches(52, 1, 1, 1, 2, 2, Season.Spring) ||
            !Matches(359, 6, 1, 1, 7, 7, Season.Spring) ||
            !Matches(360, 7, 1, 1, 8, 1, Season.Spring) ||
            !Matches(720, 14, 1, 1, 15, 1, Season.Spring) ||
            !Matches(1439, 27, 1, 1, 28, 7, Season.Spring) ||
            !Matches(1440, 28, 1, 2, 1, 1, Season.Spring) ||
            !Matches(4320, 84, 1, 4, 1, 1, Season.Summer) ||
            !Matches(17279, 335, 1, 12, 28, 7, Season.Winter) ||
            !Matches(17280, 336, 2, 1, 1, 1, Season.Spring) ||
            !Matches(360000, 7000, 21, 11, 1, 1, Season.Winter))
            return Fail("精确比例的日、周、月、季或年边界错误");
        return true;
    }

    private static bool CheckAdvanceAndPause()
    {
        var oneStep = new GameCalendar();
        var split = new GameCalendar();
        if (!oneStep.TryAdvanceSeconds(360) || !split.TryAdvanceSeconds(51) ||
            !split.TryAdvanceSeconds(1) || !split.TryAdvanceSeconds(308) ||
            split.Snapshot != oneStep.Snapshot)
            return Fail("累计推进与分步推进的日期不同");
        split.SetPaused(true);
        if (!split.TryAdvanceSeconds(360) || split.Snapshot.ElapsedSeconds != 360 ||
            !split.Snapshot.IsPaused)
            return Fail("暂停时日历继续推进");
        split.SetPaused(false);
        if (!split.TryAdvanceSeconds(360) || split.Snapshot.ElapsedSeconds != 720 ||
            split.Snapshot.ElapsedDays != 14 || split.Snapshot.IsPaused)
            return Fail("恢复后日历补算或未推进");
        return true;
    }

    private static bool CheckLimit()
    {
        var calendar = new GameCalendar(uint.MaxValue - 1);
        if (!calendar.TryAdvanceSeconds(1) || calendar.Snapshot.ElapsedSeconds != uint.MaxValue ||
            calendar.TryAdvanceSeconds(1) || calendar.Snapshot.ElapsedSeconds != uint.MaxValue ||
            calendar.Snapshot.Year < 200000)
            return Fail("uint32 秒数上限发生回绕或丢失状态");
        return true;
    }

    private static bool Matches(uint seconds, uint elapsedDays, int year, int month, int day,
        int dayOfWeek, Season season)
    {
        CalendarSnapshot date = new GameCalendar(seconds).Snapshot;
        return date.ElapsedSeconds == seconds && date.ElapsedDays == elapsedDays &&
            date.Year == year && date.Month == month && date.Day == day &&
            date.DayOfWeek == dayOfWeek && date.Season == season && !date.IsPaused;
    }

    private static bool Fail(string message)
    {
        GD.PushError(message);
        return false;
    }
}
