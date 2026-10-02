using System;
using System.Collections.Generic;
using Godot;
using FarmExchange.Economy;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
using FarmExchange.Market;
using FarmExchange.Time;
using FarmExchange.Trading;
using GoodsInventory = FarmExchange.Inventory.Inventory;

public partial class TestTradeOrders : Node
{
    private static readonly CommodityId Raw = new(CropKind.Radish, CommodityKind.Raw);
    private static readonly CommodityId Product = new(CropKind.Radish, CommodityKind.Product);

    public override void _Ready() => GetTree().Quit(RunChecks() ? 0 : 1);

    public static bool RunChecks() => CheckLimitAndBudget() && CheckTargetsAndConditions() &&
        CheckEditingAndReserves() && CheckFrozenResources() && CheckFailuresAndBounds() &&
        CheckIsolationAndLockedTargets() && CheckLockedQuantityEditing() && CheckLastFillIdentity() && CheckGameplayPhases();

    private static TradeOrderRequest Request(TradeOrderSide side = TradeOrderSide.Buy,
        TradeOrderFrequency frequency = TradeOrderFrequency.Once, int quantity = 1,
        TradeOrderQuantityMode quantityMode = TradeOrderQuantityMode.Fixed) =>
        new(Raw, side, frequency, quantityMode, quantity,
            side == TradeOrderSide.Buy && frequency == TradeOrderFrequency.Once
                ? TradeOrderBudgetMode.LimitPrice : TradeOrderBudgetMode.None,
            0, 30, CashReserveMode.Amount, 0,
            new IReadOnlyList<TradeOrderCondition>[]
            {
                new[] { new TradeOrderCondition(TradeConditionFactor.Price, TradeConditionComparison.GreaterOrEqual, 0) },
            });

    private static (GoodsInventory Inventory, Wallet Wallet, MarketQuotes Market, TradingService Trading, TradeOrderBook Book) Setup(int cents = 1000)
    {
        var inventory = new GoodsInventory();
        var wallet = new Wallet(cents);
        var market = new MarketQuotes(12345);
        var trading = new TradingService(inventory, wallet, market);
        return (inventory, wallet, market, trading, new TradeOrderBook(inventory, wallet, market, trading));
    }

    private static void Tick(TradeOrderBook book, uint seconds = 1) => book.Execute(new GameCalendar(seconds).Snapshot);

    private static bool CheckLimitAndBudget()
    {
        var setup = Setup();
        TradeOrderCommandResult first = setup.Book.Create(Request());
        if (!first.Success || first.Id != 1 || setup.Wallet.FrozenCents != 31 ||
            setup.Wallet.AvailableCents != 969 || setup.Wallet.BalanceCents != 1000 || setup.Inventory.Get(Raw) != 0)
            return Fail("一次限价单未按数量最高价及费用冻结，或创建立即成交");
        Tick(setup.Book);
        TradeOrderSnapshot completed = setup.Book.GetSnapshots()[0];
        if (completed.Status != TradeOrderStatus.Completed || completed.FrozenCents != 0 ||
            setup.Wallet.FrozenCents != 0 || setup.Wallet.BalanceCents != 974 ||
            setup.Inventory.Get(Raw) != 1 || completed.LastFill?.Trade.TotalCents != 25 || completed.LastFill?.Trade.FeeCents != 1)
            return Fail("限价单未按实际低价成交并释放差额");
        Tick(setup.Book, 2);
        if (setup.Inventory.Get(Raw) != 1 || setup.Wallet.BalanceCents != 974 ||
            setup.Book.Cancel(first.Id).Success || setup.Book.Update(first.Id, Request()).Success ||
            setup.Book.SetEnabled(first.Id, true).Success)
            return Fail("一次单重复成交或允许修改终态");

        TradeOrderRequest budget = Request() with { BudgetMode = TradeOrderBudgetMode.FixedBudget, BudgetCents = 100, Quantity = 0 };
        if (!setup.Book.Create(budget).Success || setup.Wallet.FrozenCents != 100)
            return Fail("固定含费预算未冻结");
        Tick(setup.Book, 3);
        TradeOrderSnapshot bought = setup.Book.GetSnapshots()[1];
        if (bought.LastFill?.Trade.Quantity != 3 || bought.LastFill?.Trade.TotalCents != 75 || bought.LastFill?.Trade.FeeCents != 1 ||
            setup.Wallet.BalanceCents != 898 || setup.Wallet.FrozenCents != 0 || setup.Inventory.Get(Raw) != 4)
            return Fail("预算未在执行时按含费最大完整数量成交并释放余额");
        if (!setup.Book.Create(budget with { BudgetCents = 101 }).Success)
            return Fail("整百含费预算创建失败");
        Tick(setup.Book, 4);
        if (setup.Book.GetSnapshots()[2].LastFill?.Trade.Quantity != 4 || setup.Wallet.BalanceCents != 797)
            return Fail("固定预算的手续费一分边界错误");
        TradeOrderCommandResult tooSmall = setup.Book.Create(budget with { BudgetCents = 25 });
        Tick(setup.Book, 5);
        if (!tooSmall.Success || setup.Book.GetSnapshots()[3].Status != TradeOrderStatus.Waiting ||
            setup.Wallet.FrozenCents != 25 || setup.Wallet.BalanceCents != 797 ||
            setup.Book.GetSnapshots()[3].LastFill != null)
            return Fail("预算不足一份时应完整等待并保留冻结，不扣费");
        if (!setup.Book.Cancel(tooSmall.Id).Success || setup.Wallet.FrozenCents != 0)
            return Fail("撤销预算单未释放全部额度");
        TradeOrderCommandResult cheap = setup.Book.Create(Request() with { LimitPriceCents = 24 });
        Tick(setup.Book, 6);
        if (!cheap.Success || setup.Book.GetSnapshots()[4].LastFill != null || setup.Wallet.FrozenCents != 25)
            return Fail("超限价单仍然成交");
        return true;
    }

