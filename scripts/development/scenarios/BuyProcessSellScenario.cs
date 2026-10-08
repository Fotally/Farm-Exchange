using System;
using System.Collections.Generic;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
using FarmExchange.Land;
using FarmExchange.Trading;

namespace FarmExchange.Development;

/**
 * <summary>当前局与独立局共用的固定买入、加工、一次委托卖出流程。</summary>
 * <remarks>只提交正式经营命令并观察完整检查点；不拥有经营驱动，不补资源或回滚。</remarks>
 */
public sealed class BuyProcessSellScenario
{
    private readonly ScenarioConfiguration _configuration;
    private uint _lastTicks;
    private int _timePlanIndex;
    private ScenarioSnapshot? _beforeOrder;
    private TradeOrderRequest? _orderRequest;
    private readonly Dictionary<string, int> _checkIndices = new();

    public FarmGame Game { get; }
    public ScenarioReport Report { get; }
    public bool IsRunning => Report.Outcome == ScenarioOutcome.Running;
    public ScenarioStage Stage { get; private set; } = ScenarioStage.Preparing;
    public ScenarioOutcome Outcome => Report.Outcome;
    public double CurrentRateIntent => _configuration.TimePlan[_timePlanIndex].Rate;
    public string Progress => $"{StageText()}；完成{Report.AdvancedTicks} tick；加工等待{Report.ProcessingWaitTicks}，订单等待{Report.OrderWaitTicks}；{Report.Reason}";

    private BuyProcessSellScenario(ScenarioConfiguration configuration, FarmGame game, int? actualSeed)
    {
        _configuration = configuration;
        Game = game;
        _lastTicks = game.Calendar.ElapsedSeconds;
        Report = new ScenarioReport
        {
            Configuration = new ScenarioConfigurationReference(configuration.SourcePath, configuration.CaseId, configuration.Revision, configuration.Sha256),
            Target = configuration.Target,
            ActualSeed = actualSeed,
        };
        foreach (string name in new[] { "受控准备", "买入结算", "目标加工观察", "加工归因", "订单冻结", "本单成交", "资金归因", "库存关系" })
        {
            _checkIndices.Add(name, Report.Checks.Count);
            Report.Checks.Add(new ScenarioCheck(name, ScenarioCheckStatus.NotExecuted, "尚未执行"));
        }
    }

    /**
     * <summary>准备目标并记录基准，提交真实买入；拒绝保存在报告中。</summary>
     * <param name="configuration">已严格加载并固定的本次参数。</param>
     * <param name="currentGame">current模式必需的真实对象；独立模式忽略。</param>
     * <param name="actualSeed">现场已知的实际种子；未知传空。</param>
     * <returns>同一流程运行器；独立局仅运行新数据对象。</returns>
     */
    public static BuyProcessSellScenario Start(ScenarioConfiguration configuration, FarmGame? currentGame = null, int? actualSeed = null)
    {
        bool independent = configuration.Target == "independent";
        FarmGame game = independent ? new FarmGame(configuration.Seed) : currentGame ?? throw new ArgumentNullException(nameof(currentGame));
        var scenario = new BuyProcessSellScenario(configuration, game, independent ? configuration.Seed : actualSeed);
        scenario.Prepare(independent);
        return scenario;
    }

