using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using FarmExchange.Cultivation;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
using FarmExchange.Land;
using FarmExchange.Time;
using FarmExchange.UI;
using FarmExchange.Weather;
using FarmExchange.World;

public partial class TestUiFacilities : Node
{
    public override async void _Ready()
    {
        bool passed = await RunChecksAsync(this);
        if (passed) GD.Print("设施目录搜索、作物说明与真实生产进度检查通过");
        GetTree().Quit(passed ? 0 : 1);
    }

    public static async Task<bool> RunChecksAsync(Node parent)
    {
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
        main.InitialTerrainRules = TerrainTestFixtures.AllLandRules;
        parent.AddChild(main);
        try
        {
            if (!CheckCatalog(main)) return false;
            await main.Game.WaitForNavigationAsync(parent.GetTree());
            return CheckProductionProgress(main) && CheckCompleteFarmRefresh(parent);
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
        Button[] roads = Enum.GetValues<RoadMaterial>().Select(material =>
            Find<Button>(catalog, material == RoadMaterial.Dirt ? "RoadCard" : $"RoadCard{material}")).ToArray();
        Button removal = Find<Button>(catalog, "RoadRemovalCard");
        if (!farm.Visible || !road.Visible || FarmGame.Crops.Any(c =>
            !Find<Button>(catalog, $"ProcessorCard{c.Kind}").Visible))
            return Fail("默认全部分类没有呈现农田与七加工设施");
        if (roads.Any(card => !card.Visible || !card.Text.Contains("1.00 金币/格")) || !removal.Visible)
            return Fail("默认全部分类没有呈现五材质同价道路与拆路工具");
        string[] roadNames = { "土路", "碎石道路", "卵石道路", "红砖道路", "石板道路" };
        RoadDetailsPanel details = Find<RoadDetailsPanel>(main, "RoadDetailsPanel");
        foreach (RoadMaterial material in Enum.GetValues<RoadMaterial>())
        {
            Vector2I cell = new(20 + (int)material, 20);
            if (!roads[(int)material].Text.StartsWith(roadNames[(int)material], StringComparison.Ordinal) ||
                !main.Game.TryPlaceRoads(new[] { cell }, material).Success)
                return Fail("道路目录中文名称不正确或详情材质夹具失败");
            details.RefreshPlacement(main.Game.GetBuildingSpace(cell)!);
            if (Find<Label>(details, "RoadTitle").Text != roadNames[(int)material])
                return Fail("道路详情与目录的中文名称不一致");
            main.Game.TryRemoveRoads(new[] { cell });
        }
        foreach (RoadMaterial invalid in new[] { (RoadMaterial)(-1), (RoadMaterial)99 })
        {
            try { UiElements.RoadMaterialName(invalid); return Fail("无效材质被转换为UI名称"); }
            catch (ArgumentOutOfRangeException error)
            {
                if (error.ParamName != "material") return Fail("UI名称转换改变无效值异常参数名");
            }
        }
        LineEdit search = Find<LineEdit>(catalog, "BuildSearch");
        search.Text = "农田";
        catalog.RefreshCards();
        if (!farm.Visible || roads.Any(card => card.Visible) || removal.Visible || FarmGame.Crops.Any(c =>
            Find<Button>(catalog, $"ProcessorCard{c.Kind}").Visible))
            return Fail("全类名称搜索未筛出唯一农田");
        search.Text = "道路";
        catalog.RefreshCards();
        if (farm.Visible || roads.Any(card => !card.Visible) || !removal.Visible)
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
        for (int tick = 0; tick < 12; tick++) game.AdvanceTick();
        TestMapInput.ShortClickCell(map, farm);
        PlotSnapshot growing = game.GetPlot(farm);
        Label actual = Find<Label>(main, "FarmActualProgress");
        ProgressBar growth = Find<ProgressBar>(main, "FarmProgress");
        if (growing.Crop != CropStage.Growing || !growth.Visible ||
            !actual.Text.Contains($"实际剩余 {Days(growing.RemainingSeconds)} 天"))
            return Fail("农田详情未使用真实本轮剩余天数");
        Find<Button>(main, "ChangeCropButton").EmitSignal(Button.SignalName.Pressed);
        Button wheat = Find<Button>(main, "CropCardWheat");
        if (!wheat.Text.Contains("适宜：春 / 秋 / 冬") ||
            !wheat.Text.Contains("获水后 16 天") || !wheat.Text.Contains("基准产量 2 份") ||
            Find<Label>(main, "FarmCurrentWeather").Text != "当前天气：晴天")
            return Fail("作物卡缺少真实季节、时长或收获量");
        var cropWindow = Find<CropSelectionWindow>(main, "CropWindow");
        Find<Button>(cropWindow, "CloseButton").EmitSignal(Button.SignalName.Pressed);

        Vector2I processor = game.GetBuildingSpaces().First(s => s.Building == BuildingKind.Processor).AnchorCell;
        CropKind crop = game.GetPlot(processor).CropKind;
        if (!game.Buy(new CommodityId(crop, CommodityKind.Raw), 1).Success)
            return Fail("加工进度夹具买入原料失败");
        game.AdvanceTick();
        TestMapInput.ShortClickCell(map, processor);
        PlotSnapshot processing = game.GetPlot(processor);
        Label remaining = Find<Label>(main, "ProcessorActualProgress");
        if (processing.RemainingSeconds <= 0 || !Find<ProgressBar>(main, "ProcessorProgress").Visible ||
            !remaining.Text.Contains($"实际剩余 {Days(processing.RemainingSeconds)} 天") ||
            game.GetProcessorDetails(processor).BatchQuantity != 1 ||
            !Find<Label>(main, "ProcessorRelation").Text.Contains("本批投入 1 份"))
            return Fail("加工详情未显示真实少量批次和进度");
        string pausedText = remaining.Text;
        double pausedValue = Find<ProgressBar>(main, "ProcessorProgress").Value;
        game.SetPaused(true);
        main.AdvanceSimulation(1);
        if (remaining.Text != pausedText || Find<ProgressBar>(main, "ProcessorProgress").Value != pausedValue ||
            game.GetPlot(processor).RemainingSeconds != processing.RemainingSeconds)
            return Fail("暂停刷新推进或虚构了加工进度");
        game.SetPaused(false);
        for (int tick = 0; tick < 8; tick++) game.AdvanceTick();
        TestMapInput.ShortClickCell(map, processor);
        return remaining.Text.Contains($"实际剩余 {Days(game.GetPlot(processor).RemainingSeconds)} 天") &&
            Find<ProgressBar>(main, "ProcessorProgress").Value > pausedValue ||
            Fail("经营推进后加工详情没有更新真实剩余和进度条");
    }

    private static bool CheckCompleteFarmRefresh(Node parent)
    {
        using var game = new FarmGame(12345, weatherMode: WeatherMode.Explicit,
            terrainRules: TerrainTestFixtures.AllLandRules);
        var panel = new FarmDetailsPanel();
        parent.AddChild(panel);
        try
        {
            Vector2I first = new(189, 189), second = new(192, 189), resting = new(195, 189);
            if (game.SetFarmCrop(first, CropKind.Radish) != null ||
                game.SetFarmCrop(second, CropKind.Wheat) != null ||
                game.SetFarmCrop(resting, CropKind.Wheat) != null)
                return Fail("完整农田刷新夹具选种失败");
            var plan = game.CreateCultivationPlan(new CultivationPlanRequest("详情休耕", CultivationMode.Immediate,
                Array.Empty<CultivationEntry>()));
            if (!plan.Success || game.ApplyCultivationPlan(plan.Id, new[] { resting }) != null)
                return Fail("完整农田刷新夹具休耕失败");
            if (!CheckFarmPanel(panel, game, resting + Vector2I.One, "休耕中", "计划休耕") ||
                !Find<Label>(panel, "FarmCultivationStatus").Text.Contains("详情休耕"))
                return Fail("完整刷新未展示同一农田的休耕引用");
            if (!CheckFarmPanel(panel, game, first, "待工人", "暂无下一轮安排")) return false;

            game.AdvanceTick();
            if (game.GetPlot(first).Crop != CropStage.Seeded ||
                !CheckFarmPanel(panel, game, first + new Vector2I(2, 2), "待浇水", "暂无下一轮安排") ||
                Find<Label>(panel, "FarmActualProgress").Text != "实际待水 · 完成日期尚未确定")
                return Fail("完整刷新未同时更新子格对应的待水状态与未定完成日期");
            game.AdvanceTick();
            if (game.GetPlot(first).Crop != CropStage.Growing ||
                game.PrepareFarmCrop(first, CropKind.Corn) != null ||
                !CheckFarmPanel(panel, game, first + Vector2I.One, "生长中", "预备：玉米"))
                return Fail("完整刷新未同时更新生长进度与下一轮安排");
            string remaining = Find<Label>(panel, "FarmActualProgress").Text;
            if (!remaining.Contains($"实际剩余 {Days(game.GetPlot(first).RemainingSeconds)} 天") ||
                !CheckFarmPanel(panel, game, second, "生长中", "暂无下一轮安排"))
                return Fail("切换不同农田后仍保留前田的作物或预备安排");

            if (game.RemoveBuilding(first) != null || !game.TryPlace(first, BuildingKind.Farm, CropKind.Potato).Success ||
                !CheckFarmPanel(panel, game, first + Vector2I.One, "待工人", "暂无下一轮安排"))
                return Fail("拆除重建后完整刷新仍保留旧作物、进度或安排");
            return true;
        }
        finally { panel.Free(); }
    }

    private static bool CheckFarmPanel(FarmDetailsPanel panel, FarmGame game, Vector2I cell,
        string status, string cultivation)
    {
        PlotSnapshot plot = game.GetPlot(cell);
        FarmDetailsSnapshot details = game.GetFarmDetails(cell);
        Vector2I anchor = game.GetBuildingSpace(cell)!.AnchorCell;
        uint elapsed = game.Calendar.ElapsedSeconds;
        panel.Refresh(game, cell);
        FacilityPreview preview = Find<FacilityPreview>(panel, "FarmFacilityPreview");
        TextureRect art = preview.FindChildren("*", "TextureRect", true, false).OfType<TextureRect>().Single();
        return Find<Label>(panel, "FarmCropTitle").Text == details.Crop.CropName &&
            Find<Label>(panel, "FarmCurrentStatus").Text == status &&
            Find<Label>(panel, "FarmRawPrice").Text == UiElements.FormatCoins(details.RawPriceCents) &&
            Find<Label>(panel, "FarmHarvestQuantity").Text == details.Crop.HarvestQuantity.ToString(CultureInfo.InvariantCulture) &&
            Find<Label>(panel, "FarmWaterStatus").Text.Contains(plot.HasWater ? "水分充足" : "尚无水分") &&
            Find<Label>(panel, "FarmCultivationStatus").Text.Contains(cultivation) &&
            Find<ProgressBar>(panel, "FarmProgress").Visible == (plot.Crop == CropStage.Growing) &&
            Find<Label>(preview, "FacilityPreviewPlacement").Text == $"3 × 3 基础格 · ({anchor.X}, {anchor.Y})" &&
            ReferenceEquals(art.Texture, FacilityPreview.Texture(BuildingKind.Farm, plot.CropKind, plot.Crop)) &&
            game.Calendar.ElapsedSeconds == elapsed && game.GetPlot(cell) == plot ||
            Fail("单次完整刷新未保持同一农田的文字、进度、安排和缩略图，或修改了经营状态");
    }

    private static string Days(int seconds) =>
        (Math.Ceiling(seconds * (double)GameTimeUnits.PerSecond / GameTimeUnits.PerDay * 10) / 10)
            .ToString("0.0", CultureInfo.InvariantCulture);

    private static T Find<T>(Node node, string name) where T : Node =>
        node.FindChild(name, true, false) as T ?? throw new InvalidOperationException(name);

    private static bool Fail(string text) { GD.PushError(text); return false; }
}