    private static bool CheckTargetsAndConditions()
    {
        var setup = Setup();
        setup.Inventory.Add(Raw, 2);
        TradeOrderCommandResult once = setup.Book.Create(Request(quantity: 5, quantityMode: TradeOrderQuantityMode.BuyToTarget));
        if (!once.Success || setup.Book.GetSnapshots()[0].LockedQuantity != 3)
            return Fail("一次目标买单未在设单时锁定差额");
        setup.Inventory.Add(Raw, 1);
        Tick(setup.Book);
        if (setup.Inventory.Get(Raw) != 6 || setup.Book.GetSnapshots()[0].LastFill?.Trade.Quantity != 3)
            return Fail("一次目标买单错误地随新库存重算数量");
        if (!setup.Book.Create(Request(frequency: TradeOrderFrequency.Continuous, quantity: 8,
                quantityMode: TradeOrderQuantityMode.BuyToTarget)).Success)
            return Fail("持续目标买单创建失败");
        Tick(setup.Book, 2);
        Tick(setup.Book, 3);
        if (setup.Inventory.Get(Raw) != 8 || setup.Book.GetSnapshots()[1].LastFill?.Trade.Quantity != 2 ||
            setup.Book.GetSnapshots()[1].WaitingReason == null)
            return Fail("持续目标没有按执行时差额补足或达标后仍重复买入");
        setup.Trading.Sell(Raw, 3);
        Tick(setup.Book, 4);
        if (setup.Inventory.Get(Raw) != 8 || setup.Book.GetSnapshots()[1].LastFill?.Trade.Quantity != 3)
            return Fail("持续目标未随库存消耗重新补货");
        setup.Book.SetEnabled(2, false);
        TradeOrderRequest sell = Request(TradeOrderSide.Sell, TradeOrderFrequency.Continuous, 2, TradeOrderQuantityMode.SellToTarget) with
        {
            ConditionGroups = new IReadOnlyList<TradeOrderCondition>[]
            {
                new[] { new TradeOrderCondition(TradeConditionFactor.Price, TradeConditionComparison.Greater, 25),
                    new TradeOrderCondition(TradeConditionFactor.Stock, TradeConditionComparison.Greater, 0) },
                new[] { new TradeOrderCondition(TradeConditionFactor.Price, TradeConditionComparison.Equal, 25),
                    new TradeOrderCondition(TradeConditionFactor.Stock, TradeConditionComparison.GreaterOrEqual, 8),
                    new TradeOrderCondition(TradeConditionFactor.Season, TradeConditionComparison.Equal, (int)Season.Spring) },
            },
        };
        setup.Book.Create(sell);
        Tick(setup.Book, 5);
        if (setup.Inventory.Get(Raw) != 2 || setup.Book.GetSnapshots()[2].LastFill?.Trade.Quantity != 6)
            return Fail("OR 条件或组内 AND 季节条件没有正确触发目标出售");
        setup.Inventory.Add(Raw, 2);
        Tick(setup.Book, 6);
        if (setup.Inventory.Get(Raw) != 4 || setup.Book.GetSnapshots()[2].WaitingReason == null)
            return Fail("库存积压本身强制出售，或未展示真实未满足条件");
        setup.Book.Cancel(3);
        TradeOrderRequest both = Request(frequency: TradeOrderFrequency.Continuous) with
        {
            ConditionGroups = new IReadOnlyList<TradeOrderCondition>[]
            {
                new[] { new TradeOrderCondition(TradeConditionFactor.Stock, TradeConditionComparison.LessOrEqual, 4) },
                new[] { new TradeOrderCondition(TradeConditionFactor.Price, TradeConditionComparison.Less, 30) },
            },
        };
        setup.Book.Create(both);
        setup.Book.Create(Request(TradeOrderSide.Sell, TradeOrderFrequency.Continuous, 1) with
        {
            ConditionGroups = new IReadOnlyList<TradeOrderCondition>[]
            {
                new[] { new TradeOrderCondition(TradeConditionFactor.Stock, TradeConditionComparison.Equal, 5) },
            },
        });
        int before = setup.Wallet.BalanceCents;
        Tick(setup.Book, 7);
        if (setup.Inventory.Get(Raw) != 4 || setup.Wallet.BalanceCents != before - 2 ||
            setup.Book.GetSnapshots()[3].LastFill?.Trade.Quantity != 1 || setup.Book.GetSnapshots()[4].LastFill?.Trade.Quantity != 1)
            return Fail("多组成立导致重复成交，或反向后单没有读取前单真实库存");
        return true;
    }

