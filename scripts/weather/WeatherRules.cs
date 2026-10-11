using System;
using System.Text.Json;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Time;

namespace FarmExchange.Weather;

/**
 * <summary>由正式天气 JSON 提供的具名预排数据，加载失败直接报告异常。</summary>
 */
public sealed class WeatherRules
{
    public const string ResourcePath = "res://assets/gameplay/weather/weather_rules.json";
    private static readonly Lazy<WeatherRules> Shared = new(() => Parse(FileAccess.GetFileAsString(ResourcePath)));
    public static WeatherRules Default => Shared.Value;

    public int CycleYears { get; init; }
    public int DaysPerYear { get; init; }
    public int MinimumIntervalDays { get; init; }
    public int MaximumIntervalDays { get; init; }
    public int MinimumDurationDays { get; init; }
    public int MaximumDurationDays { get; init; }
    public int MinimumRegionSizeCells { get; init; }
    public int MaximumRegionSizeCells { get; init; }
    public int MaximumActiveRegions { get; init; }
    public int CloudyWeight { get; init; }
    public int RainWeight { get; init; }
    public uint CycleSeconds => checked((uint)((long)CycleYears * DaysPerYear * GameTimeUnits.PerDay / GameTimeUnits.PerSecond));

    /**
     * <summary>解析并完整检查天气配置，不为缺失或非法数值补默认值。</summary>
     * <param name="json">具名天气配置 JSON。</param>
     * <returns>验证通过的天气配置。</returns>
     */
    public static WeatherRules Parse(string json)
    {
        WeatherRules rules = JsonSerializer.Deserialize<WeatherRules>(json,
            new JsonSerializerOptions { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow })
            ?? throw new JsonException("天气配置不能为空。");
        long cycleDays = (long)rules.CycleYears * rules.DaysPerYear;
        if (rules.CycleYears <= 0 || rules.DaysPerYear <= 0 ||
            GameCalendar.GetDate((uint)rules.DaysPerYear).Year != 2 ||
            GameCalendar.GetDate((uint)rules.DaysPerYear).Month != 1 ||
            GameCalendar.GetDate((uint)rules.DaysPerYear).Day != 1 ||
            rules.MinimumIntervalDays <= 0 || rules.MaximumIntervalDays < rules.MinimumIntervalDays ||
            rules.MinimumDurationDays <= 0 || rules.MaximumDurationDays < rules.MinimumDurationDays ||
            rules.MinimumRegionSizeCells <= 0 || rules.MaximumRegionSizeCells < rules.MinimumRegionSizeCells ||
            rules.MaximumRegionSizeCells > FarmGame.MapSize || rules.MaximumActiveRegions <= 0 ||
            rules.CloudyWeight <= 0 || rules.RainWeight <= 0 ||
            (long)rules.CloudyWeight + rules.RainWeight > int.MaxValue ||
            rules.MaximumDurationDays >= cycleDays || rules.MaximumIntervalDays >= cycleDays ||
            cycleDays > (long)uint.MaxValue * GameTimeUnits.PerSecond / GameTimeUnits.PerDay ||
            cycleDays * GameTimeUnits.PerDay % GameTimeUnits.PerSecond != 0)
            throw new JsonException("天气配置的周期、区间、区域尺寸、数量或类型权重无效。");
        return rules;
    }
}
