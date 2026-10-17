using System;
using System.Globalization;
using Godot;
using FarmExchange.Gameplay;
using static FarmExchange.UI.UiElements;

namespace FarmExchange.UI;

public partial class ProcessorDetailsPanel : VBoxContainer
{
    private readonly Label _status;
    private readonly Label _title;
    private readonly FacilityPreview _preview;
    private readonly Label _relation;
    private readonly Label _processing;
    private readonly Label _price;
    private readonly Label _stock;
    private readonly Label _remaining;
    private readonly Label _nextStep;
    private readonly Label _removalLoss;
    private readonly ProgressBar _progress;

    public event Action? RemoveRequested;
    public event Action? InventoryRequested;

    public ProcessorDetailsPanel()
    {
        AddThemeConstantOverride("separation", 11);
        _preview = new FacilityPreview(BuildingKind.Processor) { Name = "ProcessorFacilityPreview" };
        AddChild(_preview);
        var heading = new HBoxContainer();
        _title = MakeLabel("", 27, Ink);
        _title.Name = "ProcessorBuildingTitle";
        _title.AutowrapMode = TextServer.AutowrapMode.Off;
        _title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        heading.AddChild(_title);
        _status = MakeLabel("", 14, Mid);
        _status.AutowrapMode = TextServer.AutowrapMode.Off;
        _status.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _status.Name = "ProcessorCurrentStatus";
        heading.AddChild(_status);
        AddChild(heading);
        _remaining = MakeLabel("", 14, Muted);
        _remaining.Name = "ProcessorActualProgress";
        AddChild(_remaining);
        _progress = new ProgressBar { Name = "ProcessorProgress", ShowPercentage = false, CustomMinimumSize = new Vector2(0, 6) };
        AddChild(_progress);
        var relation = new HBoxContainer();
        relation.AddChild(UiIcons.Create(UiIcon.Box, 25, Muted));
        _relation = MakeLabel("", 14, Muted);
        _relation.Name = "ProcessorRelation";
        _relation.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        relation.AddChild(_relation);
        AddChild(relation);
        _processing = MakeLabel("", 12, Muted);
        _processing.Name = "ProcessorBatchRules";
        AddChild(_processing);
        var metrics = new HBoxContainer();
        metrics.AddThemeConstantOverride("separation", 11);
        metrics.AddChild(MakeMetric("加工品库存", "份", "ProcessorProductStock", out _stock));
        metrics.AddChild(MakeMetric("加工品当前报价", "金币", "ProcessorProductPrice", out _price));
        AddChild(metrics);
        _nextStep = MakeLabel("", 14, Muted);
        _nextStep.Name = "ProcessorNextStep";
        AddChild(_nextStep);
        Button inventory = MakeButton("管理原料库存", Mid, 0, 56);
        inventory.Name = "ProcessorInventoryButton";
        inventory.AddThemeFontSizeOverride("font_size", 16);
        inventory.Icon = UiIcons.Texture(UiIcon.Box);
        inventory.ExpandIcon = true;
        inventory.AddThemeConstantOverride("icon_max_width", 23);
        inventory.Pressed += () => InventoryRequested?.Invoke();
        AddChild(inventory);
        _removalLoss = MakeLabel("", 12, Muted);
        _removalLoss.Name = "ProcessorRemovalLoss";
        AddChild(_removalLoss);
        Button remove = MakeQuietButton("移除加工场地 · 不退款", 0, 35);
        remove.AddThemeFontSizeOverride("font_size", 13);
        remove.Name = "RemoveButton";
        remove.Pressed += () => RemoveRequested?.Invoke();
        AddChild(remove);
    }

    /**
     * <summary>完整刷新真实加工场地的批量、状态、库存、进度、损失说明与缩略图。</summary>
     * <param name="game">只读经营入口，不推进或修改经营。</param>
     * <param name="cell">当前加工场地任一子格；非加工场地属于调用错误。</param>
     */
    public void Refresh(FarmGame game, Vector2I cell)
    {
        var space = game.GetBuildingSpace(cell);
        if (space?.Building != BuildingKind.Processor)
            throw new InvalidOperationException("该土地没有加工场地");
        Vector2I anchor = space.AnchorCell;
        ProcessorDetailsSnapshot details = game.GetProcessorDetails(anchor);
        PlotSnapshot plot = game.GetPlot(anchor);

        RefreshDetails(details);
        RefreshProgress(plot);
        _preview.Refresh(space, plot.CropKind, CropStage.None);
    }

