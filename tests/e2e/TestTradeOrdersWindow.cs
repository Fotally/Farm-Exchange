using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
using FarmExchange.Time;
using FarmExchange.Trading;
using FarmExchange.UI;

public partial class TestTradeOrdersWindow : Node
{
    private static readonly CommodityId RadishRaw = new(CropKind.Radish, CommodityKind.Raw);
    private static readonly CommodityId RadishProduct = new(CropKind.Radish, CommodityKind.Product);

    public override async void _Ready()
    {
        bool passed = RunChecks(this) && await RunLayoutChecks(this);
        if (passed) GD.Print("委托编辑、冻结、成交与窗口布局检查通过");
        GetTree().Quit(passed ? 0 : 1);
    }

    public static bool RunChecks(Node parent)
    {
        Main main = OpenMain(parent);
        bool passed = CheckOrders(main);
        main.QueueFree();
        return passed && CheckInvalidInput(parent) && CheckEditedFill(parent);
    }

    private static Main OpenMain(Node parent)
    {
        Main main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
        parent.AddChild(main);
        Click(main, "PauseButton");
        Click(main, "MarketButton");
        Click(main, "OpenTradeOrdersButton");
        return main;
    }

    private static bool CheckOrders(Main main)
    {
        FarmGame game = main.Game;
        TradeOrdersWindow window = Find<TradeOrdersWindow>(main, "TradeOrdersWindow");
        if (!window.Visible || Find<MarketWindow>(main, "MarketWindow").Visible)
            return Fail("市场委托入口未打开独立窗口");
        SetOption(window, "OrderCommodity", 12);
        SetText(window, "OrderLimitInput", "1.00");
        SetText(window, "OrderQuantityInput", "10");
        SetOption(window, "OrderReserveMode", 1);
        SetText(window, "OrderReserveInput", "20");
        SetOption(window, "OrderConditionComparison1", 1);
        SetText(window, "OrderConditionValue1", "0.01");
        Click(window, "SaveOrderButton");
        if (game.GetTradeOrders().Count != 1) return Fail("公开界面无法建单");
        TradeOrderSnapshot order = game.GetTradeOrders()[0];
        if (order.CashBasisCents != 5000 || order.ReserveCents != 1000 || order.FrozenCents != 1010 ||
            game.AvailableMoneyCents != 3990 || order.Status != TradeOrderStatus.Waiting)
            return Fail("一次买单冻结与设单现金比例展示不一致");
        if (!Find<Label>(window, "OrderDetails").Text.Contains("冻结金币 10.10") ||
            !Find<Label>(window, "OrderResources").Text.Contains("可用 39.90") ||
            !Find<Button>(window, "ToggleOrderButton").Disabled)
            return Fail("冻结或单次操作按钮未显示真实快照");
        uint pausedSeconds = game.Calendar.ElapsedSeconds;
        main.GetNode<Timer>("TickTimer").EmitSignal(Timer.SignalName.Timeout);
        if (game.Calendar.ElapsedSeconds != pausedSeconds || game.GetStock(RadishRaw) != 0)
            return Fail("暂停期间委托提前成交");
        LineEdit draft = Find<LineEdit>(window, "OrderQuantityInput");
        draft.Text = "10";
        draft.GrabFocus();
        draft.CaretColumn = 1;
        window.Refresh(game);
        if (draft.Text != "10" || !draft.HasFocus() || draft.CaretColumn != 1 ||
            !ReferenceEquals(draft, Find<LineEdit>(window, "OrderQuantityInput")))
            return Fail("委托刷新覆盖草稿、焦点、光标或控件");
        Click(window, "CloseButton");
        Click(main, "MarketButton");
        Click(main, "OpenTradeOrdersButton");
        if (draft.Text != "10") return Fail("关闭重开丢失未提交编辑");
        Click(window, "AddOrderGroupButton");
        SetOption(window, "OrderConditionComparison2", 3);
        SetText(window, "OrderConditionValue2", "0.25");
        Click(window, "AddOrderCondition2Button");
        SetOption(window, "OrderConditionFactor3", 1);
        SetOption(window, "OrderConditionComparison3", 0);
        SetText(window, "OrderConditionValue3", "100");
        Click(window, "AddOrderCondition2Button");
        SetOption(window, "OrderConditionFactor4", 2);
        SetOption(window, "OrderConditionSeason4", (int)Season.Spring);
        Click(window, "SaveOrderButton");
        order = game.GetTradeOrders()[0];
        if (order.Request.ConditionGroups.Count != 2 || order.Request.ConditionGroups[1].Count != 3 ||
            order.CashBasisCents != 5000 || order.ReserveCents != 1000)
            return Fail("条件组组合或原设单基准编辑丢失");
        Click(main, "PauseButton");
        main.GetNode<Timer>("TickTimer").EmitSignal(Timer.SignalName.Timeout);
        order = game.GetTradeOrders()[0];
        if (order.Status != TradeOrderStatus.Completed || order.LastFill?.Trade is not TradeResult trade ||
            trade.Quantity != 10 || trade.TotalCents != 250 || trade.FeeCents != 3 || game.MoneyCents != 4747 ||
            game.FrozenMoneyCents != 0 || !Find<Label>(window, "OrderDetails").Text.Contains("实际支出 2.53") ||
            !Find<Button>(window, "SaveOrderButton").Disabled || !Find<Button>(window, "CancelOrderButton").Disabled)
            return Fail("真实成交费用、释放差额、终态按钮或反馈不一致");
        Click(main, "PauseButton");
        if (!game.Buy(RadishProduct, 10).Success) return Fail("准备卖出库存失败");
        Click(window, "NewOrderButton");
        SetOption(window, "OrderCommodity", 13);
        SetOption(window, "OrderSide", 1);
        SetOption(window, "OrderQuantityMode", 2);
        SetText(window, "OrderQuantityInput", "7");
        SetText(window, "OrderConditionValue1", "100.00");
        SetOption(window, "OrderConditionComparison1", 3);
        Click(window, "RemoveOrderGroup2Button");
        Click(window, "SaveOrderButton");
        order = game.GetTradeOrders()[1];
        if (order.LockedQuantity != 3 || order.FrozenQuantity != 3 || game.GetAvailableStock(RadishProduct) != 7)
            return Fail("单次卖到目标未锁定冻结差额");
        SetOption(window, "OrderQuantityMode", 0);
        SetText(window, "OrderQuantityInput", "10");
        Click(window, "SaveOrderButton");
        MarketWindow market = Find<MarketWindow>(main, "MarketWindow");
        market.Refresh(game);
        Click(market, "CommodityRadishProductButton");
        if (!Find<Button>(market, "SellCommodityAllButton").Disabled || !Find<Button>(market, "SellButton").Disabled ||
            game.GetAvailableStock(RadishProduct) != 0 ||
            Find<Label>(market, "QuoteRadishProductStock").Text != $"{game.GetStock(RadishProduct)} / 0" ||
            !Find<Label>(market, "QuoteRadishProductStock").TooltipText.Contains($"冻结 {game.GetFrozenStock(RadishProduct)}"))
            return Fail("市场仍将冻结库存作为可出售库存");
        Click(window, "CancelOrderButton");
        if (game.GetFrozenStock(RadishProduct) != 0 || game.GetTradeOrders()[1].Status != TradeOrderStatus.Cancelled ||
            !Find<Label>(window, "OrderDetails").Text.Contains("已撤销"))
            return Fail("撤销未释放库存或丢失终态记录");
        Click(window, "NewOrderButton");
        SetOption(window, "OrderSide", 0);
        SetOption(window, "OrderFrequency", 0);
        SetOption(window, "OrderBudgetMode", 1);
        SetText(window, "OrderBudgetInput", "2.00");
        SetOption(window, "OrderReserveMode", 0);
        SetText(window, "OrderReserveInput", "5.00");
        if (Find<Control>(window, "OrderQuantityMode").IsVisibleInTree() || Find<LineEdit>(window, "OrderLimitInput").IsVisibleInTree())
            return Fail("固定预算仍显示冲突的数量或限价输入");
        Click(window, "SaveOrderButton");
        if (game.GetTradeOrders().Count != 3 || game.GetTradeOrders()[2].FrozenCents != 200 || game.GetTradeOrders()[2].ReserveCents != 500)
            return Fail("固定预算或固定保留额未通过界面提交");
        Click(window, "CancelOrderButton");
        Click(window, "NewOrderButton");
        SetOption(window, "OrderFrequency", 1);
        SetOption(window, "OrderQuantityMode", 1);
        SetText(window, "OrderQuantityInput", "12");
        Click(window, "SaveOrderButton");
        if (game.GetTradeOrders().Count != 4 || game.GetTradeOrders()[3].FrozenCents != 0 ||
            Find<Button>(window, "ToggleOrderButton").Disabled || Find<LineEdit>(window, "OrderBudgetInput").IsVisibleInTree())
            return Fail("持续补货冻结资源或缺少停用入口");
        Click(window, "ToggleOrderButton");
        if (game.GetTradeOrders()[3].Status != TradeOrderStatus.Disabled || Find<Button>(window, "ToggleOrderButton").Text != "恢复")
            return Fail("持续停用没有反映真实状态");
        Click(window, "ToggleOrderButton");
        if (game.GetTradeOrders()[3].Status != TradeOrderStatus.Waiting) return Fail("恢复持续策略失败");
        ItemList list = Find<ItemList>(window, "OrderList");
        list.Select(0);
        list.EmitSignal(ItemList.SignalName.ItemSelected, 0L);
        if (Find<LineEdit>(window, "OrderQuantityInput").Text != "10" ||
            Find<OptionButton>(window, "OrderCommodity").Selected != 12 || !Find<Button>(window, "SaveOrderButton").Disabled ||
            Controls<OptionButton>(window, "OrderConditionFactor").Count != 4)
            return Fail("列表选择没有还原完整条件，或允许修改终态");
        list.Select(3);
        list.EmitSignal(ItemList.SignalName.ItemSelected, 3L);
        int originalBasis = game.GetTradeOrders()[3].CashBasisCents;
        SetOption(window, "OrderReserveMode", 1);
        SetText(window, "OrderReserveInput", "10");
        SetText(window, "OrderQuantityInput", "14");
        Click(window, "SaveOrderButton");
        if (game.GetTradeOrders()[3].CashBasisCents != originalBasis ||
            game.GetTradeOrders()[3].ReserveCents != (originalBasis + 9) / 10)
            return Fail("编辑保留比例重新计算了现金基准");
        Click(window, "NewOrderButton");
        SetOption(window, "OrderSide", 1);
        SetOption(window, "OrderQuantityMode", 0);
        SetText(window, "OrderQuantityInput", "1");
        Controls<LineEdit>(window, "OrderConditionValue")[0].Text = "0.50";
        Click(window, "SaveOrderButton");
        int beforeSell = game.MoneyCents;
        Click(main, "PauseButton");
        main.GetNode<Timer>("TickTimer").EmitSignal(Timer.SignalName.Timeout);
        if (game.GetTradeOrders().Count != 5 || game.GetTradeOrders()[4].LastFill?.Trade is not TradeResult sale ||
            sale.Quantity != 1 || sale.TotalCents != 50 || sale.FeeCents != 1 || game.MoneyCents != beforeSell + 49 ||
            !Find<Label>(window, "OrderDetails").Text.Contains("实际收入 0.49"))
            return Fail("持续卖出真实收入和费用未展示");
        main._UnhandledInput(new InputEventKey { Pressed = true, Keycode = Key.Escape });
        return !window.Visible || Fail("Esc 未关闭委托窗口");
    }

