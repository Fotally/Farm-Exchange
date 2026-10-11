using Godot;

namespace FarmExchange.Weather;

/**
 * <summary>当前真实活动天气的基础格矩形与类型，不含未来预报。</summary>
 * <param name="Cells">半开基础格矩形，右边和下边不包含在内。</param>
 * <param name="Kind">多云或降雨。</param>
 */
public readonly record struct WeatherRegionSnapshot(Rect2I Cells, WeatherKind Kind);
