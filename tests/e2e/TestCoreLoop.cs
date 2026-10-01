using System.Globalization;
using System.Threading.Tasks;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
using FarmExchange.Market;
using FarmExchange.UI;
using FarmExchange.World;

public partial class TestCoreLoop : Node
{
    public override async void _Ready()
    {
        bool passed = RunChecks(this) && await RunLayoutChecks(this);
        if (passed)
            GD.Print("主场景建造与窗口操作检查通过");
        GetTree().Quit(passed ? 0 : 1);
    }

    public static bool RunChecks(Node parent)
    {
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
        parent.AddChild(main);
        bool passed = Check(main);
        main.QueueFree();
        return passed && CheckFootprintUi(parent) && CheckRainUi(parent) && CheckReserveUi(parent) && CheckRoadUi(parent) &&
            CheckMarketUi(parent);
    }

    public static async Task<bool> RunLayoutChecks(Node parent)
    {
        var window = new InventoryWindow();
        parent.AddChild(window);
        window.Refresh(new FarmGame(12345));
        window.ShowRaised();
        await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        bool passed = true;
        foreach (CropDefinition crop in FarmGame.Crops)
        {
            Label label = Find<Label>(window, $"RawReserve{crop.Kind}Label");
            LineEdit input = Find<LineEdit>(window, $"RawReserve{crop.Kind}Input");
            Button apply = Find<Button>(window, $"SetRawReserve{crop.Kind}Button");
            Rect2 labelRect = label.GetGlobalRect();
            Rect2 inputRect = input.GetGlobalRect();
            Rect2 buttonRect = apply.GetGlobalRect();
            if (label.GetLineCount() != 1 || input.Size.Y < 36 || input.Size.Y > 40 ||
                apply.Size.Y < 36 || apply.Size.Y > 40 || labelRect.End.X > inputRect.Position.X ||
                inputRect.End.X > buttonRect.Position.X)
            {
                passed = Fail($"{crop.CropName}保留底线布局发生换行、异常高度或重叠");
                break;
            }
        }
        ScrollContainer scroll = Find<ScrollContainer>(window, "InventoryScroll");
        LineEdit lastInput = Find<LineEdit>(window, "RawReserveRadishInput");
        scroll.EnsureControlVisible(lastInput);
        await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        Rect2 viewportRect = scroll.GetGlobalRect();
        Rect2 lastRect = lastInput.GetGlobalRect();
        if (scroll.ScrollVertical <= 0 || lastRect.Position.Y < viewportRect.Position.Y ||
            lastRect.End.Y > viewportRect.End.Y)
            passed = Fail("库存正常滚动无法完整显示最后一种作物的输入框");
        window.QueueFree();
        return passed && await CheckMarketLayout(parent);
    }

    private static async Task<bool> CheckMarketLayout(Node parent)
    {
        var game = new FarmGame(12345);
        for (int i = 0; i < 669; i++) game.AdvanceTick();
        var window = new MarketWindow();
        parent.AddChild(window);
        window.Refresh(game);
        window.ShowRaised();
        await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        bool passed = true;
        Rect2 windowRect = window.GetGlobalRect();
        Rect2 viewport = parent.GetViewport().GetVisibleRect();
        if (windowRect.Position.Y < 70 || windowRect.End.Y > viewport.End.Y - 109 ||
            windowRect.Position.X < 0 || windowRect.End.X > viewport.End.X)
            passed = Fail("市场窗口溢出1280×720可用区域或覆盖顶部/底部操作");
        var scroll = Find<ScrollContainer>(window, "MarketScroll");
        var newsScroll = Find<ScrollContainer>(window, "MarketNewsScroll");
        var input = Find<LineEdit>(window, "MarketQuantityInput");
        var controls = Find<Control>(window, "MarketTradeControls");
        Rect2 tradeRect = controls.GetGlobalRect();
        if (newsScroll.Size.Y > 65 || scroll.Size.Y < 120 ||
            tradeRect.Position.Y < scroll.GetGlobalRect().End.Y ||
            tradeRect.End.Y > windowRect.End.Y || input.Size.Y < 36)
            passed = Fail("市场消息挤出表格或数量操作，或操作区发生重叠");
        Button last = Find<Button>(window, "CommodityRadishProductButton");
        scroll.EnsureControlVisible(last);
        await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        Rect2 rowRect = last.GetGlobalRect();
        Rect2 scrollRect = scroll.GetGlobalRect();
        if (scroll.ScrollVertical <= 0 || rowRect.Position.Y < scrollRect.Position.Y ||
            rowRect.End.Y > scrollRect.End.Y)
            passed = Fail("行情表正常滚动无法完整展示最后一个商品");
        foreach (string name in new[] { "BuyCommodityButton", "SellCommodityButton", "SellCommodityAllButton", "SellButton" })
        {
            Rect2 buttonRect = Find<Button>(window, name).GetGlobalRect();
            if (!windowRect.Encloses(buttonRect))
                passed = Fail("市场交易按钮超出窗口：" + name);
        }
        if (game.GetMarketSnapshot().News is not MarketNewsSnapshot news ||
            !Find<Label>(window, "MarketNews").Text.Contains(news.Lines[^1]))
            passed = Fail("真实已公布因素消息未完整写入独立滚动区");
        window.QueueFree();
        return passed;
    }

