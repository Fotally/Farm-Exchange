using System;
using FarmExchange.Gameplay;
using FarmExchange.Weather;

namespace FarmExchange.Farming;

internal static class CropYieldRules
{
    internal const double MinimumMultiplier = 6;
    internal const double MaximumMultiplier = 9;

    internal static int GetWeatherPercent(CropKind crop, WeatherKind weather) => weather switch
    {
        WeatherKind.Sunny => 0,
        WeatherKind.Cloudy => crop switch
        {
            CropKind.Corn or CropKind.Sugarcane => -10,
            CropKind.Potato or CropKind.Radish => 10,
            _ => 0,
        },
        WeatherKind.Rain => crop switch
        {
            CropKind.Corn or CropKind.Rice or CropKind.Sugarcane => 20,
            CropKind.Potato or CropKind.Radish => -10,
            _ => 0,
        },
        _ => throw new ArgumentOutOfRangeException(nameof(weather)),
    };

    internal static int CalculateQuantity(double randomBaseQuantity, long weatherPercentTimeUnits,
        int grownTimeUnits) => Math.Max(1, (int)Math.Round(randomBaseQuantity *
            (1 + (double)weatherPercentTimeUnits / grownTimeUnits / 100), MidpointRounding.AwayFromZero));
}