    private void Prepare(bool independent)
    {
        // 时间区间在任何经营命令之前核对；不重置现场日期。
        if (_configuration.TimePlan[0].EndTicks <= Game.Calendar.ElapsedSeconds)
        {
            Finish(ScenarioOutcome.PreconditionsRejected, "首个时间区间终点必须晚于准备基准");
            return;
        }
        if (independent)
        {
            Game.SetPaused(true);
            var cells = new List<Godot.Vector2I>();
            foreach (BuildingSpaceSnapshot space in Game.GetBuildingSpaces())
                cells.Add(space.AnchorCell);
            foreach (Godot.Vector2I cell in cells)
            {
                string? removal = Game.RemoveBuilding(cell, CommandOrigin.Scenario);
                Report.Initialization.Add(new ScenarioPreparation("RemoveBuilding", new { x = cell.X, y = cell.Y }, new { success = removal == null, reason = removal }));
                if (removal != null)
                {
                    Finish(ScenarioOutcome.PreconditionsRejected, removal);
                    return;
                }
            }
            int balanceBefore = Game.MoneyCents;
            string? error = Game.BuildProcessor(_configuration.ProcessorAnchor, _configuration.RawCommodity.Crop, CommandOrigin.Scenario);
            Report.Initialization.Add(new ScenarioPreparation("BuildProcessor", new { x = _configuration.ProcessorAnchor.X, y = _configuration.ProcessorAnchor.Y, crop = _configuration.RawCommodity.Crop },
                new { success = error == null, reason = error, spentCents = balanceBefore - Game.MoneyCents }));
            if (error != null)
            {
                Finish(ScenarioOutcome.PreconditionsRejected, error);
                return;
            }
            RawReserveFailure reserveResult = Game.SetRawReserve(_configuration.RawCommodity.Crop, 0, CommandOrigin.Scenario);
            Report.Initialization.Add(new ScenarioPreparation("SetRawReserve", new { crop = _configuration.RawCommodity.Crop, quantity = 0 }, reserveResult));
            if (reserveResult != RawReserveFailure.None)
                throw new InvalidOperationException("合法原料底线设置被拒绝");
        }
        else
            Report.Initialization.Add(new ScenarioPreparation("BindCurrent", new { knownSeed = Report.ActualSeed }, new { cleanup = false, resetDate = false }));
        BuildingSpaceSnapshot? target = Game.GetBuildingSpace(_configuration.ProcessorAnchor);
        if (target == null || target.AnchorCell != _configuration.ProcessorAnchor || target.Building != BuildingKind.Processor ||
            Game.GetPlot(_configuration.ProcessorAnchor).CropKind != _configuration.RawCommodity.Crop)
        {
            Finish(ScenarioOutcome.PreconditionsRejected, "processorAnchor必须指向匹配原料的真实加工场地锚点");
            return;
        }
        Report.Baseline = Capture();
        Check("受控准备", independent ? ScenarioCheckStatus.Passed : ScenarioCheckStatus.InsufficientEvidence,
            independent ? "显式种子新局通过正式命令拆除赠送设施、付费建造唯一场地、原料底线0；无其他生产或订单" : "绑定现场，保留已有设施、批次、订单与底线；无法保证受控归因");
        ScenarioSnapshot before = Capture();
        TradeResult buy = Game.Buy(_configuration.RawCommodity, _configuration.Quantity,
            CommandOrigin.Scenario);
        ScenarioSnapshot after = Capture();
        Report.Operations.Add(new ScenarioOperation("Buy", _lastTicks,
            new { commodity = _configuration.RawCommodity, quantity = _configuration.Quantity }, buy, before, after));
        if (!buy.Success)
        {
            Finish(ScenarioOutcome.OperationRejected, buy.ErrorMessage ?? "买入被正式经营拒绝");
            return;
        }
        bool conserved = buy.Quantity == _configuration.Quantity && buy.FeeCents == 0 &&
            after.Raw.Total - (long)before.Raw.Total == _configuration.Quantity &&
            before.BalanceCents - (long)after.BalanceCents == buy.TotalCents &&
            before.FrozenCents == after.FrozenCents && before.Raw.Frozen == after.Raw.Frozen;
        Check("买入结算", conserved, "同步买入返回值、总资金与原料库存命令前后差额");
        if (!conserved)
        {
            Finish(ScenarioOutcome.CheckFailed, "买入同步结算检查失败");
            return;
        }
        Stage = ScenarioStage.ObservingProcessing;
        ObserveProcessor();
        if (independent)
            Game.SetPaused(false);
    }

