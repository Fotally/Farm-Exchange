using System;
using System.Globalization;
using System.Linq;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
using FarmExchange.Time;
using FarmExchange.UI;
using FarmExchange.World;

public partial class TestUiFacilities : Node
{
    public override void _Ready()
    {
        bool passed = RunChecks(this);
        if (passed) GD.Print("设施目录搜索、作物说明与真实生产进度检查通过");
        GetTree().Quit(passed ? 0 : 1);
    }

    public static bool RunChecks(Node parent)
    {
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
        parent.AddChild(main);
        try
        {
            return CheckCatalog(main) && CheckProductionProgress(main);
        }
        finally
        {
            main.QueueFree();
        }
    }

    private static bool CheckCatalog(Main main)
    {
        BuildCatalogWindow catalog = Find<BuildCatalogWindow>(main, "BuildWindow");
        Button farm = Find<Button>(catalog, "FarmCard");
        Button road = Find<Button>(catalog, "RoadCard");
        if (!farm.Visible || !road.Visible || FarmGame.Crops.Any(c =>
            !Find<Button>(catalog, $"ProcessorCard{c.Kind}").Visible))
            return Fail("默认全部分类没有呈现九项建筑");
        LineEdit search = Find<LineEdit>(catalog, "BuildSearch");
        search.Text = "农田";
        catalog.RefreshCards();
        if (!farm.Visible || road.Visible || FarmGame.Crops.Any(c =>
            Find<Button>(catalog, $"ProcessorCard{c.Kind}").Visible))
            return Fail("全类名称搜索未筛出唯一农田");
        search.Text = "道路";
        catalog.RefreshCards();
        if (farm.Visible || !road.Visible)
            return Fail("全类搜索没有匹配道路");
        search.Text = "腌制坊";
        catalog.RefreshCards();
        Button radish = Find<Button>(catalog, "ProcessorCardRadish");
        if (!radish.Visible || farm.Visible || road.Visible)
            return Fail("全类搜索没有匹配加工场地");
        Find<Button>(catalog, "ProcessorTab").EmitSignal(Button.SignalName.Pressed);
        catalog.RefreshCards();
        if (search.Text != "腌制坊" || !radish.Visible ||
            !ReferenceEquals(radish, Find<Button>(catalog, "ProcessorCardRadish")))
            return Fail("分类切换或刷新丢失搜索草稿与卡片身份");
        return true;
    }

    private static bool CheckProductionProgress(Main main)
    {
        FarmGame game = main.Game;
        WorldMap map = main.GetNode<WorldMap>("WorldMap");
        Vector2I farm = game.GetBuildingSpaces().First(s => s.Building == BuildingKind.Farm).AnchorCell;
        game.SetFarmCrop(farm, CropKind.Wheat);
        for (int tick = 0; tick < 12; tick++) game.AdvanceTick(isRaining: true);
        map.EmitSignal(WorldMap.SignalName.SelectionChanged, farm);
        PlotSnapshot growing = game.GetPlot(farm);
        Label actual = Find<Label>(main, "FarmActualProgress");
        ProgressBar growth = Find<ProgressBar>(main, "FarmProgress");
        if (growing.Crop != CropStage.Growing || !growth.Visible ||
            !actual.Text.Contains($"实际剩余 {Days(growing.RemainingSeconds)} 天"))
            return Fail("农田详情未使用真实本轮剩余天数");
        Find<Button>(main, "ChangeCropButton").EmitSignal(Button.SignalName.Pressed);
        Button wheat = Find<Button>(main, "CropCardWheat");
        if (!wheat.Text.Contains("适宜：春 / 秋 / 冬") ||
            !wheat.Text.Contains("获水后 16 天") || !wheat.Text.Contains("每轮 2 份"))
            return Fail("作物卡缺少真实季节、时长或收获量");
        var cropWindow = Find<CropSelectionWindow>(main, "CropWindow");
        Find<Button>(cropWindow, "CloseButton").EmitSignal(Button.SignalName.Pressed);

        Vector2I processor = game.GetBuildingSpaces().First(s => s.Building == BuildingKind.Processor).AnchorCell;
        CropKind crop = game.GetPlot(processor).CropKind;
        if (!game.Buy(new CommodityId(crop, CommodityKind.Raw), 1).Success)
            return Fail("加工进度夹具买入原料失败");
        game.AdvanceTick();
        map.EmitSignal(WorldMap.SignalName.SelectionChanged, processor);
        PlotSnapshot processing = game.GetPlot(processor);
        Label remaining = Find<Label>(main, "ProcessorActualProgress");
        if (processing.RemainingSeconds <= 0 || !Find<ProgressBar>(main, "ProcessorProgress").Visible ||
            !remaining.Text.Contains($"实际剩余 {Days(processing.RemainingSeconds)} 天"))
            return Fail("加工详情未显示真实批次进度");
        string pausedText = remaining.Text;
        double pausedValue = Find<ProgressBar>(main, "ProcessorProgress").Value;
        game.SetPaused(true);
        main.GetNode<Timer>("TickTimer").EmitSignal(Timer.SignalName.Timeout);
        if (remaining.Text != pausedText || Find<ProgressBar>(main, "ProcessorProgress").Value != pausedValue ||
            game.GetPlot(processor).RemainingSeconds != processing.RemainingSeconds)
            return Fail("暂停刷新推进或虚构了加工进度");
        game.SetPaused(false);
        for (int tick = 0; tick < 8; tick++) game.AdvanceTick();
        map.EmitSignal(WorldMap.SignalName.SelectionChanged, processor);
        return remaining.Text.Contains($"实际剩余 {Days(game.GetPlot(processor).RemainingSeconds)} 天") &&
            Find<ProgressBar>(main, "ProcessorProgress").Value > pausedValue ||
            Fail("经营推进后加工详情没有更新真实剩余和进度条");
    }

    private static string Days(int seconds) =>
        (Math.Ceiling(seconds * (double)GameTimeUnits.PerSecond / GameTimeUnits.PerDay * 10) / 10)
            .ToString("0.0", CultureInfo.InvariantCulture);

    private static T Find<T>(Node node, string name) where T : Node =>
        node.FindChild(name, true, false) as T ?? throw new InvalidOperationException(name);

    private static bool Fail(string text) { GD.PushError(text); return false; }
}
