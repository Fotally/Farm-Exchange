using FarmExchange.Gameplay;
using FarmExchange.Time;

namespace FarmExchange.Farming;

public enum PlantingFailure { None, WrongSeason, InsufficientTime }

internal static class PlantingRules
{
    private const int SecondsPerSeason = 84 * GameTimeUnits.PerDay / GameTimeUnits.PerSecond;

    internal static PlantingFailure Check(CropKind kind, CalendarSnapshot calendar, bool hasWater)
    {
        CropDefinition crop = CropCatalog.Get(kind);
        if (!Allows(crop.GrowingSeasons, calendar.Season))
            return PlantingFailure.WrongSeason;

        int secondsAvailable = SecondsPerSeason -
            (int)(calendar.ElapsedSeconds % SecondsPerSeason);
        for (int offset = 1; offset < 4; offset++)
        {
            Season next = (Season)(((int)calendar.Season + offset) % 4);
            if (!Allows(crop.GrowingSeasons, next))
                break;
            secondsAvailable += SecondsPerSeason;
        }

        int growthSeconds = GameTimeUnits.RemainingSeconds(crop.GrowthDays * GameTimeUnits.PerDay);
        int earliestMaturity = 1 + (hasWater ? 0 : 1) + growthSeconds;
        return earliestMaturity <= secondsAvailable
            ? PlantingFailure.None : PlantingFailure.InsufficientTime;
    }

    /**
     * <summary>按当前季节判断能否播种，预计成熟风险不阻止播种。</summary>
     * <returns>当前适季返回 true，禁生季节返回 false。</returns>
     */
    internal static bool CanSow(CropKind kind, CalendarSnapshot calendar, bool hasWater) =>
        Check(kind, calendar, hasWater) != PlantingFailure.WrongSeason;

    private static bool Allows(GrowingSeasons seasons, Season season) =>
        (seasons & (GrowingSeasons)(1 << (int)season)) != 0;
}
