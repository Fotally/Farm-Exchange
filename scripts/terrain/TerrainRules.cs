using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Godot;

namespace FarmExchange.Terrain;

/**
 * <summary>地形 Module 内经过完整配置校验的不可变输入，不持有生成结果或导航状态。</summary>
 */
public sealed class TerrainRules
{
    public const string ResourcePath = "res://assets/gameplay/terrain/terrain_rules.json";

    public readonly record struct NumberRange(decimal Min, decimal Max);
    public readonly record struct PondBand(int Count, NumberRange LongAxis, NumberRange ShortAxis);
    public readonly record struct Coverage(decimal Target, decimal Min, decimal Max);
    public readonly record struct Protection(int Width, int Height, decimal ShoreClearance);
    public readonly record struct NearPondRules(decimal LongAxis, decimal ShortAxis, NumberRange CenterDistance);

    public string Source { get; }
    public int MapSize { get; }
    public int SchemaVersion { get; }
    public int GenerationVersion { get; }
    public PondBand Small { get; }
    public PondBand Medium { get; }
    public PondBand Large { get; }
    public Coverage WaterCoverage { get; }
    public decimal MinimumPondSeparation { get; }
    public int MapEdgeLandWidth { get; }
    public decimal MaximumOutlinePerturbation { get; }
    public Protection CenterProtection { get; }
    public NearPondRules NearPond { get; }
    public decimal WorkerFootRadius { get; }
    public Rect2I ProtectionCells { get; }
    public long TotalPondCount => (long)Small.Count + Medium.Count + Large.Count;
    public bool IsAllLand => TotalPondCount == 0;

    /**
     * <summary>每次重新读取指定地形配置并校验，不缓存文件或修改已有局配置。</summary>
     * <param name="source">Godot 可读取的配置文件路径。</param>
     * <param name="mapSize">实际正方形地图的基础格边长。</param>
     * <param name="openingCells">实际开局设施的完整占地格，不能只传锚点。</param>
     * <param name="workerStarts">实际工人脚根起点，采用基础格平面坐标。</param>
     * <returns>不可变配置；读取或校验失败抛出带来源、字段和原因的 JsonException。</returns>
     */
    public static TerrainRules Load(string source, int mapSize,
        IReadOnlyList<Vector2I> openingCells, IReadOnlyList<Vector2> workerStarts)
    {
        CheckSource(source);
        using FileAccess file = FileAccess.Open(source, FileAccess.ModeFlags.Read);
        if (file is null)
            throw Error(source, "$", $"无法读取文件：{FileAccess.GetOpenError()}。");
        string json = file.GetAsText();
        if (file.GetError() is not (Godot.Error.Ok or Godot.Error.FileEof))
            throw Error(source, "$", $"读取文件失败：{file.GetError()}。");
        return Parse(json, source, mapSize, openingCells, workerStarts);
    }