    private static bool CheckEditingAndReserves()
    {
        var setup = Setup(126);
        TradeOrderRequest reserve = Request() with { LimitPriceCents = 25, ReserveMode = CashReserveMode.Percent, ReserveValue = 79 };
        TradeOrderCommandResult created = setup.Book.Create(reserve);
        TradeOrderSnapshot original = setup.Book.GetSnapshots()[0];
        if (!created.Success || original.CashBasisCents != 126 || original.ReserveCents != 100 ||
            setup.Wallet.FrozenCents != 26)
            return Fail("百分比未在设单时按原现金向上取整锁定，或精确保留冻结失败");
        if (setup.Book.Update(created.Id, reserve with { Quantity = 2 }).Success ||
            setup.Book.GetSnapshots()[0] != original || setup.Wallet.FrozenCents != 26 || setup.Wallet.BalanceCents != 126)
            return Fail("编辑失败没有保留原单和冻结资源");
        setup.Wallet.Credit(100);
        if (!setup.Book.Update(created.Id, reserve with { Quantity = 2 }).Success ||
            setup.Book.GetSnapshots()[0].CashBasisCents != 126 || setup.Book.GetSnapshots()[0].ReserveCents != 100 ||
            setup.Wallet.FrozenCents != 51)
            return Fail("编辑重新计算现金基准或未计入原单额度");
        if (!setup.Book.Update(created.Id, reserve with { Quantity = 2, ReserveValue = 50 }).Success ||
            setup.Book.GetSnapshots()[0].ReserveCents != 63)
            return Fail("修改保留参数未使用原设单现金");
        setup.Book.Cancel(created.Id);
        if (setup.Wallet.FrozenCents != 0 || setup.Book.GetSnapshots()[0].Status != TradeOrderStatus.Cancelled)
            return Fail("取消未释放冻结且保留终态记录");
        if (!setup.Book.Create(reserve with
        {
            Frequency = TradeOrderFrequency.Continuous,
            BudgetMode = TradeOrderBudgetMode.None,
            ReserveValue = 50
        }).Success ||
            setup.Book.GetSnapshots()[1].ReserveCents != 113 || setup.Wallet.FrozenCents != 0)
            return Fail("新建策略未取新现金基准或持续单错误冻结");
        setup.Book.SetEnabled(2, false);
        Tick(setup.Book);
        if (setup.Inventory.Get(Raw) != 0 || setup.Wallet.BalanceCents != 226)
            return Fail("停用持续策略仍成交");
        setup.Book.SetEnabled(2, true);
        setup.Wallet.TrySpend(114);
        Tick(setup.Book, 2);
        if (setup.Inventory.Get(Raw) != 0 || setup.Wallet.BalanceCents != 112 ||
            setup.Book.GetSnapshots()[1].WaitingReason == null)
            return Fail("持续单未按实际现金保留金额等待");
        return true;
    }

