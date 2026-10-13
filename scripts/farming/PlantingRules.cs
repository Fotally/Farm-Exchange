using FarmExchange.Gameplay;
using FarmExchange.Logging;
using FarmExchange.Time;

namespace FarmExchange.Farming;

public enum PlantingFailure { None, WrongSeason, InsufficientTime }

internal static class PlantingRules
{
    /**
     * <summary>只读查询当前时刻所在连续适季区间的结束边界，包含冬春年度环绕。</summary>
     * <param name="kind">有效作物种类。</param>
     * <param name="now">非负累计精确时间单位。</param>
     * <returns>首个禁生季起点；当前已禁生时返回 now，全年适季时返回 long.MaxValue。</returns>
     */
    internal static long GetGrowingWindowEndTimeUnits(CropKind kind, long now)
    {
        GrowingSeasons seasons = CropCatalog.Get(kind).GrowingSeasons;
        Season current = GameCalendar.GetDate(checked((uint)(now / GameTimeUnits.PerDay))).Season;
        long seasonUnits = GameCalendar.DaysPerSeason * GameTimeUnits.PerDay;
        long seasonNumber = now / seasonUnits;
        for (int offset = 0; offset < GameCalendar.SeasonsPerYear; offset++)
        {
            Season season = (Season)(((int)current + offset) % GameCalendar.SeasonsPerYear);
            if (!Allows(seasons, season))
                return offset == 0 ? now : (seasonNumber + offset) * seasonUnits;
        }
        return long.MaxValue;
    }

    internal static PlantingFailure Check(CropKind kind, CalendarSnapshot calendar, bool hasWater,
        ProductionDiagnostics? diagnostics = null, int? anchorIndex = null)
    {
        long now = (long)calendar.ElapsedSeconds * GameTimeUnits.PerSecond;
        long growingEnd = GetGrowingWindowEndTimeUnits(kind, now);
        if (growingEnd == now)
        {
            diagnostics?.PlantingChecked(anchorIndex, kind, PlantingFailure.WrongSeason);
            return PlantingFailure.WrongSeason;
        }

        CropDefinition crop = CropCatalog.Get(kind);
        int growthSeconds = GameTimeUnits.RemainingSeconds(crop.GrowthDays * GameTimeUnits.PerDay);
        int earliestMaturity = 1 + (hasWater ? 0 : 1) + growthSeconds;
        PlantingFailure result = (long)earliestMaturity * GameTimeUnits.PerSecond <= growingEnd - now
            ? PlantingFailure.None : PlantingFailure.InsufficientTime;
        diagnostics?.PlantingChecked(anchorIndex, kind, result);
        return result;
    }

    /**
     * <summary>按当前季节判断能否播种，预计成熟风险不阻止播种。</summary>
     * <returns>当前适季返回 true，禁生季节返回 false。</returns>
     */
    internal static bool CanSow(CropKind kind, CalendarSnapshot calendar, bool hasWater,
        ProductionDiagnostics? diagnostics = null, int? anchorIndex = null) =>
        Check(kind, calendar, hasWater, diagnostics, anchorIndex) != PlantingFailure.WrongSeason;

    private static bool Allows(GrowingSeasons seasons, Season season) =>
        (seasons & (GrowingSeasons)(1 << (int)season)) != 0;
}