    private static bool CheckInvalidInput(Node parent)
    {
        var window = new TradeOrdersWindow();
        parent.AddChild(window);
        window.ShowRaised();
        int submissions = 0;
        window.CreateRequested += _ => submissions++;
        SetText(window, "OrderQuantityInput", "1");
        SetText(window, "OrderConditionValue1", "1.00");
        foreach (string invalid in new[] { "", "-1", "0", "1.001", "21474836.48", "79228162514264337593543950335" })
        {
            SetText(window, "OrderLimitInput", invalid);
            Click(window, "SaveOrderButton");
        }
        SetText(window, "OrderLimitInput", "1.00");
        foreach (string invalid in new[] { "", "-1", "1.5", "2147483648" })
        {
            SetText(window, "OrderQuantityInput", invalid);
            Click(window, "SaveOrderButton");
        }
        SetText(window, "OrderQuantityInput", "1");
        SetOption(window, "OrderReserveMode", 1);
        SetText(window, "OrderReserveInput", "101");
        Click(window, "SaveOrderButton");
        SetOption(window, "OrderReserveMode", 0);
        SetText(window, "OrderReserveInput", "1.001");
        Click(window, "SaveOrderButton");
        SetText(window, "OrderReserveInput", "0");
        SetText(window, "OrderConditionValue1", "-1");
        Click(window, "SaveOrderButton");
        SetOption(window, "OrderConditionFactor1", 1);
        SetText(window, "OrderConditionValue1", "1.5");
        Click(window, "SaveOrderButton");
        SetOption(window, "OrderBudgetMode", 1);
        SetText(window, "OrderBudgetInput", "0");
        Click(window, "SaveOrderButton");
        SetOption(window, "OrderBudgetMode", 0);
        SetText(window, "OrderConditionValue1", "10");
        Click(window, "SaveOrderButton");
        if (submissions != 1) { window.QueueFree(); return Fail("非法金额、数量或条件通过文本解析"); }
        window.ShowCommandResult(new TradeOrderCommandResult(false, 0, "测试拒绝"));
        if (Find<Label>(window, "OrderFeedback").Text != "测试拒绝")
        { window.QueueFree(); return Fail("命令拒绝没有展示原因"); }
        Click(window, "AddOrderCondition1Button");
        Click(window, "RemoveOrderCondition2Button");
        Click(window, "AddOrderGroupButton");
        Click(window, "RemoveOrderGroup2Button");
        if (Controls<OptionButton>(window, "OrderConditionFactor").Count != 1)
        { window.QueueFree(); return Fail("删除条件或条件组未更新编辑器"); }
        window.QueueFree();
        return true;
    }

