using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Land;
using FarmExchange.UI;
using FarmExchange.World;

public partial class TestFacilityThumbnails : Node
{
    public override async void _Ready()
    {
        try
        {
            bool passed = await RunChecksAsync(this);
            if (passed) GD.Print("正式加工与五材质道路缩略图、图文分区及原目录交互通过");
            GetTree().Quit(passed ? 0 : 1);
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    public static async Task<bool> RunChecksAsync(Node parent)
    {
        Vector2I originalSize = parent.GetWindow().Size;
        Main? main = null;
        try
        {
            parent.GetWindow().Size = new Vector2I(1920, 1080);
            if (!CheckCachedSourcesUnchanged()) return false;
            main = await CreateMain(parent);
            BuildCatalogWindow catalog = Find<BuildCatalogWindow>(main, "BuildWindow");
            if (!CheckSources(catalog) || !await CheckCatalog(parent, main, catalog)) return false;
            if (!await CheckRoadDetails(parent, main)) return false;
            for (int index = 0; index < FarmGame.Crops.Count; index++)
            {
                if (main.Game.AvailableMoneyCents < FarmGame.GetBuildingCostCents(BuildingKind.Processor))
                {
                    main.QueueFree();
                    await Frames(parent);
                    main = await CreateMain(parent);
                }
                CropKind crop = FarmGame.Crops[index].Kind;
                Vector2I anchor = new(201 + index * 3, 192);
                if (main.Game.BuildProcessor(anchor, crop) != null) return Fail("加工详情真实设施夹具建造失败");
                Select(main, anchor + Vector2I.One);
                await Frames(parent);
                FacilityPreview preview = Find<FacilityPreview>(main, "ProcessorFacilityPreview");
                if (!CheckPreview(preview, FacilityPreview.Texture(BuildingKind.Processor, crop), anchor, "3 × 3"))
                    return false;
                await Capture(parent, $"processor-{crop.ToString().ToLowerInvariant()}-1920.png");
            }
            return await CheckScaling(parent, main);
        }
        finally
        {
            main?.QueueFree();
            await Frames(parent);
            parent.GetWindow().Size = originalSize;
        }
    }

    private static bool CheckCachedSourcesUnchanged()
    {
        Texture2D dirt = FacilityPreview.Texture(BuildingKind.Road, default, roadMaterial: RoadMaterial.Dirt);
        Texture2D build = UiIcons.Texture(UiIcon.Build);
        using Image dirtBefore = dirt.GetImage();
        using Image buildBefore = build.GetImage();
        byte[] dirtBytes = dirtBefore.GetData();
        byte[] buildBytes = buildBefore.GetData();
        Vector2I dirtSize = new(dirtBefore.GetWidth(), dirtBefore.GetHeight());
        Vector2I buildSize = new(buildBefore.GetWidth(), buildBefore.GetHeight());
        FacilityPreview.RoadRemovalTexture();
        using Image dirtAfter = dirt.GetImage();
        using Image buildAfter = build.GetImage();
        return dirtBytes.SequenceEqual(dirtAfter.GetData()) && buildBytes.SequenceEqual(buildAfter.GetData()) &&
            dirtSize == new Vector2I(dirtAfter.GetWidth(), dirtAfter.GetHeight()) &&
            buildSize == new Vector2I(buildAfter.GetWidth(), buildAfter.GetHeight()) ||
            Fail("拆路图派生修改了缓存土路或原工具SVG的像素/尺寸");
    }

    private static async Task<Main> CreateMain(Node parent)
    {
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
        main.InitialTerrainRules = TerrainTestFixtures.AllLandRules;
        parent.AddChild(main);
        await main.ReadyTask;
        main.SetProcess(false);
        Button pause = Find<Button>(main, "PauseButton");
        pause.EmitSignal(Button.SignalName.Pressed);
        Label[] visibleLabels = main.FindChildren("*", "Label", true, false).OfType<Label>()
            .Where(label => label.IsVisibleInTree()).ToArray();
        if (!main.Game.IsPaused || pause.TooltipText != "继续经营" ||
            !ReferenceEquals(pause.Icon, UiIcons.Texture(UiIcon.Play)) ||
            !visibleLabels.Any(label => label.Text == "照料已暂停") ||
            !visibleLabels.Any(label => label.Text == "经营已暂停 · 仍可交易"))
        {
            main.QueueFree();
            throw new InvalidOperationException("缩略图截图夹具的经营暂停状态与顶部可见提示不一致");
        }
        main.GetNode<CameraController>("Camera2D").SetProcess(false);
        return main;
    }

    private static bool CheckSources(BuildCatalogWindow catalog)
    {
        var sources = new HashSet<string>();
        var pixelImages = new HashSet<string>();
        foreach (CropDefinition crop in FarmGame.Crops)
        {
            Button card = Find<Button>(catalog, $"ProcessorCard{crop.Kind}");
            string expected = $"res://assets/gameplay/buildings/{crop.Kind.ToString().ToLowerInvariant()}_workshop_q0.png";
            if (card.Icon?.ResourceName != expected || !sources.Add(card.Icon.ResourceName) ||
                card.TextureFilter != CanvasItem.TextureFilterEnum.Nearest ||
                card.Icon.GetSize() != new Vector2(145, 108) ||
                card.IconAlignment != HorizontalAlignment.Center || card.VerticalIconAlignment != VerticalAlignment.Top)
                return Fail("加工目录未使用七种独立正式 q0 图及统一最近邻图片区");
            using Image image = card.Icon.GetImage();
            using Image source = GD.Load<Texture2D>(expected).GetImage();
            var sourceColors = new HashSet<Color>();
            for (int y = 0; y < source.GetHeight(); y++)
                for (int x = 0; x < source.GetWidth(); x++) sourceColors.Add(source.GetPixel(x, y));
            for (int y = 0; y < image.GetHeight(); y++)
                for (int x = 0; x < image.GetWidth(); x++)
                    if (image.GetPixel(x, y).A > 0 && !sourceColors.Contains(image.GetPixel(x, y)))
                        return Fail("加工缩略图引入原图没有的插值颜色");
            if (!pixelImages.Add(Convert.ToBase64String(image.GetData())) || image.GetUsedRect().End.Y != 104)
                return Fail("加工图被共用，或内容未底对齐且保留四像素余量");
        }
        var roadImages = new HashSet<string>();
        foreach (RoadMaterial material in Enum.GetValues<RoadMaterial>())
        {
            Button card = RoadCard(catalog, material);
            if (card.Icon?.ResourceName != $"res://assets/gameplay/terrain/roads/road_{material.ToString().ToLowerInvariant()}_atlas.png" ||
                card.Icon.GetSize() != new Vector2(145, 108) || card.TextureFilter != CanvasItem.TextureFilterEnum.Nearest)
                return Fail("五材质目录未读取各自正式道路图集");
            using Image image = card.Icon.GetImage();
            Rect2I used = image.GetUsedRect();
            if (!roadImages.Add(Convert.ToBase64String(image.GetData())) || used.Size.X != 145 ||
                used.End.Y != 104 || used.Size.X <= used.Size.Y * 2)
                return Fail("道路材质共用，或三格短直道路形状/底对齐不正确");
        }
        using Image removal = Find<Button>(catalog, "RoadRemovalCard").Icon!.GetImage();
        using Image dirt = RoadCard(catalog, RoadMaterial.Dirt).Icon!.GetImage();
        if (removal.GetPixel(115, 74) != new Color("9c5542") ||
            removal.GetPixel(113, 72) != UiElements.Cream ||
            Convert.ToBase64String(removal.GetData()) == Convert.ToBase64String(dirt.GetData()))
            return Fail("拆路卡片缺少红色工具角标或仍与建造图相同");
        for (int y = 0; y < removal.GetHeight(); y++)
            for (int x = 0; x < removal.GetWidth(); x++)
                if ((x < 113 || y < 72 || y >= 104) && removal.GetPixel(x, y) != dirt.GetPixel(x, y))
                    return Fail("拆路角标以外的背景不是原土路三格短路段");
        if (Find<Button>(catalog, "FarmCard").Icon!.ResourcePath != "res://assets/ui/facilities/field_wheat.svg" ||
            FacilityPreview.CropTexture(CropKind.Wheat).ResourcePath != "res://assets/ui/facilities/crop_wheat.svg")
            return Fail("农田或选种图片被本次正式缩略图替换");
        return true;
    }

    private static async Task<bool> CheckCatalog(Node parent, Main main, BuildCatalogWindow catalog)
    {
        catalog.ShowRaised();
        Find<Button>(catalog, "ProcessorTab").EmitSignal(Button.SignalName.Pressed);
        await Frames(parent);
        if (Find<Button>(catalog, "FarmCard").Visible || RoadCard(catalog, RoadMaterial.Dirt).Visible ||
            FarmGame.Crops.Any(crop => !Find<Button>(catalog, $"ProcessorCard{crop.Kind}").Visible))
            return Fail("加工分类未保持原七项过滤");
        Button radish = Find<Button>(catalog, "ProcessorCardRadish");
        LineEdit search = Find<LineEdit>(catalog, "BuildSearch");
        search.Text = "腌制坊";
        catalog.RefreshCards();
        if (!radish.Visible || FarmGame.Crops.Where(crop => crop.Kind != CropKind.Radish).Any(crop =>
            Find<Button>(catalog, $"ProcessorCard{crop.Kind}").Visible)) return Fail("正式图替换破坏目录搜索");
        search.Text = "";
        catalog.RefreshCards();
        if (!ReferenceEquals(radish, Find<Button>(catalog, "ProcessorCardRadish"))) return Fail("刷新重建卡片");
        await Frames(parent);
        await Capture(parent, "catalog-processors-1920.png");
        ScrollContainer scroll = Find<ScrollContainer>(catalog, "BuildScroll");
        scroll.EnsureControlVisible(radish);
        await Frames(parent);
        if (!scroll.GetGlobalRect().Encloses(radish.GetGlobalRect())) return Fail("末排加工卡片不可滚动到达");
        Find<Button>(catalog, "RoadTab").EmitSignal(Button.SignalName.Pressed);
        await Frames(parent);
        if (FarmGame.Crops.Any(crop => Find<Button>(catalog, $"ProcessorCard{crop.Kind}").Visible) ||
            Enum.GetValues<RoadMaterial>().Any(material => !RoadCard(catalog, material).Visible))
            return Fail("道路分类丢失五材质或残留加工卡片");
        await Capture(parent, "catalog-roads-1920.png");
        int beforeMoney = main.Game.MoneyCents;
        int beforeBuildings = main.Game.GetBuildingSpaces().Count;
        CropKind selected = default;
        catalog.SelectionRequested += (_, crop) => selected = crop;
        Find<Button>(catalog, "ProcessorTab").EmitSignal(Button.SignalName.Pressed);
        radish.EmitSignal(Button.SignalName.Pressed);
        if (selected != CropKind.Radish || !Find<Button>(main, "CancelPlacementButton").Visible)
            return Fail("加工卡片未提交原作物选择意图");
        Find<Button>(main, "CancelPlacementButton").EmitSignal(Button.SignalName.Pressed);
        catalog.ShowRaised();
        Find<Button>(catalog, "RoadTab").EmitSignal(Button.SignalName.Pressed);
        RoadMaterial selectedRoad = default;
        catalog.RoadRequested += material => selectedRoad = material;
        foreach (RoadMaterial material in Enum.GetValues<RoadMaterial>())
        {
            RoadCard(catalog, material).EmitSignal(Button.SignalName.Pressed);
            if (selectedRoad != material || !Find<Button>(main, "CancelPlacementButton").Visible)
                return Fail("道路卡片未保持原材质选择意图");
            Find<Button>(main, "CancelPlacementButton").EmitSignal(Button.SignalName.Pressed);
            catalog.ShowRaised();
        }
        bool removalRequested = false;
        catalog.RoadRemovalRequested += () => removalRequested = true;
        Find<Button>(catalog, "RoadRemovalCard").EmitSignal(Button.SignalName.Pressed);
        Find<Button>(main, "CancelPlacementButton").EmitSignal(Button.SignalName.Pressed);
        return removalRequested && main.Game.MoneyCents == beforeMoney &&
            main.Game.GetBuildingSpaces().Count == beforeBuildings || Fail("目录选择意图被改动或预先扣费建造");
    }

    private static async Task<bool> CheckRoadDetails(Node parent, Main main)
    {
        foreach (RoadMaterial material in Enum.GetValues<RoadMaterial>())
        {
            Vector2I anchor = new(201 + (int)material, 201);
            if (!main.Game.TryPlaceRoads(new[] { anchor }, material).Success) return Fail("道路详情真实材质建造失败");
            await main.Game.WaitForNavigationAsync(parent.GetTree());
            Select(main, anchor);
            await Frames(parent);
            FacilityPreview preview = Find<FacilityPreview>(main, "RoadFacilityPreview");
            if (!CheckPreview(preview, RoadCard(Find<BuildCatalogWindow>(main, "BuildWindow"), material).Icon!, anchor, "1 × 1") ||
                Find<Label>(main, "RoadTitle").Text != UiElements.RoadMaterialName(material)) return false;
            await Capture(parent, $"road-{material.ToString().ToLowerInvariant()}-1920.png");
        }
        return true;
    }

    private static bool CheckPreview(FacilityPreview preview, Texture2D expected, Vector2I anchor, string footprint)
    {
        FacilityThumbnail art = Find<FacilityThumbnail>(preview, "FacilityThumbnail");
        Label caption = Find<Label>(preview, "FacilityPreviewPlacement");
        return preview.IsVisibleInTree() && ReferenceEquals(art.Texture, expected) &&
            art.TextureFilter == CanvasItem.TextureFilterEnum.Nearest &&
            caption.Text == $"{footprint} 基础格 · ({anchor.X}, {anchor.Y})" &&
            art.GetGlobalRect().End.Y <= caption.GetGlobalRect().Position.Y &&
            preview.GetGlobalRect().Encloses(art.GetGlobalRect()) && preview.GetGlobalRect().Encloses(caption.GetGlobalRect()) ||
            Fail("详情正式源、真实占地锚点或独立图文区域不正确");
    }

    private static async Task<bool> CheckScaling(Node parent, Main main)
    {
        BuildCatalogWindow catalog = Find<BuildCatalogWindow>(main, "BuildWindow");
        DraggableWindow detail = Find<DraggableWindow>(main, "DetailWindow");
        foreach ((float overall, float font) in new[] { (1f, 1f), (1.25f, 1f), (1f, 1.2f), (1.25f, 1.2f) })
        {
            UiScaling.SetOverallScale(catalog, overall);
            UiScaling.SetFontScale(catalog, font);
            UiScaling.SetOverallScale(detail, overall);
            UiScaling.SetFontScale(detail, font);
            await Frames(parent);
            FacilityPreview preview = Find<FacilityPreview>(main, "ProcessorFacilityPreview");
            FacilityThumbnail art = Find<FacilityThumbnail>(preview, "FacilityThumbnail");
            Label caption = Find<Label>(preview, "FacilityPreviewPlacement");
            if (art.GetGlobalRect().End.Y > caption.GetGlobalRect().Position.Y ||
                caption.Size.Y < caption.GetMinimumSize().Y ||
                !preview.GetGlobalRect().Encloses(caption.GetGlobalRect())) return Fail("倍率改变导致详情图文重叠或标签裁切");
            catalog.ShowRaised();
            Find<Button>(catalog, "ProcessorTab").EmitSignal(Button.SignalName.Pressed);
            await Frames(parent);
            foreach (CropDefinition crop in FarmGame.Crops)
            {
                Button card = Find<Button>(catalog, $"ProcessorCard{crop.Kind}");
                if (card.Size.Y < card.GetCombinedMinimumSize().Y ||
                    card.GetThemeConstant("icon_max_width") != (int)Math.Round(145 * overall) ||
                    card.GetThemeFontSize("font_size") != (int)Math.Round(12 * overall * font) ||
                    card.Text.Split('\n').Length != 3) return Fail("倍率改变破坏三行卡片或图片区尺寸");
            }
            await Capture(parent, $"catalog-overall-{overall:0.##}-font-{font:0.##}-1920.png");
            catalog.Hide();
            await Capture(parent, $"detail-overall-{overall:0.##}-font-{font:0.##}-1920.png");
        }
        UiScaling.SetOverallScale(catalog, 1);
        UiScaling.SetFontScale(catalog, 1);
        UiScaling.SetOverallScale(detail, 1);
        UiScaling.SetFontScale(detail, 1);
        return true;
    }

    private static void Select(Main main, Vector2I cell)
    {
        WorldMap map = main.GetNode<WorldMap>("WorldMap");
        CameraController camera = main.GetNode<CameraController>("Camera2D");
        camera.GlobalPosition = map.GetGridWorldPosition(cell);
        camera.ForceUpdateScroll();
        map.SyncFromGame();
        map.EmitSignal(WorldMap.SignalName.SelectionChanged, cell);
    }

    private static Button RoadCard(BuildCatalogWindow catalog, RoadMaterial material) =>
        Find<Button>(catalog, material == RoadMaterial.Dirt ? "RoadCard" : $"RoadCard{material}");

    private static async Task Frames(Node parent)
    {
        for (int frame = 0; frame < 3; frame++) await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static async Task Capture(Node parent, string name)
    {
        if (DisplayServer.GetName() == "headless") return;
        await Frames(parent);
        await parent.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        if (parent.GetViewport().GetVisibleRect().Size != new Vector2(1920, 1080))
            throw new InvalidOperationException("缩略图验收截图必须为1920×1080");
        string directory = ProjectSettings.GlobalizePath("res://build/issue149-validation");
        DirAccess.MakeDirRecursiveAbsolute(directory);
        Error saved = parent.GetViewport().GetTexture().GetImage().SavePng(directory + "/" + name);
        if (saved != Error.Ok) throw new InvalidOperationException($"缩略图截图保存失败：{saved}");
    }

    private static T Find<T>(Node node, string name) where T : Node =>
        node.FindChild(name, true, false) as T ?? throw new InvalidOperationException(name);

    private static bool Fail(string message) { GD.PushError(message); return false; }
}
