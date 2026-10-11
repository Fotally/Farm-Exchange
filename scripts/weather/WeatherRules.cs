using System;
using System.Text.Json;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Time;

namespace FarmExchange.Weather;

/**
 * <summary>经完整验证的不可变天气配置，可由多个独立经营局共享。</summary>
 */
public sealed class WeatherRules
{
    public const string ResourcePath = "res://assets/gameplay/weather/weather_rules.json";
    private static readonly Lazy<WeatherRules> Shared = new(() => Parse(FileAccess.GetFileAsString(ResourcePath)));
    public static WeatherRules Default => Shared.Value;

    public int CycleYears { get; }
    public int DaysPerYear { get; }
    public int MinimumIntervalDays { get; }
    public int MaximumIntervalDays { get; }
    public int MinimumDurationDays { get; }
    public int MaximumDurationDays { get; }
    public int MinimumRegionSizeCells { get; }
    public int MaximumRegionSizeCells { get; }
    public int MaximumActiveRegions { get; }
    public int CloudyWeight { get; }
    public int RainWeight { get; }
    public uint CycleSeconds => checked((uint)((long)CycleYears * DaysPerYear * GameTimeUnits.PerDay / GameTimeUnits.PerSecond));

    // JSON 描述仅用于加载；只有 Parse 完整验证后才能产生对外配置值。
    private sealed class JsonConfiguration
    {
        public JsonConfiguration() { }
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
    }

    private WeatherRules(JsonConfiguration data)
    {
        CycleYears = data.CycleYears;
        DaysPerYear = data.DaysPerYear;
        MinimumIntervalDays = data.MinimumIntervalDays;
        MaximumIntervalDays = data.MaximumIntervalDays;
        MinimumDurationDays = data.MinimumDurationDays;
        MaximumDurationDays = data.MaximumDurationDays;
        MinimumRegionSizeCells = data.MinimumRegionSizeCells;
        MaximumRegionSizeCells = data.MaximumRegionSizeCells;
        MaximumActiveRegions = data.MaximumActiveRegions;
        CloudyWeight = data.CloudyWeight;
        RainWeight = data.RainWeight;
    }

    /**
     * <summary>解析并完整检查天气配置，不为缺失或非法数值补默认值。</summary>
     * <param name="json">具名天气配置 JSON。</param>
     * <returns>验证通过的天气配置。</returns>
     */
    public static WeatherRules Parse(string json)
    {
        JsonConfiguration rules = JsonSerializer.Deserialize<JsonConfiguration>(json,
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
            (cycleDays - 1 + rules.MaximumDurationDays) * GameTimeUnits.PerDay >
                (long)uint.MaxValue * GameTimeUnits.PerSecond ||
            cycleDays * GameTimeUnits.PerDay % GameTimeUnits.PerSecond != 0)
            throw new JsonException("天气配置的周期、区间、区域尺寸、数量或类型权重无效。");
        return new WeatherRules(rules);
    }
}
