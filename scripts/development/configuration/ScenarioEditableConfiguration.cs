using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FarmExchange.Development;

/**
 * <summary>与schemaVersion=1原JSON一一对应的可编辑参数；来源和摘要另由加载结果拥有。</summary>
 */
public sealed class ScenarioEditableConfiguration
{
    [JsonPropertyName("schemaVersion"), ConfigField("格式版本", ReadOnly = true, Minimum = 1, Maximum = 1)]
    public int SchemaVersion { get; set; } = 1;
    [JsonPropertyName("caseId"), ConfigField("配置名称", Group = "基本设置")]
    public string CaseId { get; set; } = "新配置";
    [JsonPropertyName("revision"), ConfigField("修订", ReadOnly = true, Minimum = 1, Maximum = int.MaxValue)]
    public int Revision { get; set; } = 1;
    [JsonPropertyName("flow"), ConfigField("流程", ReadOnly = true, Values = new[] { "buy-process-sell" }, Labels = new[] { "买入 → 加工 → 委托卖出" })]
    public string Flow { get; set; } = "buy-process-sell";
    [JsonPropertyName("run"), ConfigField("运行对象", Group = "基本设置")]
    public ScenarioEditableRun Run { get; set; } = new();
    [JsonPropertyName("execution"), ConfigField("时间安排", Group = "时间安排")]
    public ScenarioEditableExecution Execution { get; set; } = new();
    [JsonPropertyName("parameters"), ConfigField("生产参数", Group = "生产参数")]
    public ScenarioEditableParameters Parameters { get; set; } = new();
}

public sealed class ScenarioEditableRun
{
    [JsonPropertyName("target"), ConfigField("运行对象", Values = new[] { "independent", "current" }, Labels = new[] { "独立测试局", "当前经营局" })]
    public string Target { get; set; } = "independent";
    [JsonPropertyName("seed"), ConfigField("随机种子", Description = "只用于独立测试局", VisibleWhenProperty = nameof(Target), VisibleWhenValue = "independent")]
    public int Seed { get; set; } = 12345;
}

public sealed class ScenarioEditableExecution
{
    [JsonPropertyName("timePlan"), ConfigField("时间区间", Description = "按顺序推进，终点日期必须严格递增", Minimum = 1)]
    public List<ScenarioEditableTimeSegment> TimePlan { get; set; } = new() { new() };
}

public sealed class ScenarioEditableTimeSegment
{
    [JsonPropertyName("endDate"), ConfigField("终点日期", Editor = "game-date", Description = "游戏每月28日")]
    public string EndDate { get; set; } = "01-02-01";
    [JsonPropertyName("rate"), ConfigField("经营倍率", Editor = "rate", Unit = "×")]
    public double Rate { get; set; } = 1;
}

public sealed class ScenarioEditableParameters
{
    [JsonPropertyName("rawCommodity"), ConfigField("买入原料", Editor = "raw-commodity")]
    public string RawCommodity { get; set; } = "Wheat.Raw";
    [JsonPropertyName("quantity"), ConfigField("买入数量", Minimum = 1, Maximum = int.MaxValue, Unit = "份")]
    public int Quantity { get; set; } = 1;
    [JsonPropertyName("processorAnchor"), ConfigField("加工场地锚点")]
    public ScenarioEditableAnchor ProcessorAnchor { get; set; } = new();
    [JsonPropertyName("processingWaitLimitTicks"), ConfigField("加工等待预算", Minimum = 1, Maximum = uint.MaxValue, Unit = "经营tick")]
    public uint ProcessingWaitLimitTicks { get; set; } = 1800;
    [JsonPropertyName("orderWaitLimitTicks"), ConfigField("委托等待预算", Minimum = 1, Maximum = uint.MaxValue, Unit = "经营tick")]
    public uint OrderWaitLimitTicks { get; set; } = 10;
}

public sealed class ScenarioEditableAnchor
{
    [JsonPropertyName("x"), ConfigField("X格坐标")]
    public int X { get; set; }
    [JsonPropertyName("y"), ConfigField("Y格坐标")]
    public int Y { get; set; }
}