    private static bool CheckEditedFill(Node parent)
    {
        Main main = OpenMain(parent);
        bool passed = CheckEditedFill(main);
        main.QueueFree();
        return passed;
    }

    private static bool CheckEditedFill(Main main)
    {
        FarmGame game = main.Game;
        if (!game.Buy(RadishProduct, 1).Success) return Fail("准备编辑后卖出库存失败");
        TradeOrdersWindow window = Find<TradeOrdersWindow>(main, "TradeOrdersWindow");
        SetOption(window, "OrderCommodity", 12);
        SetOption(window, "OrderFrequency", 1);
        SetText(window, "OrderQuantityInput", "2");
        SetOption(window, "OrderConditionComparison1", 1);
        SetText(window, "OrderConditionValue1", "0.25");
        int beforeBuy = game.MoneyCents;
        Click(window, "SaveOrderButton");
        Click(main, "PauseButton");
        main.GetNode<Timer>("TickTimer").EmitSignal(Timer.SignalName.Timeout);
        Click(main, "PauseButton");
        TradeOrderFillSnapshot? buy = game.GetTradeOrders()[0].LastFill;
        if (buy == null || buy.Commodity != RadishRaw || buy.Side != TradeOrderSide.Buy ||
            buy.Trade.Quantity != 2 || buy.Trade.TotalCents != 50 || buy.Trade.FeeCents != 1 || buy.BalanceCents != beforeBuy - 51)
            return Fail("持续买入未记录真实成交身份和余额");
        SetOption(window, "OrderCommodity", 13);
        SetOption(window, "OrderSide", 1);
        SetText(window, "OrderQuantityInput", "1");
        SetOption(window, "OrderConditionComparison1", 3);
        SetText(window, "OrderConditionValue1", "0.50");
        Click(window, "SaveOrderButton");
        TradeOrderSnapshot edited = game.GetTradeOrders()[0];
        string text = Find<Label>(window, "OrderDetails").Text;
        string oldBalance = (buy.BalanceCents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
        if (edited.Request.Side != TradeOrderSide.Sell || edited.Request.Commodity != RadishProduct || edited.LastFill != buy ||
            !text.Contains("最近成交 买入 萝卜原料 2 份") || !text.Contains("实际支出 0.51") ||
            !text.Contains("成交后余额 " + oldBalance) || text.Contains("实际收入"))
            return Fail("编辑方向和商品误解释或清空了之前的真实买入");
        Click(main, "PauseButton");
        main.GetNode<Timer>("TickTimer").EmitSignal(Timer.SignalName.Timeout);
        TradeOrderFillSnapshot? sell = game.GetTradeOrders()[0].LastFill;
        text = Find<Label>(window, "OrderDetails").Text;
        if (sell == null || sell.Commodity != RadishProduct || sell.Side != TradeOrderSide.Sell ||
            sell.Trade.Quantity != 1 || sell.Trade.TotalCents != 50 || sell.Trade.FeeCents != 1 || sell.BalanceCents != buy.BalanceCents + 49 ||
            !text.Contains("最近成交 卖出 腌萝卜加工品 1 份") || !text.Contains("实际收入 0.49") || text.Contains("实际支出"))
            return Fail("后续新成交未替换为新商品真实卖出和扣费收入");
        return true;
    }

    public static async Task<bool> RunLayoutChecks(Node parent)
    {
        Main main = OpenMain(parent);
        TradeOrdersWindow window = Find<TradeOrdersWindow>(main, "TradeOrdersWindow");
        SetOption(window, "OrderCommodity", 12);
        SetText(window, "OrderLimitInput", "1.00");
        SetText(window, "OrderQuantityInput", "10");
        SetText(window, "OrderConditionValue1", "0.01");
        Click(window, "SaveOrderButton");
        Click(window, "NewOrderButton");
        SetOption(window, "OrderFrequency", 1);
        SetOption(window, "OrderQuantityMode", 1);
        SetText(window, "OrderQuantityInput", "100");
        SetOption(window, "OrderReserveMode", 1);
        SetText(window, "OrderReserveInput", "20");
        Click(window, "SaveOrderButton");
        Click(window, "AddOrderGroupButton");
        for (int i = 0; i < 6; i++) Click(window, "AddOrderCondition2Button");
        await Frames(parent);
        bool passed = true;
        Rect2 viewport = parent.GetViewport().GetVisibleRect();
        Rect2 rect = window.GetGlobalRect();
        if (rect.Position.Y < 94 || rect.End.Y > viewport.End.Y - 90 || rect.Position.X < 0 || rect.End.X > viewport.End.X)
            passed = Fail("委托窗口溢出1280×720可用区域");
        passed = CheckWindowTitle(window, viewport) && passed;
        foreach (string button in new[] { "SaveOrderButton", "ToggleOrderButton", "CancelOrderButton", "NewOrderButton" })
            if (!rect.Encloses(Find<Button>(window, button).GetGlobalRect())) passed = Fail("委托固定按钮超出窗口：" + button);
        ScrollContainer scroll = Find<ScrollContainer>(window, "OrderEditorScroll");
        List<LineEdit> values = Controls<LineEdit>(window, "OrderConditionValue");
        LineEdit last = values[^1];
        scroll.EnsureControlVisible(last);
        await Frames(parent);
        if (scroll.ScrollVertical <= 0 || !scroll.GetGlobalRect().Encloses(last.GetGlobalRect()))
            passed = Fail("条件编辑器无法滚动至末个输入");
        foreach (OptionButton factor in Controls<OptionButton>(window, "OrderConditionFactor"))
        {
            Control row = (Control)factor.GetParent();
            foreach (Node child in row.GetChildren())
                if (child is Control control && control.Visible &&
                    (control.GetGlobalRect().Position.X < scroll.GetGlobalRect().Position.X || control.GetGlobalRect().End.X > scroll.GetGlobalRect().End.X))
                    passed = Fail("条件行超出编辑器横向可用区域");
        }
        last.Text = "0.25";
        last.GrabFocus();
        last.CaretColumn = 2;
        int oldScroll = scroll.ScrollVertical;
        window.Refresh(main.Game);
        await Frames(parent);
        if (last.Text != "0.25" || !last.HasFocus() || last.CaretColumn != 2 || scroll.ScrollVertical != oldScroll)
            passed = Fail("布局后的暂停刷新丢失焦点或滚动");
        last.ReleaseFocus();
        Click(window, "RemoveOrderGroup2Button");
        scroll.ScrollVertical = 0;
        await Frames(parent);
        if (DisplayServer.GetName() != "headless")
        {
            string directory = ProjectSettings.GlobalizePath("res://build/issue36-validation");
            DirAccess.MakeDirRecursiveAbsolute(directory);
            parent.GetViewport().GetTexture().GetImage().SavePng(directory + "/orders-window.png");
        }
        Click(main, "MarketButton");
        await Frames(parent);
        passed = CheckWindowTitle(Find<MarketWindow>(main, "MarketWindow"), viewport) && passed;
        main.QueueFree();
        return passed;
    }

    private static bool CheckWindowTitle(DraggableWindow window, Rect2 viewport)
    {
        Control header = Find<Control>(window, "Header");
        Label title = Controls<Label>(header, "")[0];
        Rect2 rect = window.GetGlobalRect();
        if (title.GetLineCount() != 1 || !header.GetGlobalRect().Encloses(title.GetGlobalRect()) ||
            rect.Position.Y < 94 || rect.End.Y > viewport.End.Y - 90 || rect.Position.X < 0 || rect.End.X > viewport.End.X)
            return Fail(window.Name + "标题不是完整单行，或窗口溢出可用区域");
        return true;
    }

    private static async Task Frames(Node node)
    {
        await node.ToSignal(node.GetTree(), SceneTree.SignalName.ProcessFrame);
        await node.ToSignal(node.GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static void Click(Node parent, string name) => Find<Button>(parent, name).EmitSignal(Button.SignalName.Pressed);

    private static void SetText(Node parent, string name, string text) => Find<LineEdit>(parent, name).Text = text;

    private static void SetOption(Node parent, string name, int index)
    {
        OptionButton control = Find<OptionButton>(parent, name);
        control.Select(index);
        control.EmitSignal(OptionButton.SignalName.ItemSelected, (long)index);
    }

    private static T Find<T>(Node parent, string name) where T : Node => (T)parent.FindChild(name, true, false);

    private static List<T> Controls<T>(Node parent, string prefix) where T : Node
    {
        var controls = new List<T>();
        foreach (Node child in parent.GetChildren())
        {
            if (child is T typed && child.Name.ToString().StartsWith(prefix, StringComparison.Ordinal)) controls.Add(typed);
            controls.AddRange(Controls<T>(child, prefix));
        }
        return controls;
    }

    private static bool Fail(string message)
    {
        GD.PushError("委托窗口：" + message);
        return false;
    }
}