    private static bool CheckFrozenResources()
    {
        var setup = Setup(1000);
        setup.Inventory.Add(Raw, 10);
        setup.Inventory.Add(Product, 3);
        TradeOrderCommandResult rawSell = setup.Book.Create(Request(TradeOrderSide.Sell, quantity: 8));
        setup.Book.Create(Request(TradeOrderSide.Sell, quantity: 2) with { Commodity = Product });
        if (!rawSell.Success || setup.Inventory.Get(Raw) != 10 || setup.Inventory.GetFrozen(Raw) != 8 ||
            setup.Inventory.GetAvailable(Raw) != 2 || setup.Trading.Sell(Raw, 3).Success ||
            setup.Inventory.TryTakeRawForProcessing(CropKind.Radish) == false ||
            setup.Inventory.TryTakeRawForProcessing(CropKind.Radish) == false ||
            setup.Inventory.TryTakeRawForProcessing(CropKind.Radish))
            return Fail("冻结库存被手动出售或加工消耗，或可用库存不能正常领取");
        if (setup.Trading.SellAll(Raw).Quantity != 0 || setup.Trading.SellAllProducts().Quantity != 1 ||
            setup.Inventory.Get(Product) != 2 || setup.Inventory.GetFrozen(Product) != 2)
            return Fail("全部出售消费了冻结商品");
        TradeOrderSnapshot original = setup.Book.GetSnapshots()[0];
        if (setup.Book.Update(rawSell.Id, Request(TradeOrderSide.Sell, quantity: 9)).Success ||
            setup.Book.GetSnapshots()[0] != original || setup.Inventory.GetFrozen(Raw) != 8)
            return Fail("编辑卖单超过真实额度未零修改拒绝");
        if (!setup.Book.Update(rawSell.Id, Request(TradeOrderSide.Sell, quantity: 7)).Success ||
            setup.Inventory.GetFrozen(Raw) != 7 || setup.Inventory.GetAvailable(Raw) != 1)
            return Fail("编辑卖单未用回原冻结额度");
        setup.Book.Cancel(rawSell.Id);
        if (setup.Inventory.GetFrozen(Raw) != 0 || setup.Inventory.GetAvailable(Raw) != 8)
            return Fail("撤销卖单未归还可用库存");
        setup.Book.Create(Request() with { BudgetMode = TradeOrderBudgetMode.FixedBudget, BudgetCents = setup.Wallet.BalanceCents });
        if (setup.Wallet.TrySpend(100) || setup.Trading.Buy(Raw, 1).Success)
            return Fail("冻结资金被主动支出或即时买入使用");
        return true;
    }