    private static bool CheckMarketUi(Node parent)
    {
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
        parent.AddChild(main);
        bool passed = CheckMarketControls(main);
        main.QueueFree();
        return passed;
    }

    private static bool CheckMarketControls(Main main)
    {
        var ui = main.GetNode<Control>("CanvasLayer/UiRoot");
        Find<Button>(ui, "MarketButton").EmitSignal(Button.SignalName.Pressed);
        var window = Find<MarketWindow>(ui, "MarketWindow");
        var input = Find<LineEdit>(window, "MarketQuantityInput");
        var buy = Find<Button>(window, "BuyCommodityButton");
        var sell = Find<Button>(window, "SellCommodityButton");
        var all = Find<Button>(window, "SellCommodityAllButton");
        var timer = main.GetNode<Timer>("TickTimer");
        foreach (MarketQuoteSnapshot quote in main.Game.GetMarketSnapshot().Quotes)
        {
            string prefix = $"Quote{quote.Id.Crop}{quote.Id.Kind}";
            if (Find<Label>(window, prefix + "Price").Text !=
                    (quote.PriceCents / 100m).ToString("0.00", CultureInfo.InvariantCulture) ||
                Find<Label>(window, prefix + "Previous").Text !=
                    (quote.PreviousPriceCents / 100m).ToString("0.00", CultureInfo.InvariantCulture) ||
                Find<Label>(window, prefix + "Change").Text !=
                    quote.ChangePercent.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + "%" ||
                Find<Label>(window, prefix + "Stock").Text != main.Game.GetStock(quote.Id).ToString(CultureInfo.InvariantCulture))
                return Fail("十四商品的当前/上次报价、涨跌或库存未读取真实经营快照");
        }
        CommodityId raw = new(CropKind.Radish, CommodityKind.Raw);
        Find<Button>(window, "CommodityRadishRawButton").EmitSignal(Button.SignalName.Pressed);
        EnterText(input, "123");
        input.GrabFocus();
        input.CaretColumn = 2;
        var scroll = Find<ScrollContainer>(window, "MarketScroll");
        scroll.ScrollVertical = 30;
        int scrollPosition = scroll.ScrollVertical;
        timer.EmitSignal(Timer.SignalName.Timeout);
        if (input.Text != "123" || !input.HasFocus() || input.CaretColumn != 2 ||
            scroll.ScrollVertical != scrollPosition ||
            Find<Label>(window, "SelectedCommodityLabel").Text != "萝卜原料" ||
            !object.ReferenceEquals(input, Find<LineEdit>(window, "MarketQuantityInput")))
            return Fail("行情刷新丢失所选商品、数量草稿、焦点、光标或滚动");
        input.ReleaseFocus();
        Find<Button>(window, "CloseButton").EmitSignal(Button.SignalName.Pressed);
        Find<Button>(ui, "MarketButton").EmitSignal(Button.SignalName.Pressed);
        if (input.Text != "123" || Find<Label>(window, "SelectedCommodityLabel").Text != "萝卜原料")
            return Fail("市场关闭重开丢失数量草稿或商品选择");
        foreach (string invalid in new[] { "", "0", "-1", "1.5", "2147483648" })
        {
            int balance = main.Game.MoneyCents;
            int stock = main.Game.GetStock(raw);
            EnterText(input, invalid);
            buy.EmitSignal(Button.SignalName.Pressed);
            if (main.Game.MoneyCents != balance || main.Game.GetStock(raw) != stock ||
                input.Text != invalid || !ContainsVisibleText(window, "请输入 1 到 2147483647"))
                return Fail("非法交易数量改变经营状态或缺少错误反馈");
        }
        EnterText(input, "1000000");
        buy.EmitSignal(Button.SignalName.Pressed);
        if (!ContainsVisibleText(window, "金币不足"))
            return Fail("资金不足没有显示正常失败原因");
        main.Game.SetPaused(true);
        uint pausedSecond = main.Game.Calendar.ElapsedSeconds;
        int before = main.Game.MoneyCents;
        int beforeStock = main.Game.GetStock(raw);
        int price = main.Game.GetQuote(raw).PriceCents;
        EnterText(input, "2");
        buy.EmitSignal(Button.SignalName.Pressed);
        timer.EmitSignal(Timer.SignalName.Timeout);
        if (main.Game.MoneyCents != before - 2 * price || main.Game.GetStock(raw) != beforeStock + 2 ||
            main.Game.Calendar.ElapsedSeconds != pausedSecond || input.Text != "2")
            return Fail("暂停买入未按当前报价进入公共库存，或推进了经营");
        sell.EmitSignal(Button.SignalName.Pressed);
        if (main.Game.MoneyCents != before || main.Game.GetStock(raw) != beforeStock)
            return Fail("同价卖出未恢复买入前的余额和公共库存");
        EnterText(input, "2147483647");
        sell.EmitSignal(Button.SignalName.Pressed);
        if (main.Game.MoneyCents != before || main.Game.GetStock(raw) != beforeStock ||
            !ContainsVisibleText(window, "公共库存不足"))
            return Fail("库存不足没有零修改拒绝或没有反馈");
        main.Game.SetPaused(false);
        for (int i = 0; i < 724; i++) main.Game.AdvanceTick();
        main.Game.SetPaused(true);
        before = main.Game.MoneyCents;
        beforeStock = main.Game.GetStock(raw);
        price = main.Game.GetQuote(raw).PriceCents;
        EnterText(input, "1");
        buy.EmitSignal(Button.SignalName.Pressed);
        if (main.Game.MoneyCents != before - price || main.Game.GetStock(raw) != beforeStock + 1 ||
            Find<Label>(window, "QuoteRadishRawPrice").Text != (price / 100m).ToString("0.00", CultureInfo.InvariantCulture))
            return Fail("行情变化后成交使用了窗口旧报价或未刷新生效价");
        int quantity = main.Game.GetStock(raw);
        before = main.Game.MoneyCents;
        all.EmitSignal(Button.SignalName.Pressed);
        if (main.Game.GetStock(raw) != 0 || main.Game.MoneyCents != before + quantity * price || !all.Disabled)
            return Fail("该商品全售没有结算同一公共库存或更新按钮");
        CommodityId product = new(CropKind.Radish, CommodityKind.Product);
        Find<Button>(window, "CommodityRadishProductButton").EmitSignal(Button.SignalName.Pressed);
        beforeStock = main.Game.GetStock(product);
        EnterText(input, "1");
        buy.EmitSignal(Button.SignalName.Pressed);
        if (main.Game.GetStock(product) != beforeStock + 1)
            return Fail("加工品未接入选中商品数量买入");
        sell.EmitSignal(Button.SignalName.Pressed);
        return main.Game.GetStock(product) == beforeStock || Fail("加工品未接入选中商品数量卖出");
    }

