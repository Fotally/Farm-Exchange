using System;
using System.Globalization;
using Godot;
using FarmExchange.Gameplay;
using static FarmExchange.UI.UiElements;

namespace FarmExchange.UI;

public partial class ProcessorDetailsPanel : VBoxContainer
{
    private readonly Label _status;
    private readonly Label _relation;
    private readonly Label _processing;
    private readonly Label _price;
    private readonly Label _stock;
    private readonly Label _remaining;
    private readonly Label _nextStep;
    private readonly ProgressBar _progress;

    public event Action? RemoveRequested;

    public ProcessorDetailsPanel()
    {
        AddThemeConstantOverride("separation", 9);
        _status = MakeLabel("", 18, Ink);
        _status.Name = "ProcessorCurrentStatus";
        AddChild(_status);
        _remaining = MakeLabel("", 13, Ink);
        _remaining.Name = "ProcessorActualProgress";
        AddChild(_remaining);
        _progress = new ProgressBar { Name = "ProcessorProgress", ShowPercentage = false, CustomMinimumSize = new Vector2(0, 9) };
        AddChild(_progress);
        _relation = MakeLabel("", 14, Ink);
        AddChild(_relation);
        _processing = MakeLabel("", 12, Ink);
        AddChild(_processing);
        var resources = new VBoxContainer();
        var card = new PanelContainer();
        card.AddThemeStyleboxOverride("panel", Style(Paper, 0));
        WrapMargin(card, 9, 8).AddChild(resources);
        _price = MakeLabel("", 14, Ink);
        _stock = MakeLabel("", 14, Ink);
        resources.AddChild(_price);
        resources.AddChild(_stock);
        AddChild(card);
        AddChild(MakeInfoCard("", out _nextStep));
        AddChild(MakeLabel("移除会丢失已投入原料和未完成批次，不退还建造费。", 12, Ink));
        Button remove = MakeQuietButton("移除加工场地", 0, 32);
        remove.Name = "RemoveButton";
        remove.Pressed += () => RemoveRequested?.Invoke();
        AddChild(remove);
    }

    public void Refresh(ProcessorDetailsSnapshot details)
    {
        string status = details.Status switch
        {
            ProcessorStatus.Processing => "加工中",
            ProcessorStatus.WaitingForReserve => "等待原料超过保留底线",
            ProcessorStatus.ReadyToProcess => "待领取原料",
            _ => $"等待{details.Crop.CropName}",
        };
        _status.Text = $"{details.Crop.BuildingName} · {status}";
        _relation.Text = $"1 份{details.Crop.CropName} → 1 份{details.Crop.ProductName}";
        _processing.Text = $"加工周期：投入原料后 {details.Crop.ProcessingHalfDays / 2m:0.#} 天完成";
        _price.Text = $"{details.Crop.ProductName}加工品当前报价：{FormatCoins(details.ProductPriceCents)} 金币";
        _stock.Text = $"{details.Crop.ProductName}加工品库存：{details.ProductStock}";
        _nextStep.Text = details.Status switch
        {
            ProcessorStatus.Processing => "下一步：本批完成后自动入库，可在市场出售。",
            ProcessorStatus.WaitingForReserve => "下一步：在库存调整保留底线，或补充原料后等待领取。",
            ProcessorStatus.ReadyToProcess => "下一步：下一次领取阶段自动投入原料。",
            _ => "下一步：等待农田收获，或在市场买入对应原料。",
        };
    }

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
}