    /**
     * <summary>观察已完成真实区间；阶段转换后停止当前批量请求供宿主重新决定倍率和预算。</summary>
     * <param name="checkpoint">FarmGame真实事件或请求终点的稳定完整检查点。</param>
     * <returns>是否允许当前批量请求继续；false不必表示流程结束。</returns>
     */
    public bool ObserveCheckpoint(SimulationCheckpoint checkpoint)
    {
        if (!IsRunning)
            return false;
        if (checkpoint.ElapsedSeconds != Game.Calendar.ElapsedSeconds || checkpoint.ElapsedSeconds < _lastTicks)
            throw new ArgumentException("检查点必须与目标当前稳定时点一致", nameof(checkpoint));
        uint advanced = checkpoint.ElapsedSeconds - _lastTicks;
        if (advanced == 0)
            return true;
        Report.Intervals.Add(new ScenarioInterval(_lastTicks, checkpoint.ElapsedSeconds, advanced, checkpoint.IsEvent, checkpoint.Result));
        _lastTicks = checkpoint.ElapsedSeconds;
        Report.AdvancedTicks += advanced;
        Report.Produced += checkpoint.Result.Produced;
        Report.Harvested += checkpoint.Result.Harvested;
        ScenarioStage previousStage = Stage;
        int previousSegment = _timePlanIndex;
        if (Stage == ScenarioStage.WaitingForOrder)
        {
            Report.OrderWaitTicks += advanced;
            ObserveOrder();
        }
        else
        {
            Report.ProcessingWaitTicks += advanced;
            if (!ObserveProcessor())
                return false;
        }
        if (!IsRunning)
            return false;
        // 最后日期允许既有订单的本tick成交；不再买入或建新单。
        if (_lastTicks >= _configuration.TimePlan[^1].EndTicks)
        {
            Finish(ScenarioOutcome.TimeRangeExhausted, "已达到配置的最后游戏日期，流程尚未完成");
            return false;
        }
        while (_timePlanIndex < _configuration.TimePlan.Count - 1 && _lastTicks >= _configuration.TimePlan[_timePlanIndex].EndTicks)
            _timePlanIndex++;
        if (Stage is ScenarioStage.ObservingProcessing or ScenarioStage.WaitingForProducts)
        {
            if (Game.GetAvailableStock(_configuration.ProductCommodity) >= _configuration.Quantity)
                CreateOrder();
            else if (Report.ProcessingWaitTicks >= _configuration.ProcessingWaitLimitTicks)
                Finish(ScenarioOutcome.WaitLimitExceeded, "加工观察与产品等待共用的tick预算已耗尽");
        }
        else if (Report.OrderWaitTicks >= _configuration.OrderWaitLimitTicks)
            Finish(ScenarioOutcome.WaitLimitExceeded, "本单等待tick预算已耗尽");
        return IsRunning && previousStage == Stage && previousSegment == _timePlanIndex;
    }

    private bool ObserveProcessor()
    {
        BuildingSpaceSnapshot? space = Game.GetBuildingSpace(_configuration.ProcessorAnchor);
        if (space == null || space.AnchorCell != _configuration.ProcessorAnchor || space.Building != BuildingKind.Processor ||
            Game.GetPlot(_configuration.ProcessorAnchor).CropKind != _configuration.RawCommodity.Crop)
        {
            Finish(ScenarioOutcome.PreconditionsRejected, "运行中目标加工场地被移除或改变");
            return false;
        }
        if (Game.GetProcessorDetails(_configuration.ProcessorAnchor).Status == ProcessorStatus.Processing)
        {
            Check("目标加工观察", true, "稳定检查点观察到目标场地真实Processing；现场已有批次不归因于本次买入");
            Stage = ScenarioStage.WaitingForProducts;
        }
        return true;
    }

    private void CreateOrder()
    {
        bool independent = _configuration.Target == "independent";
        ScenarioSnapshot before = Capture();
        if (!independent && Report.Checks[_checkIndices["目标加工观察"]].Status == ScenarioCheckStatus.NotExecuted)
            Check("目标加工观察", ScenarioCheckStatus.InsufficientEvidence, "现场可用产品已达Q，但没有观察到目标加工批次；不把已有库存认作本次产出");
        bool production = Report.Produced == _configuration.Quantity && before.Raw.Total == 0 &&
            before.Product.Total == _configuration.Quantity && before.Processor.Status == ProcessorStatus.WaitingForRaw;
        Check("加工归因", independent ? (production ? ScenarioCheckStatus.Passed : ScenarioCheckStatus.Failed) : ScenarioCheckStatus.InsufficientEvidence,
            independent ? "唯一场地真实产出汇总与Q份原料消耗、目标产品库存、空闲场地状态核对" : "现场没有逐实例批次凭据，产品可能来自已有库存、其他生产或同期操作");
        if (independent && !production)
        {
            Finish(ScenarioOutcome.CheckFailed, "受控Q份加工关系检查失败");
            return;
        }
        _orderRequest = new TradeOrderRequest(_configuration.ProductCommodity, TradeOrderSide.Sell, TradeOrderFrequency.Once,
            TradeOrderQuantityMode.Fixed, _configuration.Quantity, TradeOrderBudgetMode.None, 0, 0, CashReserveMode.Amount, 0,
            new IReadOnlyList<TradeOrderCondition>[] { new[] { new TradeOrderCondition(TradeConditionFactor.Stock, TradeConditionComparison.GreaterOrEqual, _configuration.Quantity) } });
        TradeOrderCommandResult result = Game.CreateTradeOrder(_orderRequest, CommandOrigin.Scenario);
        ScenarioSnapshot after = Capture();
        Report.Operations.Add(new ScenarioOperation("CreateTradeOrder", _lastTicks, _orderRequest, result, before, after));
        if (!result.Success)
        {
            Finish(ScenarioOutcome.OperationRejected, result.ErrorMessage ?? "建单被正式经营拒绝");
            return;
        }
        Report.OrderId = result.Id;
        TradeOrderSnapshot? order = FindOrder();
        bool frozen = order != null && order.Status == TradeOrderStatus.Waiting && order.FrozenQuantity == _configuration.Quantity &&
            order.FrozenCents == 0 && after.Product.Total == before.Product.Total &&
            after.Product.Frozen - (long)before.Product.Frozen == _configuration.Quantity &&
            before.Product.Available - (long)after.Product.Available == _configuration.Quantity &&
            before.BalanceCents == after.BalanceCents;
        Check("订单冻结", frozen, "紧邻建单的同步快照确认Q份本单冻结和可用量减少，建单不立即成交");
        if (!frozen)
        {
            Finish(ScenarioOutcome.CheckFailed, "订单冻结检查失败");
            return;
        }
        _beforeOrder = after;
        Stage = ScenarioStage.WaitingForOrder;
    }