    private static bool CheckFootprintUi(Node parent)
    {
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
        parent.AddChild(main);
        try
        {
            var game = main.Game;
            var ui = main.GetNode<Control>("CanvasLayer/UiRoot");
            var map = main.GetNode<WorldMap>("WorldMap");
            var detail = Find<Control>(ui, "DetailWindow");
            Vector2I[] opening = { new(189, 189), new(192, 189), new(195, 189), new(189, 192), new(192, 192) };
            if (game.GetBuildingSpaces().Count != opening.Length || game.MoneyCents != 5000)
                return Fail("主场景开局设施数量或免费费用错误");
            foreach (Vector2I anchor in opening)
            {
                var space = game.GetBuildingSpace(anchor);
                if (space == null || space.AnchorCell != anchor || space.Footprint.Offsets.Count != 9)
                    return Fail("主场景开局布局未按标准田跨度放置完整3×3设施");
                foreach (Vector2I offset in space.Footprint.Offsets)
                {
                    map.EmitSignal(WorldMap.SignalName.SelectionChanged, anchor + offset);
                    if (!ContainsVisibleText(detail, $"建筑锚点 ({anchor.X}, {anchor.Y}) · 占地 9 格") ||
                        game.GetPlot(anchor + offset) != game.GetPlot(anchor))
                        return Fail("主场景开局任一子格没有显示同一设施详情");
                }
            }
            for (int i = 0; i < 3; i++)
                if (game.GetWorkers()[i].GridPosition != (Vector2)(opening[i] + Vector2I.One))
                    return Fail("主场景三工人未出生在各田中央小格");
            Find<Button>(ui, "BuildButton").EmitSignal(Button.SignalName.Pressed);
            var buildWindow = Find<Control>(ui, "BuildWindow");
            if (!Find<Button>(buildWindow, "FarmCard").Text.Contains("3×3"))
                return Fail("建造目录未说明生产设施占地");
            Find<Button>(buildWindow, "FarmCard").EmitSignal(Button.SignalName.Pressed);
            Vector2I farm = new(188, 198); // 任意小格锚点，无需按三格对齐。
            map.EmitSignal(WorldMap.SignalName.SelectionChanged, farm);
            if (game.MoneyCents != 4000 || game.GetBuildingSpace(farm)?.AnchorCell != farm)
                return Fail("非三格对齐锚点未建造一次并只收一座费用");
            map.EmitSignal(WorldMap.SignalName.SelectionChanged, farm + new Vector2I(2, 1));
            Find<Button>(detail, "ChangeCropButton").EmitSignal(Button.SignalName.Pressed);
            Find<Button>(Find<Control>(ui, "CropWindow"), "CropCardRadish").EmitSignal(Button.SignalName.Pressed);
            foreach (Vector2I offset in game.GetBuildingSpace(farm)!.Footprint.Offsets)
            {
                map.EmitSignal(WorldMap.SignalName.SelectionChanged, farm + offset);
                if (game.GetPlot(farm + offset).CropKind != CropKind.Radish ||
                    !ContainsVisibleText(detail, "获得水后 4 天成熟") ||
                    !ContainsVisibleText(detail, "建筑锚点 (188, 198) · 占地 9 格"))
                    return Fail("子格改种后九格没有保持同一作物与详情");
            }
            Find<Button>(detail, "RemoveButton").EmitSignal(Button.SignalName.Pressed);
            for (int row = 0; row < 3; row++)
                for (int col = 0; col < 3; col++)
                    if (game.GetPlot(farm + new Vector2I(col, row)).Building != BuildingKind.None)
                        return Fail("末端子格拆除后仍残留同座设施");
            if (game.MoneyCents != 4000 || !ContainsVisibleText(detail, "空地"))
                return Fail("整座拆除退款或未恢复空地详情");
            int redraws = map.ChunkRedrawCount;
            Find<Button>(ui, "InventoryButton").EmitSignal(Button.SignalName.Pressed);
            if (map.ChunkRedrawCount != redraws)
                return Fail("只读打开库存重建了地块");
            return true;
        }
        finally
        {
            main.QueueFree();
        }
    }