    private static bool CheckFailuresAndBounds()
    {
        var setup = Setup(int.MaxValue);
        TradeOrderRequest request = Request();
        TradeOrderRequest[] invalid =
        {
            request with { Commodity = new CommodityId((CropKind)999, CommodityKind.Raw) },
            request with { Side = (TradeOrderSide)999 },
            request with { ReserveMode = (CashReserveMode)999 },
            request with { ReserveValue = -1 },
            request with { ReserveMode = CashReserveMode.Percent, ReserveValue = 101 },
            request with { BudgetMode = TradeOrderBudgetMode.None },
            request with { LimitPriceCents = 0 },
            request with { Quantity = 0 },
            request with { QuantityMode = TradeOrderQuantityMode.SellToTarget },
            request with { BudgetMode = TradeOrderBudgetMode.FixedBudget, BudgetCents = 0 },
            request with { BudgetMode = TradeOrderBudgetMode.FixedBudget, BudgetCents = 100, QuantityMode = TradeOrderQuantityMode.BuyToTarget },
            request with { Frequency = TradeOrderFrequency.Continuous, BudgetMode = TradeOrderBudgetMode.LimitPrice },
            request with { Quantity = int.MaxValue, LimitPriceCents = int.MaxValue },
            request with { ConditionGroups = Array.Empty<IReadOnlyList<TradeOrderCondition>>() },
            request with { ConditionGroups = new IReadOnlyList<TradeOrderCondition>[] { Array.Empty<TradeOrderCondition>() } },
            request with { ConditionGroups = new IReadOnlyList<TradeOrderCondition>[] {
                new[] { new TradeOrderCondition(TradeConditionFactor.Price, (TradeConditionComparison)999, 25) } } },
            request with { ConditionGroups = new IReadOnlyList<TradeOrderCondition>[] {
                new[] { new TradeOrderCondition(TradeConditionFactor.Stock, TradeConditionComparison.Equal, -1) } } },
            request with { ConditionGroups = new IReadOnlyList<TradeOrderCondition>[] {
                new[] { new TradeOrderCondition(TradeConditionFactor.Season, TradeConditionComparison.Greater, 0) } } },
            request with { ConditionGroups = new IReadOnlyList<TradeOrderCondition>[] {
                new[] { new TradeOrderCondition(TradeConditionFactor.Season, TradeConditionComparison.Equal, 4) } } },
        };
        foreach (TradeOrderRequest bad in invalid)
            if (setup.Book.Create(bad).Success || setup.Book.GetSnapshots().Count != 0 ||
                setup.Wallet.BalanceCents != int.MaxValue || setup.Wallet.FrozenCents != 0)
                return Fail("非法或巨大委托未在冻结前完整拒绝");
        if (setup.Book.Update(0, request).Success || setup.Book.Cancel(1).Success || setup.Book.SetEnabled(1, true).Success)
            return Fail("不存在的委托没有正常拒绝");
        setup.Inventory.Add(Raw, int.MaxValue - 1);
        if (!setup.Book.Create(request with { Quantity = 2 }).Success)
            return Fail("可等待库存释放的订单创建失败");
        Tick(setup.Book);
        if (setup.Inventory.Get(Raw) != int.MaxValue - 1 || setup.Wallet.BalanceCents != int.MaxValue ||
            setup.Wallet.FrozenCents != 61 || setup.Book.GetSnapshots()[0].LastFill != null)
            return Fail("容量失败消耗冻结预算、库存或费用");
        setup.Book.Cancel(1);
        setup.Book.Create(request with { Side = TradeOrderSide.Sell, BudgetMode = TradeOrderBudgetMode.None, Quantity = 1 });
        Tick(setup.Book, 2);
        if (setup.Inventory.Get(Raw) != int.MaxValue - 1 || setup.Inventory.GetFrozen(Raw) != 1 ||
            setup.Book.GetSnapshots()[1].LastFill != null)
            return Fail("钱包容量失败消耗冻结库存");
        setup.Book.Cancel(2);
        var mutable = new List<TradeOrderCondition> { new(TradeConditionFactor.Price, TradeConditionComparison.Equal, 25) };
        setup.Book.Create(request with { ConditionGroups = new IReadOnlyList<TradeOrderCondition>[] { mutable } });
        mutable[0] = new TradeOrderCondition(TradeConditionFactor.Price, TradeConditionComparison.Equal, 0);
        if (setup.Book.GetSnapshots()[2].Request.ConditionGroups[0][0].Value != 25)
            return Fail("外部修改条件集合污染已创建委托");
        return true;
    }

    private static bool CheckGameplayPhases()
    {
        var game = new FarmGame(12345);
        foreach (var space in game.GetBuildingSpaces())
            game.RemoveBuilding(space.AnchorCell);
        game.SetPaused(true);
        game.CreateTradeOrder(Request() with { BudgetMode = TradeOrderBudgetMode.FixedBudget, BudgetCents = 4500 });
        if (game.BuildFarm(new Vector2I(0, 0)) == null || game.AvailableMoneyCents != 500 ||
            game.Buy(Raw, 21).Success || game.AdvanceTick() != default || game.GetStock(Raw) != 0)
            return Fail("暂停自动执行或建造使用了冻结资金");
        game.CancelTradeOrder(1);
        if (game.BuildProcessor(new Vector2I(0, 0), CropKind.Radish) != null)
            return Fail("撤销后可用资金不能建造");
        game.CreateTradeOrder(Request(quantity: 2));
        game.SetPaused(false);
        game.AdvanceTick();
        if (game.GetRawStock(CropKind.Radish) != 2 || game.GetPlot(new Vector2I(0, 0)).RemainingSeconds != 0)
            return Fail("自动买入没有发生在本秒加工领取之后");
        game.SetPaused(true);
        game.CreateTradeOrder(Request(TradeOrderSide.Sell, quantity: 2) with
        {
            ConditionGroups = new IReadOnlyList<TradeOrderCondition>[] {
                new[] { new TradeOrderCondition(TradeConditionFactor.Price, TradeConditionComparison.Greater, 1000) } },
        });
        game.SetPaused(false);
        game.AdvanceTick();
        if (game.GetRawStock(CropKind.Radish) != 2 || game.GetAvailableStock(Raw) != 0 ||
            game.GetPlot(new Vector2I(0, 0)).RemainingSeconds != 0 || game.SellCommodityAll(Raw).Quantity != 0)
            return Fail("冻结原料被全场加工领取或手动全售使用");
        game.CancelTradeOrder(3);
        game.AdvanceTick();
        if (game.GetRawStock(CropKind.Radish) != 1 || game.GetPlot(new Vector2I(0, 0)).RemainingSeconds != 26)
            return Fail("取消后的原料未恢复正常加工领取");
        return true;
    }