    private void ObserveOrder()
    {
        TradeOrderSnapshot? order = FindOrder();
        if (order == null || !SameRequest(order.Request) || order.Status is TradeOrderStatus.Cancelled or TradeOrderStatus.Disabled)
        {
            Finish(ScenarioOutcome.OperationRejected, "本单被撤销、改动或不再可观察，保留真实经营结果");
            return;
        }
        if (order.Status != TradeOrderStatus.Completed)
            return;
        Report.FillFirstObservedTicks = _lastTicks;
        TradeOrderFillSnapshot? fill = order.LastFill;
        bool filled = fill != null && fill.Commodity == _configuration.ProductCommodity && fill.Side == TradeOrderSide.Sell &&
            fill.Trade.Success && fill.Trade.Quantity == _configuration.Quantity && order.FrozenQuantity == 0 && order.FrozenCents == 0;
        Check("本单成交", filled, "按ID检查真实LastFill商品、方向、Q份成交与本单冻结归零；tick是首次观察时点");
        ScenarioSnapshot final = Capture();
        bool independent = _configuration.Target == "independent";
        bool money = filled && _beforeOrder != null && final.BalanceCents - (long)_beforeOrder.BalanceCents == fill!.Trade.TotalCents - fill.Trade.FeeCents;
        Check("资金归因", independent ? (money ? ScenarioCheckStatus.Passed : ScenarioCheckStatus.Failed) : ScenarioCheckStatus.InsufficientEvidence,
            independent ? "受控成交后余额净增等于本单真实货值减手续费" : "现场缺少完整交易与输入历史，整段余额不能精确归属于本单");
        bool stocks = StockRelation(final.Raw) && StockRelation(final.Product) && final.BalanceCents == (long)final.AvailableCents + final.FrozenCents;
        if (independent)
            stocks &= final.Raw.Total == 0 && final.Product.Total == 0 && final.Product.Frozen == 0 && final.RawReserve == 0;
        Check("库存关系", stocks, "总量等于可用加冻结；受控局另核对Q份全部加工卖出后的零库存");
        foreach (ScenarioCheck check in Report.Checks)
            if (check.Status is ScenarioCheckStatus.Failed or ScenarioCheckStatus.NotExecuted)
            {
                Finish(ScenarioOutcome.CheckFailed, "必需检查失败或尚未执行：" + check.Name);
                return;
            }
        Finish(independent ? ScenarioOutcome.Passed : ScenarioOutcome.CompletedWithInsufficientEvidence,
            independent ? "所有受控检查通过" : "可观察检查通过；现场生产和资金归因证据不足");
    }

    private bool SameRequest(TradeOrderRequest request)
    {
        return _orderRequest != null && request.Commodity == _orderRequest.Commodity && request.Side == _orderRequest.Side &&
            request.Frequency == _orderRequest.Frequency && request.QuantityMode == _orderRequest.QuantityMode &&
            request.Quantity == _orderRequest.Quantity && request.BudgetMode == _orderRequest.BudgetMode && request.BudgetCents == 0 &&
            request.LimitPriceCents == 0 && request.ReserveMode == CashReserveMode.Amount && request.ReserveValue == 0 &&
            request.ConditionGroups.Count == 1 && request.ConditionGroups[0].Count == 1 &&
            request.ConditionGroups[0][0] == _orderRequest.ConditionGroups[0][0];
    }

