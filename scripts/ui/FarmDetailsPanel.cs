using System;
using System.Globalization;
using Godot;
using FarmExchange.Gameplay;
using static FarmExchange.UI.UiElements;

namespace FarmExchange.UI;

public partial class FarmDetailsPanel : VBoxContainer
{
    private readonly Label _status;
    private readonly Label _growth;
    private readonly Label _price;
    private readonly Label _stock;
    private readonly Label _cultivation;
    private readonly Label _remaining;
    private readonly ProgressBar _progress;

    public event Action? ChangeCropRequested;
    public event Action? PrepareCropRequested;
    public event Action? CultivationRequested;
    public event Action? RemoveRequested;

    public FarmDetailsPanel()
    {
        AddThemeConstantOverride("separation", 9);
        _status = MakeLabel("", 18, Ink);
        _status.Name = "FarmCurrentStatus";
        AddChild(_status);
        _remaining = MakeLabel("", 13, Ink);
        _remaining.Name = "FarmActualProgress";
        AddChild(_remaining);
        _progress = new ProgressBar { Name = "FarmProgress", ShowPercentage = false, CustomMinimumSize = new Vector2(0, 9) };
        AddChild(_progress);
        _growth = MakeLabel("", 12, Ink);
        AddChild(_growth);
        var resources = new VBoxContainer();
        var card = new PanelContainer();
        card.AddThemeStyleboxOverride("panel", Style(Paper, 0));
        WrapMargin(card, 9, 8).AddChild(resources);
        _price = MakeLabel("", 14, Ink);
        _stock = MakeLabel("", 14, Ink);
        resources.AddChild(_price);
        resources.AddChild(_stock);
        AddChild(card);
        AddChild(MakeInfoCard("", out _cultivation));
        _cultivation.Name = "FarmCultivationStatus";
        AddChild(MakeLabel("下一步", 14, Ink));
        Button change = MakeSecondaryButton("立即改种 · 丢弃未收获作物", 0, 39);
        change.Name = "ChangeCropButton";
        change.Pressed += () => ChangeCropRequested?.Invoke();
        AddChild(change);
        Button prepare = MakeButton("预备下一轮 · 保留当前作物", Mid, 0, 39);
        prepare.Name = "PrepareCropButton";
        prepare.Pressed += () => PrepareCropRequested?.Invoke();
        AddChild(prepare);
        Button cultivation = MakeSecondaryButton("查看共享年度耕作表", 0, 37);
        cultivation.Name = "FarmCultivationButton";
        cultivation.Pressed += () => CultivationRequested?.Invoke();
        AddChild(cultivation);
        AddChild(MakeLabel("移除会丢失本轮未收获作物，不退还建造费。", 12, Ink));
        Button remove = MakeQuietButton("移除农田", 0, 32);
        remove.Name = "RemoveButton";
        remove.Pressed += () => RemoveRequested?.Invoke();
        AddChild(remove);
    }

    public void Refresh(FarmDetailsSnapshot details)
    {
        string status = details.Status switch
        {
            FarmStatus.WaitingForWater => "等待浇水",
            FarmStatus.WaitingForWorkerWithWater => "待播种 · 已有水分",
            FarmStatus.Growing => "生长中",
            FarmStatus.WrongSeason => "当前季节不适宜播种",
            FarmStatus.InsufficientTime => "可播种 · 预计越过禁生边界，有枯萎风险",
            FarmStatus.Resting => "按表休耕",
            _ => "等待工人照料",
        };
        _status.Text = $"{details.Crop.CropName}农田 · {status}";
        _growth.Text = $"生长周期：获得水后 {details.Crop.GrowthDays} 天成熟\n每轮收获 {details.Crop.HarvestQuantity} 份原料";
        _price.Text = $"{details.Crop.CropName}原料当前报价：{FormatCoins(details.RawPriceCents)} 金币";
        _stock.Text = $"{details.Crop.CropName}原料库存：{details.RawStock}";
    }

    /**
     * <summary>分别展示本田共享引用、实际剩余天数与预备日期。</summary>
     * <param name="game">只读经营入口。</param>
     * <param name="cell">本田的任一子格。</param>
     */
    public void RefreshCultivation(FarmGame game, Godot.Vector2I cell)
    {
        var state = game.GetFarmCultivation(cell);
        PlotSnapshot plot = game.GetPlot(cell);
        double remainingDays = Math.Ceiling(plot.RemainingSeconds *
            (double)FarmExchange.Time.GameTimeUnits.PerSecond /
            FarmExchange.Time.GameTimeUnits.PerDay * 10) / 10;
        string actual = plot.Crop == CropStage.Growing ? $"实际剩余 {remainingDays.ToString("0.0", CultureInfo.InvariantCulture)} 天" :
            plot.Crop == CropStage.Seeded ? "实际待水 · 完成日期尚未确定" : "实际未播种";
        double totalSeconds = FarmGame.GetCrop(plot.CropKind).GrowthDays *
            (double)FarmExchange.Time.GameTimeUnits.PerDay / FarmExchange.Time.GameTimeUnits.PerSecond;
        _progress.Visible = plot.Crop == CropStage.Growing;
        _progress.Value = plot.Crop == CropStage.Growing ?
            Math.Clamp((totalSeconds - plot.RemainingSeconds) / totalSeconds * 100, 0, 100) : 0;
        _remaining.Text = actual + (plot.HasWater ? " · 已有水分" : " · 尚无水分");
        string prepared = state.WaitingForGrowthStart ? "获水后确定预备日期" :
            state.PreparedCrop is CropKind crop ? $"预备：{FarmGame.GetCrop(crop).CropName}" :
            state.IsResting ? "计划休耕" : "暂无下一轮安排";
        if (state.PreparedTimeUnits is long units)
        {
            long day = units / FarmExchange.Time.GameTimeUnits.PerDay;
            if (units < 0)
            {
                uint annualDay = (uint)((day % 336 + 336) % 336);
                var date = FarmExchange.Time.GameCalendar.GetDate(annualDay);
                prepared += $" · 上一年度 {date.Month}月{date.Day}日";
            }
            else
            {
                var date = FarmExchange.Time.GameCalendar.GetDate((uint)day);
                prepared += $" · 第{date.Year}年 {date.Month}月{date.Day}日";
            }
        }
        _cultivation.Text = $"年度耕作表：{state.PlanName ?? "手动选种"}\n{prepared}";
    }
}