    private void RefreshDetails(ProcessorDetailsSnapshot details)
    {
        string status = details.Status switch
        {
            ProcessorStatus.Processing => "加工中",
            ProcessorStatus.WaitingForReserve => "底线保留",
            ProcessorStatus.ReadyToProcess => "待领取",
            _ => "待原料",
        };
        _title.Text = details.Crop.BuildingName;
        _status.Text = status;
        _relation.Text = details.BatchQuantity > 0 ?
            $"本批投入 {details.BatchQuantity} 份{details.Crop.CropName} → {details.BatchQuantity} 份{details.Crop.ProductName}" :
            $"1 份{details.Crop.CropName} → 1 份{details.Crop.ProductName}";
        _processing.Text = $"每批最多 9 份，投入与产出 1:1。\n每批加工 {details.Crop.ProcessingHalfDays / 2m:0.#} 天，少量批次同样耗时。";
        _removalLoss.Text = details.BatchQuantity > 0 ?
            $"移除丢失本批已投入的 {details.BatchQuantity} 份原料，不退还建造费。" :
            "移除不退还建造费；加工中会丢失本批已投入原料。";
        _price.Text = FormatCoins(details.ProductPriceCents);
        _price.TooltipText = $"{details.Crop.ProductName}加工品当前报价";
        _stock.Text = details.ProductStock.ToString(CultureInfo.InvariantCulture);
        _stock.TooltipText = $"{details.Crop.ProductName}加工品库存";
        _nextStep.Text = details.Status switch
        {
            ProcessorStatus.Processing => "下一步：本批完成后自动入库，可在市场出售。",
            ProcessorStatus.WaitingForReserve => "等待原料超过保留底线；可在库存调整底线，或补充原料。",
            ProcessorStatus.ReadyToProcess => "下一步：下一次领取阶段自动投入原料。",
            _ => $"等待{details.Crop.CropName}；可等待农田收获，或在市场买入。",
        };
    }

    private void RefreshProgress(PlotSnapshot plot)
    {
        double remainingDays = Math.Ceiling(plot.RemainingSeconds *
            (double)FarmExchange.Time.GameTimeUnits.PerSecond /
            FarmExchange.Time.GameTimeUnits.PerDay * 10) / 10;
        double totalSeconds = FarmGame.GetCrop(plot.CropKind).ProcessingHalfDays *
            (double)FarmExchange.Time.GameTimeUnits.PerHalfDay / FarmExchange.Time.GameTimeUnits.PerSecond;
        _progress.Visible = plot.RemainingSeconds > 0;
        _progress.Value = plot.RemainingSeconds > 0 ?
            Math.Clamp((totalSeconds - plot.RemainingSeconds) / totalSeconds * 100, 0, 100) : 0;
        _remaining.Text = plot.RemainingSeconds > 0 ?
            $"本批实际剩余 {remainingDays.ToString("0.0", CultureInfo.InvariantCulture)} 天" : "当前没有加工批次";
    }

    private static PanelContainer MakeMetric(string title, string unit, string name, out Label value)
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        panel.AddThemeStyleboxOverride("panel", Style(new Color("eee1ba"), 0));
        var stack = new VBoxContainer();
        WrapMargin(panel, 12, 11).AddChild(stack);
        stack.AddChild(MakeLabel(title, 13, Muted));
        var amount = new HBoxContainer();
        value = MakeLabel("", 27, Ink);
        value.Name = name;
        value.AutowrapMode = TextServer.AutowrapMode.Off;
        value.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        amount.AddChild(value);
        var unitLabel = MakeLabel(unit, 13, Muted);
        unitLabel.Name = name + "Unit";
        unitLabel.AutowrapMode = TextServer.AutowrapMode.Off;
        unitLabel.SizeFlagsVertical = SizeFlags.ShrinkEnd;
        amount.AddChild(unitLabel);
        stack.AddChild(amount);
        return panel;
    }
}