    /**
     * <summary>解析全部必填字段并检查版本、数值及开局保护约束，不补默认值或改参。</summary>
     * <param name="json">完整地形 JSON，正式与测试使用相同字段。</param>
     * <param name="source">用于错误定位的非空配置来源。</param>
     * <param name="mapSize">实际正方形地图的基础格边长。</param>
     * <param name="openingCells">实际开局设施的完整占地格。</param>
     * <param name="workerStarts">实际工人脚根起点，圆脚底必须完整落在保护区内。</param>
     * <returns>不可变配置；不合法输入抛出带来源、字段和原因的 JsonException。</returns>
     */
    public static TerrainRules Parse(string json, string source, int mapSize,
        IReadOnlyList<Vector2I> openingCells, IReadOnlyList<Vector2> workerStarts)
    {
        CheckSource(source);
        if (json is null)
            throw Error(source, "$", "JSON 不能为空。");
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw Error(source, exception.Path ?? "$", $"JSON 语法错误：{exception.Message}");
        }
        using (document)
            return new TerrainRules(new Reader(source), document.RootElement, mapSize, openingCells, workerStarts);
    }

    /**
     * <summary>重验显式配置与实际建局地图、设施完整占地及工人脚底一致，不保存输入集合。</summary>
     * <param name="mapSize">实际地图基础格边长，必须与解析时相同。</param>
     * <param name="openingCells">实际开局设施完整占地。</param>
     * <param name="workerStarts">实际工人脚根起点。</param>
     */
    public void ValidateContext(int mapSize, IReadOnlyList<Vector2I> openingCells, IReadOnlyList<Vector2> workerStarts)
    {
        if (mapSize != MapSize)
            throw Error(Source, "MapSize", "显式配置与实际建局地图尺寸不同。");
        CheckOpening(openingCells, workerStarts);
    }

    private TerrainRules(Reader reader, JsonElement root, int mapSize,
        IReadOnlyList<Vector2I> openingCells, IReadOnlyList<Vector2> workerStarts)
    {
        Source = reader.Source;
        MapSize = mapSize;
        reader.Object(root, "$", "SchemaVersion", "GenerationVersion", "Small", "Medium", "Large",
            "WaterCoverage", "MinimumPondSeparation", "MapEdgeLandWidth", "MaximumOutlinePerturbation",
            "CenterProtection", "NearPond", "WorkerFootRadius");
        SchemaVersion = reader.Integer(root, "SchemaVersion", 1, 1);
        GenerationVersion = reader.Integer(root, "GenerationVersion", 1, 1);
        if (mapSize <= 0)
            throw Error(Source, "MapSize", "地图边长必须为正整数。");
        Small = reader.Band(root.GetProperty("Small"), "Small");
        Medium = reader.Band(root.GetProperty("Medium"), "Medium");
        Large = reader.Band(root.GetProperty("Large"), "Large");
        JsonElement water = root.GetProperty("WaterCoverage");
        reader.Object(water, "WaterCoverage", "Target", "Min", "Max");
        WaterCoverage = new Coverage(reader.Number(water, "Target", "WaterCoverage"),
            reader.Number(water, "Min", "WaterCoverage"), reader.Number(water, "Max", "WaterCoverage"));
        if (WaterCoverage.Min < 0 || WaterCoverage.Min > WaterCoverage.Target ||
            WaterCoverage.Target > WaterCoverage.Max || WaterCoverage.Max >= 1)
            throw Error(Source, "WaterCoverage", "必须满足 0 ≤ Min ≤ Target ≤ Max < 1。");
        if (IsAllLand)
        {
            if (WaterCoverage.Max != 0)
                throw Error(Source, "WaterCoverage", "三档数量全为 0 时，Min、Target、Max 必须全部为 0。");
        }
        else if (Small.Count == 0 || Medium.Count == 0 || Large.Count == 0 || WaterCoverage.Min <= 0)
            throw Error(Source, "WaterCoverage", "含水配置须三档数量均为正且水面下限为正；部分档为 0 的组合尚未支持。");

        MinimumPondSeparation = reader.Number(root, "MinimumPondSeparation");
        MapEdgeLandWidth = reader.Integer(root, "MapEdgeLandWidth", 0, (mapSize - 1) / 2);
        MaximumOutlinePerturbation = reader.Number(root, "MaximumOutlinePerturbation");
        if (MinimumPondSeparation < 0)
            throw Error(Source, "MinimumPondSeparation", "岸线间距不能为负。");
        if (MaximumOutlinePerturbation < 0 || MaximumOutlinePerturbation >= 1)
            throw Error(Source, "MaximumOutlinePerturbation", "半轴扰动比例必须在 [0, 1) 内。");
        JsonElement protection = root.GetProperty("CenterProtection");
        reader.Object(protection, "CenterProtection", "Width", "Height", "ShoreClearance");
        CenterProtection = new Protection(reader.Integer(protection, "Width", 1, mapSize, "CenterProtection"),
            reader.Integer(protection, "Height", 1, mapSize, "CenterProtection"),
            reader.Number(protection, "ShoreClearance", "CenterProtection"));
        if (CenterProtection.ShoreClearance < 0)
            throw Error(Source, "CenterProtection.ShoreClearance", "岸线退让不能为负。");
        ProtectionCells = new Rect2I((mapSize - CenterProtection.Width) / 2,
            (mapSize - CenterProtection.Height) / 2, CenterProtection.Width, CenterProtection.Height);
        JsonElement near = root.GetProperty("NearPond");
        reader.Object(near, "NearPond", "LongAxis", "ShortAxis", "CenterDistance");
        NearPond = new NearPondRules(reader.Number(near, "LongAxis", "NearPond"),
            reader.Number(near, "ShortAxis", "NearPond"),
            reader.Range(near.GetProperty("CenterDistance"), "NearPond.CenterDistance"));
        if (NearPond.LongAxis < NearPond.ShortAxis ||
            NearPond.LongAxis < Small.LongAxis.Min || NearPond.LongAxis > Small.LongAxis.Max ||
            NearPond.ShortAxis < Small.ShortAxis.Min || NearPond.ShortAxis > Small.ShortAxis.Max)
            throw Error(Source, "NearPond", "参考长轴须不小于短轴，且两轴分别位于小池塘尺寸范围内。");
        WorkerFootRadius = reader.Number(root, "WorkerFootRadius");
        if (WorkerFootRadius <= 0 || WorkerFootRadius >= mapSize / 2m)
            throw Error(Source, "WorkerFootRadius", "脚底半径须为正，直径须小于地图边长。");
        CheckOpening(openingCells, workerStarts);
        if (!IsAllLand)
            CheckWaterSpace();
    }

    private void CheckOpening(IReadOnlyList<Vector2I> openingCells, IReadOnlyList<Vector2> workerStarts)
    {
        if (openingCells is null || openingCells.Count == 0)
            throw Error(Source, "OpeningCells", "须提供真实开局完整占地。");
        for (int i = 0; i < openingCells.Count; i++)
            if (!ProtectionCells.HasPoint(openingCells[i]))
                throw Error(Source, $"OpeningCells[{i}]", "开局占地不在中心保护区内。");
        if (workerStarts is null || workerStarts.Count == 0)
            throw Error(Source, "WorkerStarts", "须提供真实工人起点。");
        decimal left = ProtectionCells.Position.X - 0.5m;
        decimal top = ProtectionCells.Position.Y - 0.5m;
        decimal right = ProtectionCells.End.X - 0.5m;
        decimal bottom = ProtectionCells.End.Y - 0.5m;
        for (int i = 0; i < workerStarts.Count; i++)
        {
            Vector2 position = workerStarts[i];
            if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) ||
                position.X < 0 || position.Y < 0 || position.X >= MapSize || position.Y >= MapSize ||
                (decimal)position.X - WorkerFootRadius < left || (decimal)position.X + WorkerFootRadius > right ||
                (decimal)position.Y - WorkerFootRadius < top || (decimal)position.Y + WorkerFootRadius > bottom)
                throw Error(Source, $"WorkerStarts[{i}]", "工人圆脚底须完整位于中心保护区内。");
        }
    }

    private void CheckWaterSpace()
    {
        // 只拒绝配置阶段可证明的必要条件冲突；面积、摆放和连通仍由完整生成验证。
        decimal innerSize = MapSize - 2m * MapEdgeLandWidth;
        decimal maximumSpan = (decimal)Math.Sqrt(2) * innerSize;
        foreach (PondBand band in new[] { Small, Medium, Large })
            if (band.LongAxis.Min > maximumSpan || band.ShortAxis.Min > maximumSpan)
                throw Error(Source, "MapEdgeLandWidth", "留陆后的地图无法容纳池塘最小轴长。");
        if (MinimumPondSeparation >= maximumSpan)
            throw Error(Source, "MinimumPondSeparation", "最小岸距不允许在留陆后的地图内放置多座池塘。");
        decimal center = (MapSize - 1) / 2m;
        decimal nearestProtectionEdge = Math.Min(Math.Min(center - ProtectionCells.Position.X + 0.5m,
                ProtectionCells.End.X - 0.5m - center),
            Math.Min(center - ProtectionCells.Position.Y + 0.5m, ProtectionCells.End.Y - 0.5m - center));
        if (NearPond.CenterDistance.Max < nearestProtectionEdge + CenterProtection.ShoreClearance)
            throw Error(Source, "NearPond.CenterDistance.Max", "最近岸线距离上限与中心保护及退让冲突。");
        if (WaterCoverage.Min > innerSize * innerSize / ((decimal)MapSize * MapSize))
            throw Error(Source, "WaterCoverage.Min", "水面下限超过地图边缘留陆后可用面积。");
    }

    private static void CheckSource(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            throw Error("<未指定来源>", "Source", "必须指定配置来源。");
    }

    private static JsonException Error(string source, string field, string reason) =>
        new($"地形配置 {source}：字段 {field}：{reason}");

    private sealed class Reader(string source)
    {
        public string Source { get; } = source;

        public void Object(JsonElement value, string path, params string[] fields)
        {
            if (value.ValueKind != JsonValueKind.Object)
                throw Error(Source, path, "必须为对象。");
            var remaining = new HashSet<string>(fields, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in value.EnumerateObject())
            {
                string propertyPath = Path(path, property.Name);
                if (!seen.Add(property.Name))
                    throw Error(Source, propertyPath, "重复字段。");
                if (!remaining.Remove(property.Name))
                    throw Error(Source, propertyPath, "未知字段。");
            }
            foreach (string field in fields)
                if (remaining.Contains(field))
                    throw Error(Source, Path(path, field), "缺失必填字段。");
        }

        public int Integer(JsonElement parent, string name, int min, int max, string path = "$")
        {
            JsonElement value = parent.GetProperty(name);
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int number))
                throw Error(Source, Path(path, name), "必须为 32 位整数。");
            if (number < min || number > max)
                throw Error(Source, Path(path, name), $"必须在 [{min}, {max}] 内。");
            return number;
        }

        public decimal Number(JsonElement parent, string name, string path = "$")
        {
            JsonElement value = parent.GetProperty(name);
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out decimal number) ||
                !IsExactDecimal(value.GetRawText(), number))
                throw Error(Source, Path(path, name), "必须为 decimal 可无损表示的有限数值，不允许溢出或舍入。");
            return number;
        }

        private static bool IsExactDecimal(string raw, decimal number) =>
            NormalizeDecimal(raw) == NormalizeDecimal(number.ToString(CultureInfo.InvariantCulture));

        private static (string Digits, long Exponent) NormalizeDecimal(string value)
        {
            int exponentIndex = value.IndexOfAny(['e', 'E']);
            long exponent = 0;
            if (exponentIndex >= 0)
            {
                if (!int.TryParse(value[(exponentIndex + 1)..], NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out int parsedExponent))
                    return (value, long.MinValue);
                exponent = parsedExponent;
                value = value[..exponentIndex];
            }
            bool negative = value.StartsWith('-');
            if (negative)
                value = value[1..];
            int decimalPoint = value.IndexOf('.');
            if (decimalPoint >= 0)
            {
                exponent -= value.Length - decimalPoint - 1;
                value = value.Remove(decimalPoint, 1);
            }
            value = value.TrimStart('0');
            if (value.Length == 0)
                return ("0", 0);
            string digits = value.TrimEnd('0');
            exponent += value.Length - digits.Length;
            return (negative ? $"-{digits}" : digits, exponent);
        }

        public NumberRange Range(JsonElement value, string path)
        {
            Object(value, path, "Min", "Max");
            var result = new NumberRange(Number(value, "Min", path), Number(value, "Max", path));
            if (result.Min <= 0 || result.Max < result.Min)
                throw Error(Source, path, "必须满足 0 < Min ≤ Max。");
            return result;
        }

        public PondBand Band(JsonElement value, string path)
        {
            Object(value, path, "Count", "LongAxis", "ShortAxis");
            var result = new PondBand(Integer(value, "Count", 0, int.MaxValue, path),
                Range(value.GetProperty("LongAxis"), $"{path}.LongAxis"),
                Range(value.GetProperty("ShortAxis"), $"{path}.ShortAxis"));
            if (result.LongAxis.Max < result.ShortAxis.Min)
                throw Error(Source, path, "尺寸范围不存在长轴不小于短轴的合法组合。");
            return result;
        }

        private static string Path(string path, string name) => path == "$" ? name : $"{path}.{name}";
    }
}
