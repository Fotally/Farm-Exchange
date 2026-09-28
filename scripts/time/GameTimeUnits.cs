namespace FarmExchange.Time;

public static class GameTimeUnits
{
    public const int PerSecond = 7;
    public const int PerHalfDay = 180;
    public const int PerDay = 360;

    public static int RemainingSeconds(int units) => (units + PerSecond - 1) / PerSecond;
}
