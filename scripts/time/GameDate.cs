namespace FarmExchange.Time;

public readonly record struct GameDate(uint ElapsedDays, int Year, int Month, int Day, Season Season);