    private static bool CheckIsolationAndLockedTargets()
    {
        var setup = Setup(100);
        setup.Book.Create(Request() with { BudgetMode = TradeOrderBudgetMode.FixedBudget, BudgetCents = 80 });
        TradeOrderSnapshot original = setup.Book.GetSnapshots()[0];
        if (setup.Book.Create(Request()).Success || setup.Book.Update(1, Request() with { Quantity = 4 }).Success ||
            setup.Book.GetSnapshots()[0] != original || setup.Wallet.FrozenCents != 80)
            return Fail("其他一次单或编辑错误地使用了非本单资金额度");
        setup.Book.Create(Request(frequency: TradeOrderFrequency.Continuous));
        var paused = new GameCalendar();
        paused.SetPaused(true);
        setup.Book.Execute(paused.Snapshot);
        if (setup.Inventory.Get(Raw) != 0 || setup.Wallet.BalanceCents != 100)
            return Fail("委托模块直接执行暂停快照仍然成交");
        setup.Book.SetEnabled(2, false);
        Tick(setup.Book);
        if (setup.Inventory.Get(Raw) != 3 || setup.Wallet.FrozenCents != 0 || setup.Wallet.BalanceCents != 24)
            return Fail("固定预算没有只使用本单额度");

        setup.Inventory.Add(Raw, 7);
        TradeOrderCommandResult seller = setup.Book.Create(Request(TradeOrderSide.Sell, quantity: 4,
            quantityMode: TradeOrderQuantityMode.SellToTarget));
        if (!seller.Success || setup.Book.GetSnapshots()[2].LockedQuantity != 6)
            return Fail("一次目标卖单未锁定创建时差额");
        setup.Inventory.Add(Raw, 2);
        Tick(setup.Book, 2);
        if (setup.Inventory.Get(Raw) != 6 || setup.Inventory.GetFrozen(Raw) != 0 ||
            setup.Book.GetSnapshots()[2].LastFill?.Trade.Quantity != 6)
            return Fail("一次目标卖单重新计算数量，或真实成交未释放冻结库存");

        setup.Book.Create(Request(TradeOrderSide.Sell, quantity: 5) with
        {
            ConditionGroups = new IReadOnlyList<TradeOrderCondition>[] {
                new[] { new TradeOrderCondition(TradeConditionFactor.Price, TradeConditionComparison.Greater, 1000) } },
        });
        if (setup.Book.Create(Request(TradeOrderSide.Sell, quantity: 2)).Success)
            return Fail("其他卖单使用了已冻结库存");
        setup.Book.Create(Request(TradeOrderSide.Sell, TradeOrderFrequency.Continuous, 2));
        Tick(setup.Book, 3);
        if (setup.Inventory.Get(Raw) != 6 || setup.Inventory.GetFrozen(Raw) != 5 ||
            setup.Book.GetSnapshots()[4].LastFill != null)
            return Fail("持续卖单使用了他单冻结库存或缩量部分成交");
        TradeOrderRequest changed = Request(TradeOrderSide.Sell, quantity: 1) with { Commodity = Product };
        if (setup.Book.Update(4, changed).Success || setup.Inventory.GetFrozen(Raw) != 5)
            return Fail("跨商品编辑失败释放了原冻结");
        setup.Inventory.Add(Product, 1);
        if (!setup.Book.Update(4, changed).Success || setup.Inventory.GetFrozen(Raw) != 0 ||
            setup.Inventory.GetFrozen(Product) != 1)
            return Fail("跨商品编辑未整体替换冻结归属");

        var huge = Setup(int.MaxValue);
        huge.Book.Create(Request() with { BudgetMode = TradeOrderBudgetMode.FixedBudget, BudgetCents = int.MaxValue });
        Tick(huge.Book);
        TradeResult? sale = huge.Book.GetSnapshots()[0].LastFill?.Trade;
        if (sale == null)
            return Fail("最大整数含费预算没有完整成交");
        long nextTotal = (sale.Value.Quantity + 1) * 25;
        if (sale.Value.TotalCents + sale.Value.FeeCents > int.MaxValue ||
            nextTotal + (nextTotal + 99) / 100 <= int.MaxValue || huge.Wallet.FrozenCents != 0)
            return Fail("最大整数预算没有选择含费最大数量或计算溢出");
        return true;
    }

