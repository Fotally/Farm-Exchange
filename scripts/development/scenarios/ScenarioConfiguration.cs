using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;

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
            return FromBytes(source, bytes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new ScenarioConfigurationException("参数文件读取错误：" + exception.Message, exception);
        }
    }

    internal static ScenarioConfiguration FromBytes(string source, byte[] bytes)
    {
        var configuration = (ScenarioEditableConfiguration)ScenarioConfigurationSchema.BuyProcessSell.Read(bytes);
        CommodityId raw = default;
        foreach (CropDefinition crop in FarmGame.Crops)
            if (configuration.Parameters.RawCommodity == crop.Kind + ".Raw")
                raw = new CommodityId(crop.Kind, CommodityKind.Raw);
        var timePlan = new List<ScenarioTimeSegment>();
        foreach (ScenarioEditableTimeSegment segment in configuration.Execution.TimePlan)
            timePlan.Add(new ScenarioTimeSegment(segment.EndDate, ScenarioConfigurationSchema.ParseGameDate(segment.EndDate), segment.Rate));
        return new ScenarioConfiguration
        {
            SourcePath = source,
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            CaseId = configuration.CaseId,
            Revision = configuration.Revision,
            Target = configuration.Run.Target,
            Seed = configuration.Run.Target == "independent" ? configuration.Run.Seed : null,
            TimePlan = timePlan.AsReadOnly(),
            RawCommodity = raw,
            Quantity = configuration.Parameters.Quantity,
            ProcessorAnchor = new Vector2I(configuration.Parameters.ProcessorAnchor.X, configuration.Parameters.ProcessorAnchor.Y),
            ProcessingWaitLimitTicks = configuration.Parameters.ProcessingWaitLimitTicks,
            OrderWaitLimitTicks = configuration.Parameters.OrderWaitLimitTicks,
        };
    }
}

/**
 * <summary>终点为游戏日期起点首次达到的完整经营tick。</summary>
 */
public sealed record ScenarioTimeSegment(string EndDate, uint EndTicks, double Rate);
