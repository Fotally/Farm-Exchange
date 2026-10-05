using System;
using System.Globalization;
using Godot;
using FarmExchange.Gameplay;
using static FarmExchange.UI.UiElements;

namespace FarmExchange.UI;

public partial class FarmDetailsPanel : VBoxContainer
{
    private readonly Label _status;
    private readonly Label _title;
    private readonly Label _harvest;
    private readonly Label _water;
    private readonly FacilityPreview _preview;
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
        AddThemeConstantOverride("separation", 11);
        _preview = new FacilityPreview { Name = "FarmFacilityPreview" };
        AddChild(_preview);
        var heading = new HBoxContainer();
        _title = MakeLabel("", 27, Ink);
        _title.Name = "FarmCropTitle";
        _title.AutowrapMode = TextServer.AutowrapMode.Off;
        _title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        heading.AddChild(_title);
        _status = MakeLabel("", 14, Mid);
        _status.AutowrapMode = TextServer.AutowrapMode.Off;
        _status.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _status.Name = "FarmCurrentStatus";
        heading.AddChild(_status);
        AddChild(heading);
        _remaining = MakeLabel("", 14, Muted);
        _remaining.Name = "FarmActualProgress";
        AddChild(_remaining);
        _progress = new ProgressBar { Name = "FarmProgress", ShowPercentage = false, CustomMinimumSize = new Vector2(0, 6) };
        AddChild(_progress);
        var water = new HBoxContainer();
        water.AddChild(UiIcons.Create(UiIcon.Water, 25, Muted));
        _water = MakeLabel("", 14, Muted);
        _water.Name = "FarmWaterStatus";
        _water.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        water.AddChild(_water);
        AddChild(water);
        _growth = MakeLabel("", 12, Muted);
        AddChild(_growth);
        var metrics = new HBoxContainer();
        metrics.AddThemeConstantOverride("separation", 11);
        metrics.AddChild(MakeMetric("每轮收获", "份", "FarmHarvestQuantity", out _harvest));
        metrics.AddChild(MakeMetric("原料当前报价", "金币", "FarmRawPrice", out _price));
        AddChild(metrics);
        _stock = MakeLabel("", 12, Muted);
        AddChild(_stock);
        Button cultivation = MakeSecondaryButton("", 0, 72);
        cultivation.Name = "FarmCultivationButton";
        cultivation.Pressed += () => CultivationRequested?.Invoke();
        var planRow = new HBoxContainer { Name = "FarmCultivationRow", MouseFilter = MouseFilterEnum.Ignore };
        var planMargin = WrapMargin(cultivation, 12, 9);
        planMargin.Name = "FarmCultivationMargin";
        planMargin.MouseFilter = MouseFilterEnum.Ignore;
        planMargin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        planMargin.AddChild(planRow);
        var calendar = UiIcons.Create(UiIcon.Calendar, 27, Ink);
        calendar.Name = "FarmCultivationIcon";
        calendar.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        planRow.AddChild(calendar);
        _cultivation = MakeLabel("", 14, Ink);
        _cultivation.MouseFilter = MouseFilterEnum.Ignore;
        _cultivation.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _cultivation.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _cultivation.Name = "FarmCultivationStatus";
        planRow.AddChild(_cultivation);
        var arrow = UiIcons.Create(UiIcon.Arrow, 23, Ink);
        arrow.Name = "FarmCultivationArrow";
        arrow.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        planRow.AddChild(arrow);
        AddChild(cultivation);
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 11);
        Button change = MakeSecondaryButton("立即改种", 0, 56);
        change.AddThemeFontSizeOverride("font_size", 16);
        change.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        change.TooltipText = "解除共享表引用与本田安排，丢弃当前未收获作物，保留水分。";
        change.Name = "ChangeCropButton";
        change.Pressed += () => ChangeCropRequested?.Invoke();
        actions.AddChild(change);
        Button prepare = MakeButton("预备下一轮", Mid, 0, 56);
        prepare.AddThemeFontSizeOverride("font_size", 16);
        prepare.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        prepare.TooltipText = "解除共享表引用与本田安排，保留当前轮，结束后接续手动作物。";
        prepare.Name = "PrepareCropButton";
        prepare.Pressed += () => PrepareCropRequested?.Invoke();
        actions.AddChild(prepare);
        AddChild(actions);
        AddChild(MakeLabel("移除会丢失未收获作物，不退还建造费。", 12, Muted));
        Button remove = MakeQuietButton("移除农田 · 不退款", 0, 35);
        remove.AddThemeFontSizeOverride("font_size", 13);
        remove.Name = "RemoveButton";
        remove.Pressed += () => RemoveRequested?.Invoke();
        AddChild(remove);
    }

    public void Refresh(FarmDetailsSnapshot details)
    {
        string status = details.Status switch
        {
            FarmStatus.WaitingForWater => "待浇水",
            FarmStatus.WaitingForWorkerWithWater => "待播种",
            FarmStatus.Growing => "生长中",
            FarmStatus.WrongSeason => "待适季",
            FarmStatus.InsufficientTime => "枯萎风险",
            FarmStatus.Resting => "休耕中",
            _ => "待工人",
        };
        _title.Text = details.Crop.CropName;
        _status.Text = status;
        _growth.Text = $"生长周期：获得水后 {details.Crop.GrowthDays} 天成熟";
        if (details.Status == FarmStatus.WrongSeason)
            _growth.Text += "\n当前季节不适宜播种";
        else if (details.Status == FarmStatus.InsufficientTime)
            _growth.Text += "\n可播种 · 预计越过禁生边界，有枯萎风险";
        _harvest.Text = details.Crop.HarvestQuantity.ToString(CultureInfo.InvariantCulture);
        _price.Text = FormatCoins(details.RawPriceCents);
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
        _preview.Refresh(game.GetBuildingSpace(cell)!, plot.CropKind, plot.Crop);
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
        _remaining.Text = actual;
        _water.Text = plot.HasWater ? "水分充足 · 成熟自动入库" : "尚无水分 · 工人负责播种浇水";
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
