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
    private readonly string _gameId = Guid.NewGuid().ToString("N");
    private long _commandId;
    private static readonly LogEventDescriptor InitializedEvent = new(3, "GameInitialized", Source);
    private static readonly LogEventDescriptor EndedEvent = new(4, "GameEnded", Source);
    private bool _ended;

    internal GameLog(RuntimeLog owner, FarmGame game, bool collectProduction, Func<long>? productionUptime = null)
    {
        _owner = owner;
        _game = game;
        Trading = new TradingLog(this, game);
        Orders = new TradeOrderLog(this, game);
        Market = new MarketLog(this);
        Production = collectProduction ? new ProductionLog(this, game, productionUptime) : null;
        Gameplay = new GameplayLog(this, game);
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

    internal ProductionLog? Production { get; }

    internal bool CanObserve => !_ended && _owner.Output.IsEnabled;
    internal LogOutput Output => _owner.Output;
    internal CommandObservation NewCommand(CommandDescription description, CommandOrigin origin) =>
        new(this, ++_commandId, description, origin);

    internal void Initialized(int seed) => Observe(() =>
    {
        var fields = Context("Initialization");
        fields["GamePurpose"] = "Main";
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