    private static bool CheckRainUi(Node parent)
    {
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
        parent.AddChild(main);
        main.Game.SetFarmCrop(new Vector2I(189, 189), CropKind.Radish);
        main.Game.SetFarmCrop(new Vector2I(192, 189), CropKind.Radish);
        main.Game.SetFarmCrop(new Vector2I(195, 189), CropKind.Radish);
        Vector2I wetFarm = new(198, 189);
        main.Game.BuildFarm(wetFarm);
        main.Game.SetFarmCrop(wetFarm, CropKind.Radish);
        main.Game.AdvanceTick(isRaining: true);
        main.GetNode<WorldMap>("WorldMap").EmitSignal(
            WorldMap.SignalName.SelectionChanged, wetFarm);
        Control detail = Find<Control>(main.GetNode<Control>("CanvasLayer/UiRoot"), "DetailWindow");
        bool visible = main.Game.GetFarmDetails(wetFarm).Status ==
            FarmStatus.WaitingForWorkerWithWater &&
            ContainsVisibleText(detail, "待播种 · 已有水分");
        main.QueueFree();
        return visible || Fail("雨后未播种农田的已湿润状态没有显示在详情中");
    }

    private static bool CheckReserveUi(Node parent)
    {
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
        parent.AddChild(main);
        bool passed = CheckReserveControls(main);
        main.QueueFree();
        return passed;
    }

    private static bool CheckReserveControls(Main main)
    {
        foreach (var space in main.Game.GetBuildingSpaces()) main.Game.RemoveBuilding(space.AnchorCell);
        Vector2I farm = new(189, 189);
        Vector2I processor = new(15, 12);
        main.Game.SetRawReserve(CropKind.Radish, 6);
        main.Game.BuildFarm(farm);
        main.Game.SetFarmCrop(farm, CropKind.Radish);
        main.Game.AdvanceTick(isRaining: true);
        for (int i = 0; i < 206; i++) main.Game.AdvanceTick();
        main.Game.RemoveBuilding(farm);
        main.Game.BuildProcessor(processor, CropKind.Radish);

        var ui = main.GetNode<Control>("CanvasLayer/UiRoot");
        main.GetNode<WorldMap>("WorldMap").EmitSignal(WorldMap.SignalName.SelectionChanged, processor);
        var detail = Find<Control>(ui, "DetailWindow");
        if (!ContainsVisibleText(detail, "等待原料超过保留底线"))
            return Fail("加工详情未显示保留底线等待原因");
        Find<Button>(ui, "InventoryButton").EmitSignal(Button.SignalName.Pressed);
        var window = Find<Control>(ui, "InventoryWindow");
        var input = Find<LineEdit>(window, "RawReserveRadishInput");
        var apply = Find<Button>(window, "SetRawReserveRadishButton");
        var scroll = Find<ScrollContainer>(window, "InventoryScroll");
        var timer = main.GetNode<Timer>("TickTimer");
        if (input.Text != "6")
            return Fail("库存输入未显示经营模块的已提交底线");

        input.GrabFocus();
        EnterText(input, "123");
        input.CaretColumn = 2;
        scroll.ScrollVertical = 20;
        int scrollPosition = scroll.ScrollVertical;
        timer.EmitSignal(Timer.SignalName.Timeout);
        if (input.Text != "123" || !input.HasFocus() || input.CaretColumn != 2 ||
            scroll.ScrollVertical != scrollPosition ||
            !object.ReferenceEquals(input, Find<LineEdit>(window, "RawReserveRadishInput")) ||
            !object.ReferenceEquals(apply, Find<Button>(window, "SetRawReserveRadishButton")) ||
            main.Game.GetRawReserve(CropKind.Radish) != 6)
            return Fail("经营刷新丢失底线输入、光标、焦点、滚动或控件身份");
        input.ReleaseFocus();
        timer.EmitSignal(Timer.SignalName.Timeout);
        Find<Button>(window, "CloseButton").EmitSignal(Button.SignalName.Pressed);
        Find<Button>(ui, "InventoryButton").EmitSignal(Button.SignalName.Pressed);
        if (input.Text != "123")
            return Fail("输入失焦或窗口关闭重开丢失未提交草稿");

        EnterText(input, "5");
        apply.EmitSignal(Button.SignalName.Pressed);
        if (main.Game.GetRawReserve(CropKind.Radish) != 5 || main.Game.GetRawStock(CropKind.Radish) != 6 ||
            main.Game.GetPlot(processor).RemainingSeconds != 0 ||
            !ContainsVisibleText(detail, "待领取原料"))
            return Fail("设置底线未提交或在领取阶段前启动了加工");
        foreach (string invalid in new[] { "", "-1", "1.5", "2147483648" })
        {
            EnterText(input, invalid);
            apply.EmitSignal(Button.SignalName.Pressed);
            if (main.Game.GetRawReserve(CropKind.Radish) != 5 || main.Game.GetRawStock(CropKind.Radish) != 6 ||
                input.Text != invalid || !ContainsVisibleText(window, "请输入 0 到 2147483647 之间的整数"))
                return Fail("非法底线输入改变了经营状态或丢失错误提示/文本");
        }
        EnterText(input, "5");
        input.EmitSignal(LineEdit.SignalName.TextSubmitted, input.Text);
        timer.EmitSignal(Timer.SignalName.Timeout);
        if (main.Game.GetRawStock(CropKind.Radish) != 5 || main.Game.GetPlot(processor).RemainingSeconds != 26 ||
            !ContainsVisibleText(detail, "加工中"))
            return Fail("回车提交后下次经营领取未正确启动加工");
        EnterText(input, "99");
        apply.EmitSignal(Button.SignalName.Pressed);
        if (main.Game.GetRawReserve(CropKind.Radish) != 99 || main.Game.GetRawStock(CropKind.Radish) != 5 ||
            main.Game.GetPlot(processor).RemainingSeconds != 26)
            return Fail("界面提高底线退回了加工投入或重置进度");
        return true;
    }

