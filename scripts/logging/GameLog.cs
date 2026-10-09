using System;
using System.Collections.Generic;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
using FarmExchange.Market;

namespace FarmExchange.Logging;

/**
 * <summary>绑定一局真实经营的领域观察上下文，封装局身份、共同上下文及领域记录入口。</summary>
 */
public sealed class GameLog : IDisposable
{
    private const string Source = "FarmExchange.Gameplay.FarmGame";
    private readonly RuntimeLog _owner;
    private readonly FarmGame _game;
    private readonly GamePurpose _purpose;
    private readonly string _gameId = Guid.NewGuid().ToString("N");
    private long _commandId;
    private ExceptionObservation? _exceptionScope;
    private static readonly LogEventDescriptor InitializedEvent = new(3, "GameInitialized", Source);
    private static readonly LogEventDescriptor EndedEvent = new(4, "GameEnded", Source);
    private bool _ended;

    internal GameLog(RuntimeLog owner, FarmGame game, bool collectProduction, GamePurpose purpose, Func<long>? productionUptime = null, Func<long>? diagnosticUptime = null, Func<long>? timestamp = null)
    {
        _owner = owner;
        _game = game;
        _purpose = purpose;
        Diagnostics = new DiagnosticCapture(this, game, diagnosticUptime);
        Performance = collectProduction ? new PerformanceLog(this, game, diagnosticUptime, timestamp) : null;
        ProductionDiagnostics = new ProductionDiagnostics(this);
        WorkerDiagnostics = new WorkerDiagnostics(this);
        View = new ViewDiagnostics(this);
        Placement = new PlacementDiagnostics(this);
        Trading = new TradingLog(this, game);
        Orders = new TradeOrderLog(this, game);
        Market = new MarketLog(this);
        Production = collectProduction ? new ProductionLog(this, game, productionUptime) : null;
        Gameplay = new GameplayLog(this, game);
        Cultivation = new CultivationLog(this, game);
        Time = new TimeLog(this, game);
        Simulation = new SimulationLog(this);
        Scenario = new ScenarioLog(this);
    }

    /**
     * <summary>本局主动交易的领域观察入口，不执行交易。</summary>
     */
    public TradingLog Trading { get; }

    /**
     * <summary>本局订单配置、真实成交与等待观察入口。</summary>
     */
    public TradeOrderLog Orders { get; }

    /**
     * <summary>本局实际公告与正式报价观察入口。</summary>
     */
    public MarketLog Market { get; }

    /**
     * <summary>本局建拆及库存底线命令观察入口。</summary>
     */
    public GameplayLog Gameplay { get; }

    /**
     * <summary>本局共享年度表与手动接管命令观察入口。</summary>
     */
    public CultivationLog Cultivation { get; }

    /**
     * <summary>本局暂停和已接受倍率选择的观察入口。</summary>
     */
    public TimeLog Time { get; }

    /**
     * <summary>本局单秒和批量推进异常的观察入口。</summary>
     */
    public SimulationLog Simulation { get; }

    /**
     * <summary>关联本局的开发流程生命周期观察入口。</summary>
     */
    public ScenarioLog Scenario { get; }

    /**
     * <summary>本局显式限定事件、对象、时长和条数的开发诊断入口。</summary>
     */
    public DiagnosticCapture Diagnostics { get; }

    internal ProductionDiagnostics ProductionDiagnostics { get; }
    internal WorkerDiagnostics WorkerDiagnostics { get; }
    internal ViewDiagnostics View { get; }
    internal PlacementDiagnostics Placement { get; }
    internal PerformanceLog? Performance { get; }
    internal ProductionLog? Production { get; }

    internal bool CanObserve => !_ended && _owner.Output.IsEnabled;
    internal LogOutput Output => _owner.Output;
    internal CommandObservation NewCommand(CommandDescription description, CommandOrigin origin) =>
        new(this, ++_commandId, description, origin);

    internal ExceptionObservation NewExceptionObservation() => new(this, _exceptionScope);

    internal ExceptionObservation BeginExceptionScope() => _exceptionScope = NewExceptionObservation();

    internal void EndExceptionScope()
    {
        while (_exceptionScope?.IsClosed == true) _exceptionScope = _exceptionScope.Parent;
    }

    internal void Initialized(int seed) => Observe(() =>
    {
        var fields = Context("Initialization");
        fields["GamePurpose"] = _purpose.ToString();
        fields["Seed"] = seed;
        var facilities = new List<Dictionary<string, object?>>();
        foreach (var space in _game.GetBuildingSpaces())
            facilities.Add(new()
            {
                ["Anchor"] = new Dictionary<string, object?> { ["X"] = space.AnchorCell.X, ["Y"] = space.AnchorCell.Y },
                ["BuildingKind"] = space.Building.ToString(),
                ["Crop"] = _game.GetPlot(space.AnchorCell).CropKind.ToString(),
            });
        fields["InitialFacilities"] = facilities;
        var inventory = new Dictionary<string, object?>();
        foreach (var commodity in CommodityCatalog.All)
            inventory[CommodityName(commodity.Id)] = new Dictionary<string, object?>
            {
                ["Total"] = _game.GetStock(commodity.Id),
                ["Available"] = _game.GetAvailableStock(commodity.Id),
                ["Frozen"] = _game.GetFrozenStock(commodity.Id),
            };
        fields["Inventory"] = inventory;
        fields["MoneyCents"] = _game.MoneyCents;
        fields["AvailableMoneyCents"] = _game.AvailableMoneyCents;
        fields["FrozenMoneyCents"] = _game.FrozenMoneyCents;
        _owner.Output.Submit(InitializedEvent, "经营局初始化完成", fields);
    });

    internal void End(string reason)
    {
        if (_ended) return;
        Orders.End();
        Production?.End();
        Performance?.End();
        Diagnostics.End(reason == "Shutdown" ? "Shutdown" : "GameEnded");
        _ended = true;
        _owner.Output.Observe(() =>
        {
            var fields = Context("Shutdown");
            fields["EndReason"] = reason;
            _owner.Output.Submit(EndedEvent, "经营局结束", fields);
        });
        _owner.Release(this);
    }

    /**
     * <summary>幂等结束本局观察关联，不修改经营资源。</summary>
     */
    public void Dispose() => End("Released");

    internal void Observe(Action observation)
    {
        if (!_ended) _owner.Output.Observe(observation);
    }

    internal Dictionary<string, object?> Context(string phase)
    {
        var calendar = _game.Calendar;
        return new()
        {
            ["GameInstanceId"] = _gameId,
            ["SimulationSeconds"] = calendar.ElapsedSeconds,
            ["GameDate"] = new Dictionary<string, object?> { ["Year"] = calendar.Year, ["Month"] = calendar.Month, ["Day"] = calendar.Day },
            ["Season"] = calendar.Season.ToString(),
            ["IsPaused"] = calendar.IsPaused,
            ["Phase"] = phase,
        };
    }

    private static string CommodityName(CommodityId commodity) => commodity.Crop + "." + commodity.Kind;
}
