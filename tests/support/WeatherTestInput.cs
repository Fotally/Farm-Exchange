using System;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Weather;

internal static class WeatherTestInput
{
    internal static TickResult AdvanceTick(FarmGame game, bool rain)
    {
        Rect2I map = new(0, 0, FarmGame.MapSize, FarmGame.MapSize);
        if (rain && !game.TrySetWeatherRegion(map, WeatherKind.Rain, out string error))
            throw new InvalidOperationException(error);
        TickResult result = game.AdvanceTick();
        if (rain && !game.TryClearWeatherRegion(map, out string clearError))
            throw new InvalidOperationException(clearError);
        return result;
    }
}
