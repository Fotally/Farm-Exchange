using System;
using System.Globalization;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Land;
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
    private readonly ProgressBar _progress;

    public event Action? RemoveRequested;
    public event Action? InventoryRequested;

    public ProcessorDetailsPanel()
    {
        AddThemeConstantOverride("separation", 11);
        _preview = new FacilityPreview { Name = "ProcessorFacilityPreview" };
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
        AddChild(_processing);
        var metrics = new HBoxContainer();
        metrics.AddThemeConstantOverride("separation", 11);
        metrics.AddChild(MakeMetric("加工品库存", "份", "ProcessorProductStock", out _stock));
        metrics.AddChild(MakeMetric("加工品当前报价", "金币", "ProcessorProductPrice", out _price));
        AddChild(metrics);
        _nextStep = MakeLabel("", 14, Muted);
        AddChild(_nextStep);
        Button inventory = MakeButton("管理原料库存", Mid, 0, 56);
        inventory.Name = "ProcessorInventoryButton";
        inventory.AddThemeFontSizeOverride("font_size", 16);
        inventory.Icon = UiIcons.Texture(UiIcon.Box);
        inventory.ExpandIcon = true;
        inventory.AddThemeConstantOverride("icon_max_width", 23);
        inventory.Pressed += () => InventoryRequested?.Invoke();
        AddChild(inventory);
        AddChild(MakeLabel("移除丢失已投入原料及未完成批次，不退还建造费。", 12, Muted));
        Button remove = MakeQuietButton("移除加工场地 · 不退款", 0, 35);
        remove.AddThemeFontSizeOverride("font_size", 13);
        remove.Name = "RemoveButton";
        remove.Pressed += () => RemoveRequested?.Invoke();
        AddChild(remove);
    }

    public void Refresh(ProcessorDetailsSnapshot details)
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
        _relation.Text = $"1 份{details.Crop.CropName} → 1 份{details.Crop.ProductName}";
        _processing.Text = $"加工周期：投入原料后 {details.Crop.ProcessingHalfDays / 2m:0.#} 天完成";
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

    /**
     * <summary>显示当前加工实例的实际占地与锚点。</summary>
     * <param name="space">同一已选中加工实例的只读空间快照。</param>
     * <param name="crop">同一实例匹配的作物。</param>
     */
    public void RefreshPlacement(BuildingSpaceSnapshot space, CropKind crop) =>
        _preview.Refresh(space, crop, CropStage.None);

    /**
     * <summary>显示同一加工场地真实快照的本批剩余天数与时间进度。</summary>
     * <remarks>调用方在 Refresh 后传入同一实例的地块快照；不推进或预测经营。</remarks>
     * <param name="plot">当前加工场地任一子格的只读地块快照。</param>
     */
    public void RefreshProgress(PlotSnapshot plot)
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