    private static bool CheckLastFillIdentity()
    {
        var setup = Setup();
        TradeOrderRequest request = Request(frequency: TradeOrderFrequency.Continuous);
        if (!setup.Book.Create(request).Success)
            return Fail("最近成交身份测试无法创建持续策略");
        Tick(setup.Book);
        TradeOrderFillSnapshot? original = setup.Book.GetSnapshots()[0].LastFill;
        if (original == null || original.Commodity != Raw || original.Side != TradeOrderSide.Buy ||
            original.Trade.Quantity != 1 || original.Trade.TotalCents != 25 || original.Trade.FeeCents != 1 ||
            original.BalanceCents != 974)
            return Fail("最近成交未保存真实商品、方向、费用和成交后余额");
        setup.Inventory.Add(Product, 3);
        request = request with { Commodity = Product, Side = TradeOrderSide.Sell };
        if (!setup.Book.Update(1, request).Success || setup.Book.GetSnapshots()[0].LastFill != original ||
            setup.Book.GetSnapshots()[0].Request.Commodity != Product ||
            setup.Book.GetSnapshots()[0].Request.Side != TradeOrderSide.Sell)
            return Fail("修改商品和方向污染或清除了原最近成交");
        Tick(setup.Book, 2);
        TradeOrderFillSnapshot? updated = setup.Book.GetSnapshots()[0].LastFill;
        if (updated == null || updated.Commodity != Product || updated.Side != TradeOrderSide.Sell ||
            updated.Trade.Quantity != 1 || updated.Trade.TotalCents != 50 || updated.Trade.FeeCents != 1 ||
            updated.BalanceCents != 1023 || setup.Wallet.BalanceCents != 1023 ||
            setup.Inventory.Get(Raw) != 1 || setup.Inventory.Get(Product) != 2 ||
            original.Commodity != Raw || original.Side != TradeOrderSide.Buy || original.BalanceCents != 974)
            return Fail("新成交未替换为新的真实身份，或旧只读成交被后续操作修改");
        return true;
    }

    private static bool Fail(string message)
    {
        GD.PushError(message);
        return false;
    }