    /**
     * <summary>给唯一驱动提供下一日期与等待预算共同限制的最大完整tick数。</summary>
     * <returns>运行终止为0；不会忽略日期或重新开始加工预算。</returns>
     */
    public uint GetMaxAdvanceTicks()
    {
        if (!IsRunning)
            return 0;
        uint dateRemaining = _configuration.TimePlan[_timePlanIndex].EndTicks - Game.Calendar.ElapsedSeconds;
        uint budgetRemaining = Stage == ScenarioStage.WaitingForOrder ?
            _configuration.OrderWaitLimitTicks - Report.OrderWaitTicks : _configuration.ProcessingWaitLimitTicks - Report.ProcessingWaitTicks;
        return Math.Min(dateRemaining, budgetRemaining);
    }

    /**
     * <summary>记录宿主实际倍率、来源与暂停观察，不推进经营或消耗预算。</summary>
     * <param name="actualRate">共用时间能力当前倍率。</param>
     * <param name="source">Player或Scenario等实际通知来源。</param>
     * <param name="paused">目标对象当前暂停状态。</param>
     */
    public void ObserveTime(double actualRate, string source, bool paused)
    {
        if (IsRunning)
            Report.TimeObservations.Add(new ScenarioTimeObservation(Game.Calendar.ElapsedSeconds, CurrentRateIntent, actualRate, source, paused));
    }

    /**
     * <summary>在当前稳定时点终止自动流程，保留已发生的交易、生产与玩家倍率。</summary>
     * <param name="reason">用户中止、玩家手动改速或宿主退出等实际原因。</param>
     */
    public void Abort(string reason)
    {
        if (IsRunning)
            Finish(ScenarioOutcome.Aborted, reason);
    }

    /**
     * <summary>仅写入终止时捕获的数据；I/O失败不重新读取现场或更换路径。</summary>
     * <param name="reportDirectory">运行前已检查可写的唯一目录。</param>
     * <returns>report.json绝对路径；尚未终止为调用错误。</returns>
     */
    public string WriteReport(string reportDirectory)
    {
        if (IsRunning)
            throw new InvalidOperationException("流程尚未终止，不能写最终报告");
        return Report.Write(reportDirectory);
    }

    private void Finish(ScenarioOutcome outcome, string reason)
    {
        Report.LastStage = Stage;
        Report.Outcome = outcome;
        Report.Reason = reason;
        Report.Final = Capture();
        Stage = ScenarioStage.Finished;
    }

    private ScenarioSnapshot Capture()
    {
        Godot.Vector2I cell = _configuration.ProcessorAnchor;
        PlotSnapshot plot = Game.TryGetPlot(cell, out PlotSnapshot value) == LandFailure.None ? value : default;
        ProcessorStatus? status = plot.Building == BuildingKind.Processor ? Game.GetProcessorDetails(cell).Status : null;
        return new ScenarioSnapshot(Game.Calendar, Game.MoneyCents, Game.AvailableMoneyCents, Game.FrozenMoneyCents,
            Stock(_configuration.RawCommodity), Stock(_configuration.ProductCommodity), Game.GetRawReserve(_configuration.RawCommodity.Crop),
            new ScenarioProcessor(cell.X, cell.Y, plot.Building, plot.CropKind, plot.RemainingSeconds, status),
            Game.GetBuildingSpaces().Count, Game.GetQuote(_configuration.RawCommodity), Game.GetQuote(_configuration.ProductCommodity), Game.GetTradeOrders());
    }

    private ScenarioStock Stock(CommodityId commodity) => new(Game.GetStock(commodity), Game.GetAvailableStock(commodity), Game.GetFrozenStock(commodity));
    private static bool StockRelation(ScenarioStock stock) => stock.Total == (long)stock.Available + stock.Frozen;
    private string StageText() => Stage switch
    {
        ScenarioStage.Preparing => "准备",
        ScenarioStage.ObservingProcessing => "观察加工",
        ScenarioStage.WaitingForProducts => "等待产品",
        ScenarioStage.WaitingForOrder => "等待本单",
        ScenarioStage.Finished => "结束",
        _ => throw new InvalidOperationException("未知固定流程阶段"),
    };
    private TradeOrderSnapshot? FindOrder()
    {
        foreach (TradeOrderSnapshot order in Game.GetTradeOrders())
            if (order.Id == Report.OrderId)
                return order;
        return null;
    }
    private void Check(string name, bool passed, string evidence) => Check(name, passed ? ScenarioCheckStatus.Passed : ScenarioCheckStatus.Failed, evidence);
    private void Check(string name, ScenarioCheckStatus status, string evidence) => Report.Checks[_checkIndices[name]] = new ScenarioCheck(name, status, evidence);
}
