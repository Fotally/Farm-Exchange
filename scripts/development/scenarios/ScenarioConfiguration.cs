using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
using FarmExchange.Time;

namespace FarmExchange.Development;

/**
 * <summary>一次加载后固定的买入加工卖出参数及原文件引用。</summary>
 */
public sealed class ScenarioConfiguration
{
    public string SourcePath { get; private init; } = "";
    public string Sha256 { get; private init; } = "";
    public string CaseId { get; private init; } = "";
    public int Revision { get; private init; }
    public string Target { get; private init; } = "";
    public int? Seed { get; private init; }
    public IReadOnlyList<ScenarioTimeSegment> TimePlan { get; private init; } = Array.Empty<ScenarioTimeSegment>();
    public CommodityId RawCommodity { get; private init; }
    public CommodityId ProductCommodity => new(RawCommodity.Crop, CommodityKind.Product);
    public int Quantity { get; private init; }
    public Vector2I ProcessorAnchor { get; private init; }
    public uint ProcessingWaitLimitTicks { get; private init; }
    public uint OrderWaitLimitTicks { get; private init; }

    /**
     * <summary>一次读取原始UTF-8文件，严格校验并固定参数与SHA-256。</summary>
     * <param name="path">持久JSON参数文件路径。</param>
     * <returns>独立且不可修改的参数；错误抛出ScenarioConfigurationException。</returns>
     */
    public static ScenarioConfiguration Load(string path)
    {
        try
        {
            string source = Path.GetFullPath(path);
            byte[] bytes = File.ReadAllBytes(source);
            string json = new UTF8Encoding(false, true).GetString(bytes);
            if (json.Length > 0 && json[0] == '\uFEFF')
                json = json[1..];
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            Fields(root, "root", new[] { "schemaVersion", "caseId", "revision", "flow", "run", "execution", "parameters" });
            if (Integer(root, "schemaVersion") != 1 || Text(root, "flow") != "buy-process-sell")
                throw Error("未知schemaVersion或固定流程");
            string caseId = Text(root, "caseId");
            if (string.IsNullOrWhiteSpace(caseId))
                throw Error("caseId不能为空");
            int revision = Positive(root, "revision");
            JsonElement run = root.GetProperty("run");
            Fields(run, "run", new[] { "target" }, new[] { "seed" });
            string target = Text(run, "target");
            int? seed = null;
            if (target == "independent")
                seed = Integer(run, "seed");
            else if (target != "current" || run.TryGetProperty("seed", out _))
                throw Error("run.target须为current或independent；current不接受seed");
            JsonElement execution = root.GetProperty("execution");
            Fields(execution, "execution", new[] { "timePlan" });
            JsonElement segments = execution.GetProperty("timePlan");
            if (segments.ValueKind != JsonValueKind.Array || segments.GetArrayLength() == 0)
                throw Error("timePlan须为非空数组");
            var timePlan = new List<ScenarioTimeSegment>();
            uint previous = 0;
            foreach (JsonElement segment in segments.EnumerateArray())
            {
                Fields(segment, "timePlan区间", new[] { "endDate", "rate" });
                string endDate = Text(segment, "endDate");
                uint endTicks = ParseDate(endDate);
                if (endTicks <= previous)
                    throw Error("时间区间终点须严格递增");
                JsonElement rateElement = segment.GetProperty("rate");
                if (rateElement.ValueKind != JsonValueKind.Number || !rateElement.TryGetDouble(out double rate) ||
                    !SimulationDriver.IsDevelopmentRateAllowed(rate))
                    throw Error("rate须为0.5、1、2或有限正整数倍率");
                timePlan.Add(new ScenarioTimeSegment(endDate, endTicks, rate));
                previous = endTicks;
            }
            JsonElement parameters = root.GetProperty("parameters");
            Fields(parameters, "parameters", new[] { "rawCommodity", "quantity", "processorAnchor", "processingWaitLimitTicks", "orderWaitLimitTicks" });
            string raw = Text(parameters, "rawCommodity");
            CommodityId commodity = default;
            bool found = false;
            foreach (CropDefinition crop in FarmGame.Crops)
                if (raw == crop.Kind + ".Raw")
                {
                    commodity = new CommodityId(crop.Kind, CommodityKind.Raw);
                    found = true;
                }
            if (!found)
                throw Error("rawCommodity须为七种合法原料之一");
            JsonElement anchor = parameters.GetProperty("processorAnchor");
            Fields(anchor, "processorAnchor", new[] { "x", "y" });
            return new ScenarioConfiguration
            {
                SourcePath = source,
                Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                CaseId = caseId,
                Revision = revision,
                Target = target,
                Seed = seed,
                TimePlan = timePlan.AsReadOnly(),
                RawCommodity = commodity,
                Quantity = Positive(parameters, "quantity"),
                ProcessorAnchor = new Vector2I(Integer(anchor, "x"), Integer(anchor, "y")),
                ProcessingWaitLimitTicks = PositiveTicks(parameters, "processingWaitLimitTicks"),
                OrderWaitLimitTicks = PositiveTicks(parameters, "orderWaitLimitTicks"),
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or
            DecoderFallbackException or ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            throw new ScenarioConfigurationException("参数文件读取或格式错误：" + exception.Message, exception);
        }
    }

    private static void Fields(JsonElement element, string location, string[] required, string[]? optional = null)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw Error(location + "须为对象");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!names.Add(property.Name))
                throw Error(location + "含重复字段：" + property.Name);
            if (Array.IndexOf(required, property.Name) < 0 && (optional == null || Array.IndexOf(optional, property.Name) < 0))
                throw Error(location + "含未知字段：" + property.Name);
        }
        foreach (string name in required)
            if (!names.Contains(name))
                throw Error(location + "缺少字段：" + name);
    }

    private static string Text(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String)
            throw Error(name + "须为字符串");
        return value.GetString()!;
    }

    private static int Integer(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int number))
            throw Error(name + "须为int32整数");
        return number;
    }

    private static int Positive(JsonElement element, string name)
    {
        int number = Integer(element, name);
        return number > 0 ? number : throw Error(name + "须为正整数");
    }

    private static uint PositiveTicks(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Number ||
            !value.TryGetUInt32(out uint number) || number == 0)
            throw Error(name + "须为uint32正整数经营tick预算");
        return number;
    }

    private static uint ParseDate(string date)
    {
        if (date.Length != 8 || date[2] != '-' || date[5] != '-')
            throw Error("endDate须为游戏yy-MM-dd日期");
        foreach (int index in new[] { 0, 1, 3, 4, 6, 7 })
            if (date[index] < '0' || date[index] > '9')
                throw Error("endDate须为游戏yy-MM-dd日期");
        int year = int.Parse(date[..2]);
        int month = int.Parse(date[3..5]);
        int day = int.Parse(date[6..8]);
        if (year < 1 || month < 1 || month > 12 || day < 1 || day > 28)
            throw Error("游戏日期年为01至99、月为01至12、日为01至28");
        long days = (year - 1L) * 336 + (month - 1L) * 28 + day - 1L;
        return (uint)((days * GameTimeUnits.PerDay + GameTimeUnits.PerSecond - 1) / GameTimeUnits.PerSecond);
    }

    private static ScenarioConfigurationException Error(string message) => new(message);
}

/**
 * <summary>终点为游戏日期起点首次达到的完整经营tick。</summary>
 */
public sealed record ScenarioTimeSegment(string EndDate, uint EndTicks, double Rate);