    private static void EnterText(LineEdit input, string text)
    {
        input.Text = text;
        input.EmitSignal(LineEdit.SignalName.TextChanged, text);
    }

    private static bool CheckRoadUi(Node parent)
    {
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
        parent.AddChild(main);
        bool passed = CheckRoadControls(main);
        main.QueueFree();
        return passed;
    }

    private static bool CheckRoadControls(Main main)
    {
        var ui = main.GetNode<Control>("CanvasLayer/UiRoot");
        var map = main.GetNode<WorldMap>("WorldMap");
        Button build = Find<Button>(ui, "BuildButton");
        Control window = Find<Control>(ui, "BuildWindow");
        Control detail = Find<Control>(ui, "DetailWindow");
        Button cancel = Find<Button>(ui, "CancelPlacementButton");
        Label footer = Find<Label>(ui, "BuildHint");
        Label message = Find<Label>(ui, "MessageLabel");
        Timer timer = main.GetNode<Timer>("TickTimer");
        build.EmitSignal(Button.SignalName.Pressed);
        Button farmCard = Find<Button>(window, "FarmCard");
        Button roadCard = Find<Button>(window, "RoadCard");
        Button processorCard = Find<Button>(window, "ProcessorCardRadish");
        Find<Button>(window, "RoadTab").EmitSignal(Button.SignalName.Pressed);
        var roadStyle = (StyleBoxFlat)roadCard.GetThemeStylebox("normal");
        if (!roadCard.IsVisibleInTree() || farmCard.IsVisibleInTree() ||
            !roadCard.Text.Contains("1.00 金币/格") || roadStyle.BgColor.R != roadStyle.BgColor.G ||
            roadStyle.BgColor.G != roadStyle.BgColor.B)
            return Fail("道路目录未显示独立灰色卡片与每格1.00金币价格");
        Find<Button>(window, "ProcessorTab").EmitSignal(Button.SignalName.Pressed);
        LineEdit search = Find<LineEdit>(window, "BuildSearch");
        EnterText(search, "腌制坊");
        search.GrabFocus();
        timer.EmitSignal(Timer.SignalName.Timeout);
        if (!processorCard.IsVisibleInTree() || !processorCard.Text.Contains("10.00 金币") ||
            search.Text != "腌制坊" || !search.HasFocus() ||
            !object.ReferenceEquals(processorCard, Find<Button>(window, "ProcessorCardRadish")))
            return Fail("目录分类或经营刷新丢失加工搜索、焦点或固定卡片");
        Find<Button>(window, "FarmTab").EmitSignal(Button.SignalName.Pressed);
        if (!farmCard.IsVisibleInTree() || !farmCard.Text.Contains("10.00 金币") ||
            !object.ReferenceEquals(farmCard, Find<Button>(window, "FarmCard")))
            return Fail("道路接入改变农田目录价格或卡片身份");
        Find<Button>(window, "RoadTab").EmitSignal(Button.SignalName.Pressed);
        if (!object.ReferenceEquals(roadCard, Find<Button>(window, "RoadCard")))
            return Fail("目录切换重新创建了道路卡片");
        roadCard.EmitSignal(Button.SignalName.Pressed);
        if (window.Visible || !cancel.Visible || !footer.Text.Contains("每格 1.00 金币"))
            return Fail("道路选择没有进入连续铺设并显示统一费用");
        Vector2I first = new(183, 192);
        Vector2I second = new(180, 192);
        map.EmitSignal(WorldMap.SignalName.SelectionChanged, first);
        if (main.Game.GetPlot(first).Building != BuildingKind.Road || main.Game.MoneyCents != 4900 ||
            !cancel.Visible || detail.Visible || !message.Text.Contains("花费 1.00 金币"))
            return Fail("道路首格没有扣实际100分，或铺设后退出了连续模式");
        map.EmitSignal(WorldMap.SignalName.SelectionChanged, second);
        if (main.Game.GetPlot(second).Building != BuildingKind.Road || main.Game.MoneyCents != 4800 ||
            !cancel.Visible || !footer.Text.Contains("每格 1.00 金币"))
            return Fail("道路第二格未连续铺设或实际扣费错误");
        map.EmitSignal(WorldMap.SignalName.SelectionChanged, first);
        if (main.Game.MoneyCents != 4800 || main.Game.GetPlot(first).Building != BuildingKind.Road ||
            !cancel.Visible || !message.Text.Contains("已有建筑"))
            return Fail("道路占用失败扣费、覆盖，或丢失连续铺设模式");
        main._UnhandledInput(new InputEventKey { Pressed = true, Keycode = Key.Escape });
        if (cancel.Visible || footer.Text.Contains("铺路中"))
            return Fail("Esc没有退出连续道路模式");
        map.EmitSignal(WorldMap.SignalName.SelectionChanged, first);
        Control roadDetails = Find<Control>(detail, "RoadDetailsPanel");
        Button remove = Find<Button>(roadDetails, "RemoveRoadButton");
        if (!detail.Visible || !roadDetails.IsVisibleInTree() || !remove.IsVisibleInTree() ||
            !ContainsVisibleText(detail, "当前仅用于布局和外观") || !ContainsVisibleText(detail, "不退还建造费") ||
            ContainsVisibleText(detail, "生长周期") || ContainsVisibleText(detail, "加工周期") ||
            ContainsVisibleText(detail, "库存") || ContainsVisibleText(detail, "售价"))
            return Fail("道路详情误读作物/加工字段或没有独立拆除说明");
        timer.EmitSignal(Timer.SignalName.Timeout);
        if (!object.ReferenceEquals(remove, Find<Button>(roadDetails, "RemoveRoadButton")) ||
            !roadDetails.IsVisibleInTree() || Find<Label>(ui, "WorkerCountLabel").Text != "3 · 自动照料")
            return Fail("经营刷新重建道路详情控件或改变真实三人工人摘要");
        remove.EmitSignal(Button.SignalName.Pressed);
        if (main.Game.GetPlot(first).Building != BuildingKind.None || main.Game.MoneyCents != 4800 ||
            roadDetails.IsVisibleInTree() || !ContainsVisibleText(detail, "空地") ||
            main.Game.GetPlot(second).Building != BuildingKind.Road)
            return Fail("道路拆除没有释放占用、发生退款或误拆相邻道路");
        build.EmitSignal(Button.SignalName.Pressed);
        roadCard.EmitSignal(Button.SignalName.Pressed);
        cancel.EmitSignal(Button.SignalName.Pressed);
        map.EmitSignal(WorldMap.SignalName.SelectionChanged, first);
        if (cancel.Visible || main.Game.GetPlot(first).Building != BuildingKind.None || main.Game.MoneyCents != 4800)
            return Fail("取消按钮没有停止铺路，或普通选择仍触发扣费");
        return true;
    }

