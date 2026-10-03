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

    public event Action? ChangeCropRequested;
    public event Action? PrepareCropRequested;
    public event Action? CultivationRequested;
    public event Action? RemoveRequested;

    public FarmDetailsPanel()
    {
        AddThemeConstantOverride("separation", 9);
        AddChild(MakeInfoCard("", out _status));
        AddChild(MakeInfoCard("", out _growth));
        AddChild(MakeInfoCard("", out _price));
        AddChild(MakeInfoCard("", out _stock));
        AddChild(MakeInfoCard("", out _cultivation));
        _cultivation.Name = "FarmCultivationStatus";
        Button change = MakeButton("立即改种 · 查看价格与库存", Mid, 0, 43);
        change.Name = "ChangeCropButton";
        change.Pressed += () => ChangeCropRequested?.Invoke();
        AddChild(change);
        Button prepare = MakeButton("预备下一轮 · 保留当前作物", Mid, 0, 43);
        prepare.Name = "PrepareCropButton";
        prepare.Pressed += () => PrepareCropRequested?.Invoke();
        AddChild(prepare);
        Button cultivation = MakeButton("查看共享年度耕作表", Mid, 0, 37);
        cultivation.Name = "FarmCultivationButton";
        cultivation.Pressed += () => CultivationRequested?.Invoke();
        AddChild(cultivation);
        Button remove = MakeButton("移除农田", new Color(0.66f, 0.36f, 0.31f), 0, 43);
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
        _growth.Text = $"生长周期：获得水后 {details.Crop.GrowthDays} 天成熟";
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
        _cultivation.Text = $"共享表：{state.PlanName ?? "手动选种"}\n{actual}\n{prepared}";
    }
}