    private static bool CheckLockedQuantityEditing()
    {
        var setup = Setup();
        setup.Inventory.Add(Raw, 2);
        TradeOrderRequest buy = Request(quantity: 5, quantityMode: TradeOrderQuantityMode.BuyToTarget) with { LimitPriceCents = 25 };
        if (!setup.Book.Create(buy).Success)
            return Fail("一次目标买单创建失败");
        setup.Inventory.Add(Raw, 2);
        buy = buy with { LimitPriceCents = 40 };
        if (!setup.Book.Update(1, buy).Success || setup.Book.GetSnapshots()[0].LockedQuantity != 3 ||
            setup.Wallet.FrozenCents != 122)
            return Fail("只编辑最高价格后一次买单重算了差额，或冻结未按锁量更新");
        buy = buy with
        {
            ConditionGroups = new IReadOnlyList<TradeOrderCondition>[] {
                new[] { new TradeOrderCondition(TradeConditionFactor.Stock, TradeConditionComparison.Equal, 4) } },
            ReserveValue = 100,
        };
        if (!setup.Book.Update(1, buy).Success || setup.Book.GetSnapshots()[0].LockedQuantity != 3 ||
            setup.Wallet.FrozenCents != 122)
            return Fail("只编辑条件或现金保留后一次买单重算了锁量");
        TradeOrderSnapshot original = setup.Book.GetSnapshots()[0];
        if (setup.Book.Update(1, buy with { Quantity = int.MaxValue }).Success ||
            setup.Book.GetSnapshots()[0] != original || setup.Wallet.FrozenCents != 122)
            return Fail("显式目标修改失败没有保留原锁量和冻结");
        buy = buy with { Quantity = 6 };
        if (!setup.Book.Update(1, buy).Success || setup.Book.GetSnapshots()[0].LockedQuantity != 2 ||
            setup.Wallet.FrozenCents != 81 || setup.Book.GetSnapshots()[0].CashBasisCents != 1000)
            return Fail("显式修改买入目标未按新数量意图重算");
        buy = buy with { Commodity = Product };
        if (!setup.Book.Update(1, buy).Success || setup.Book.GetSnapshots()[0].LockedQuantity != 6 ||
            setup.Wallet.FrozenCents != 243)
            return Fail("修改目标商品没有按新商品库存确定数量");
        buy = buy with { Frequency = TradeOrderFrequency.Continuous, BudgetMode = TradeOrderBudgetMode.None };
        if (!setup.Book.Update(1, buy).Success || setup.Wallet.FrozenCents != 0)
            return Fail("切换持续策略未释放原冻结");
        setup.Inventory.Add(Product, 1);
        buy = buy with { Frequency = TradeOrderFrequency.Once, BudgetMode = TradeOrderBudgetMode.LimitPrice };
        if (!setup.Book.Update(1, buy).Success || setup.Book.GetSnapshots()[0].LockedQuantity != 5 ||
            setup.Wallet.FrozenCents != 202)
            return Fail("从持续切回一次没有确定新的目标差额");
        buy = buy with { BudgetMode = TradeOrderBudgetMode.FixedBudget, BudgetCents = 100, QuantityMode = TradeOrderQuantityMode.Fixed, Quantity = 0 };
        if (!setup.Book.Update(1, buy).Success || setup.Book.GetSnapshots()[0].LockedQuantity != 0 ||
            setup.Wallet.FrozenCents != 100)
            return Fail("切换固定预算没有采用预算数量语义");
        buy = buy with { BudgetMode = TradeOrderBudgetMode.LimitPrice, Quantity = 2 };
        if (!setup.Book.Update(1, buy).Success || setup.Book.GetSnapshots()[0].LockedQuantity != 2 ||
            setup.Wallet.FrozenCents != 81)
            return Fail("固定预算切回固定数量未重验锁量");
        setup.Book.Cancel(1);

        setup.Inventory.Add(Raw, 6);
        TradeOrderRequest sell = Request(TradeOrderSide.Sell, quantity: 4, quantityMode: TradeOrderQuantityMode.SellToTarget);
        if (!setup.Book.Create(sell).Success || setup.Book.GetSnapshots()[1].LockedQuantity != 6)
            return Fail("一次目标卖单未锁定初始差额");
        setup.Inventory.Add(Raw, 4);
        sell = sell with
        {
            ConditionGroups = new IReadOnlyList<TradeOrderCondition>[] {
                new[] { new TradeOrderCondition(TradeConditionFactor.Price, TradeConditionComparison.GreaterOrEqual, 30) } },
        };
        if (!setup.Book.Update(2, sell).Success || setup.Book.GetSnapshots()[1].LockedQuantity != 6 ||
            setup.Inventory.GetFrozen(Raw) != 6)
            return Fail("只编辑目标卖单价格条件后重新计算数量");
        sell = sell with { Quantity = 5 };
        if (!setup.Book.Update(2, sell).Success || setup.Book.GetSnapshots()[1].LockedQuantity != 9 ||
            setup.Inventory.GetFrozen(Raw) != 9)
            return Fail("显式卖出目标变更没有重新确定差额");
        TradeOrderRequest direction = sell with
        {
            Side = TradeOrderSide.Buy,
            QuantityMode = TradeOrderQuantityMode.BuyToTarget,
            Quantity = 16,
            BudgetMode = TradeOrderBudgetMode.LimitPrice,
        };
        if (!setup.Book.Update(2, direction).Success || setup.Book.GetSnapshots()[1].LockedQuantity != 2 ||
            setup.Inventory.GetFrozen(Raw) != 0 || setup.Wallet.FrozenCents != 61 ||
            setup.Book.GetSnapshots()[1].CashBasisCents != 1000)
            return Fail("修改方向未按新意图替换冻结或改变了现金原基准");
        return true;
    }
}