    private static bool Check(Main main)
    {
        var ui = main.GetNode<Control>("CanvasLayer/UiRoot");
        var map = main.GetNode<WorldMap>("WorldMap");
        var build = Find<Button>(ui, "BuildButton");
        var detail = Find<Control>(ui, "DetailWindow");
        var buildWindow = Find<Control>(ui, "BuildWindow");
        var cropWindow = Find<Control>(ui, "CropWindow");
        var marketWindow = Find<Control>(ui, "MarketWindow");
        var inventoryWindow = Find<Control>(ui, "InventoryWindow");
        var cancel = Find<Button>(ui, "CancelPlacementButton");
        var message = Find<Label>(ui, "MessageLabel");
        var timer = main.GetNode<Timer>("TickTimer");

        if (main.Game.MoneyCents != 5000 || detail.Visible || buildWindow.Visible || cancel.Visible ||
            !ContainsVisibleText(ui, "春 · 第 1 年 · 1 月 · 1 日"))
            return Fail("初始界面或日期显示错误");
        Button pause = Find<Button>(ui, "PauseButton");
        pause.EmitSignal(Button.SignalName.Pressed);
        timer.EmitSignal(Timer.SignalName.Timeout);
        if (main.Game.Calendar.ElapsedSeconds != 0 || pause.Text != "继续")
            return Fail("顶部暂停按钮未停止经营");
        pause.EmitSignal(Button.SignalName.Pressed);
        if (pause.Text != "暂停")
            return Fail("顶部继续按钮未恢复经营");

        map.EmitSignal(WorldMap.SignalName.SelectionChanged, new Vector2I(90, 90));
        if (!detail.Visible || ContainsVisibleText(detail, "生长周期") ||
            ContainsVisibleText(detail, "加工周期") || ContainsVisibleText(detail, "库存"))
            return Fail("空地详情显示了生产或库存信息");

        build.EmitSignal(Button.SignalName.Pressed);
        if (!buildWindow.Visible || detail.Visible)
            return Fail("建造入口未打开目录");
        buildWindow.Position = new Vector2(9999, 9999);
        build.EmitSignal(Button.SignalName.Pressed);
        Vector2 viewportSize = main.GetViewport().GetVisibleRect().Size;
        if (buildWindow.Position.X + buildWindow.Size.X > viewportSize.X - 8f ||
            buildWindow.Position.Y + buildWindow.Size.Y > viewportSize.Y - 111f - 8f)
            return Fail("建造目录超出视窗或遮挡底栏");
        buildWindow.Position = new Vector2(30, 252);
        Find<Button>(buildWindow, "FarmCard").EmitSignal(Button.SignalName.Pressed);
        if (buildWindow.Visible || !cancel.Visible)
            return Fail("选中农田后未进入摆放状态");

        Vector2I farm = new(186, 192);
        map.EmitSignal(WorldMap.SignalName.SelectionChanged, farm);
        if (main.Game.GetPlot(farm).Building != BuildingKind.Farm ||
            main.Game.MoneyCents != 4000 || !detail.Visible || cancel.Visible ||
            !ContainsVisibleText(detail, "原料当前报价：2.50 金币") ||
            !ContainsVisibleText(detail, "生长周期：获得水后 16 天成熟") ||
            !ContainsVisibleText(detail, "原料库存：") ||
            ContainsVisibleText(detail, "加工品库存"))
            return Fail("农田摆放、收费或详情显示错误");

        detail.Position = new Vector2(760, 90);
        var header = Find<Control>(detail, "Header");
        header.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = true,
        });
        detail._Input(new InputEventMouseMotion { Position = new Vector2(30, 20) });
        detail._Input(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
        Vector2 remembered = detail.Position;
        if (remembered == new Vector2(760, 90))
            return Fail("详情标题栏拖动没有改变窗口位置");
        Find<Button>(detail, "CloseButton").EmitSignal(Button.SignalName.Pressed);
        map.EmitSignal(WorldMap.SignalName.SelectionChanged, farm);
        if (!detail.Visible || detail.Position != remembered)
            return Fail("详情窗口关闭后未记住位置");

        Find<Button>(detail, "ChangeCropButton").EmitSignal(Button.SignalName.Pressed);
        if (!cropWindow.Visible || !Find<Button>(cropWindow, "CropCardCorn").Text.Contains("2.50 金币") ||
            Find<Button>(cropWindow, "CropCardCorn").Text.Contains("玉米粉库存"))
            return Fail("作物菜单没有只显示玉米原料价格与库存");
        cropWindow.Position = new Vector2(400, 120);
        Find<Button>(cropWindow, "CropCardCorn").EmitSignal(Button.SignalName.Pressed);
        if (main.Game.GetPlot(farm).CropKind != CropKind.Corn || cropWindow.Visible)
            return Fail("农田改种失败");
        Find<Button>(detail, "ChangeCropButton").EmitSignal(Button.SignalName.Pressed);
        if (cropWindow.Position != new Vector2(400, 120))
            return Fail("作物菜单重新打开后未记住位置");
        Find<Button>(cropWindow, "CloseButton").EmitSignal(Button.SignalName.Pressed);

        int[] growthDays = { 16, 20, 12, 14, 17, 40, 4 };
        foreach (CropDefinition crop in FarmGame.Crops)
        {
            Find<Button>(detail, "ChangeCropButton").EmitSignal(Button.SignalName.Pressed);
            Find<Button>(cropWindow, $"CropCard{crop.Kind}").EmitSignal(Button.SignalName.Pressed);
            if (!ContainsVisibleText(detail, $"生长周期：获得水后 {growthDays[(int)crop.Kind]} 天成熟"))
                return Fail($"{crop.CropName}农田详情未显示正确生长周期");
            if (crop.Kind == CropKind.Sugarcane &&
                !ContainsVisibleText(detail, "当前季节不适宜播种"))
                return Fail("春季选择甘蔗后详情未显示不适季原因");
        }
        Find<Button>(detail, "ChangeCropButton").EmitSignal(Button.SignalName.Pressed);
        Find<Button>(cropWindow, "CropCardRadish").EmitSignal(Button.SignalName.Pressed);

        build.EmitSignal(Button.SignalName.Pressed);
        Find<Button>(buildWindow, "ProcessorTab").EmitSignal(Button.SignalName.Pressed);
        if (!Find<Button>(buildWindow, "ProcessorCardCorn").Text.Contains("10.00"))
            return Fail("加工场地目录未显示建造费");
        Find<Button>(buildWindow, "ProcessorCardRadish").EmitSignal(Button.SignalName.Pressed);
        map.EmitSignal(WorldMap.SignalName.SelectionChanged, farm);
        if (!cancel.Visible || main.Game.MoneyCents != 4000 || !message.Text.Contains("已有建筑"))
            return Fail("占用地块仍建造或扣费");
        Vector2I processor = new(198, 192);
        map.EmitSignal(WorldMap.SignalName.SelectionChanged, processor);
        if (main.Game.GetPlot(processor).Building != BuildingKind.Processor ||
            main.Game.MoneyCents != 3000 || cancel.Visible ||
            !ContainsVisibleText(detail, "等待萝卜") ||
            !ContainsVisibleText(detail, "加工周期：投入原料后 0.5 天完成") ||
            !ContainsVisibleText(detail, "腌萝卜加工品当前报价：0.50 金币") ||
            !ContainsVisibleText(detail, "腌萝卜加工品库存：0") ||
            ContainsVisibleText(detail, "生长周期") ||
            ContainsVisibleText(detail, "原料库存") ||
            ContainsVisibleText(detail, "原料当前报价"))
            return Fail("加工场地摆放与收费错误");

        Find<Button>(ui, "InventoryButton").EmitSignal(Button.SignalName.Pressed);
        if (!inventoryWindow.Visible || !ContainsVisibleText(inventoryWindow, "玉米原料"))
            return Fail("库存窗口未显示分类库存");
        inventoryWindow.Position = new Vector2(260, 110);
        Find<Button>(inventoryWindow, "CloseButton").EmitSignal(Button.SignalName.Pressed);
        Find<Button>(ui, "InventoryButton").EmitSignal(Button.SignalName.Pressed);
        if (inventoryWindow.Position != new Vector2(260, 110))
            return Fail("库存窗口重新打开后未记住位置");
        build.EmitSignal(Button.SignalName.Pressed);
        if (inventoryWindow.Visible || !buildWindow.Visible)
            return Fail("建造目录打开后库存窗口仍遮挡操作");
        Find<Button>(buildWindow, "CloseButton").EmitSignal(Button.SignalName.Pressed);

        bool sawProcessing = false;
        for (int i = 0; i < 250; i++)
        {
            timer.EmitSignal(Timer.SignalName.Timeout);
            sawProcessing |= ContainsVisibleText(detail, "加工中");
        }
        int radishProducts = main.Game.GetProductStock(CropKind.Radish);
        string radishProductPrice = (main.Game.GetProductPriceCents(CropKind.Radish) / 100m)
            .ToString("0.00", CultureInfo.InvariantCulture);
        if (!sawProcessing || radishProducts == 0 ||
            !ContainsVisibleText(detail, $"腌萝卜加工品库存：{radishProducts}") ||
            !ContainsVisibleText(detail, $"腌萝卜加工品当前报价：{radishProductPrice} 金币"))
            return Fail("萝卜加工场地没有显示随经营更新的售价与库存");
        Find<Button>(ui, "MarketButton").EmitSignal(Button.SignalName.Pressed);
        Button sell = Find<Button>(marketWindow, "SellButton");
        if (!marketWindow.Visible || sell.Disabled || ContainsVisibleText(marketWindow, "待定") ||
            !ContainsVisibleText(marketWindow, "玉米原料"))
            return Fail("市场窗口未区分原料和可售加工品");
        ScrollContainer marketScroll = Find<ScrollContainer>(marketWindow, "MarketScroll");
        Button radishRawButton = Find<Button>(marketWindow, "SellRawRadishButton");
        marketScroll.ScrollVertical = 30;
        int scrollPosition = marketScroll.ScrollVertical;
        timer.EmitSignal(Timer.SignalName.Timeout);
        if (marketScroll.ScrollVertical != scrollPosition ||
            !object.ReferenceEquals(sell, Find<Button>(marketWindow, "SellButton")) ||
            !object.ReferenceEquals(radishRawButton, Find<Button>(marketWindow, "SellRawRadishButton")))
            return Fail("经营刷新重置了市场滚动位置或操作按钮");
        int beforeSale = main.Game.MoneyCents;
        sell.EmitSignal(Button.SignalName.Pressed);
        if (main.Game.MoneyCents <= beforeSale || main.Game.GetProductStock(CropKind.Radish) != 0 ||
            !ContainsVisibleText(detail, "腌萝卜加工品库存：0"))
            return Fail("市场出售没有更新金币与库存");

        CropKind rawKind = CropKind.Radish;
        int rawStock = main.Game.GetRawStock(rawKind);
        if (rawStock == 0)
            return Fail("未加工的原料没有进入市场可售库存");
        Find<Button>(ui, "MarketButton").EmitSignal(Button.SignalName.Pressed);
        Button sellRaw = Find<Button>(marketWindow, $"SellRaw{rawKind}Button");
        if (sellRaw.Disabled || !ContainsVisibleText(marketWindow, $"{FarmGame.GetCrop(rawKind).CropName}原料"))
            return Fail("市场未提供按品种出售原料的入口");
        int rawPrice = main.Game.GetRawPriceCents(rawKind);
        beforeSale = main.Game.MoneyCents;
        sellRaw.EmitSignal(Button.SignalName.Pressed);
        if (main.Game.MoneyCents != beforeSale + rawStock * rawPrice ||
            main.Game.GetRawStock(rawKind) != 0 ||
            !Find<Button>(marketWindow, $"SellRaw{rawKind}Button").Disabled)
            return Fail("市场原料出售没有按当日价格更新金币与按钮状态");
        return true;
    }

    private static T Find<T>(Node parent, string name) where T : Node =>
        parent.FindChild(name, true, false) as T ??
        throw new System.InvalidOperationException($"缺少界面节点：{name}");

    private static bool ContainsVisibleText(Node parent, string text)
    {
        foreach (Node child in parent.GetChildren())
        {
            if (child is Label label && label.IsVisibleInTree() && label.Text.Contains(text))
                return true;
            if (child is Button button && button.IsVisibleInTree() && button.Text.Contains(text))
                return true;
            if (ContainsVisibleText(child, text))
                return true;
        }
        return false;
    }

    private static bool Fail(string message)
    {
        GD.PushError(message);
        return false;
    }
}
