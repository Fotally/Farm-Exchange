using System;
using System.Collections.Generic;
using Godot;
using FarmExchange.Cultivation;
using FarmExchange.Economy;
using FarmExchange.Farming;
using FarmExchange.Inventory;
using FarmExchange.Land;
using FarmExchange.Logging;
using FarmExchange.Market;
using FarmExchange.Processing;
using FarmExchange.Time;
using FarmExchange.Trading;
using FarmExchange.Workers;
using FarmExchange.Weather;
using FarmExchange.World;
using GoodsInventory = FarmExchange.Inventory.Inventory;

namespace FarmExchange.Gameplay;

public enum CropStage { None, Seeded, Growing }
public enum BuildingKind { None, Farm, Processor, Road }
public enum CropKind { Wheat, Corn, Rice, Potato, Sunflower, Sugarcane, Radish }
[Flags]
public enum GrowingSeasons { Spring = 1, Summer = 2, Autumn = 4, Winter = 8 }
public enum FarmStatus
{
    WaitingForWorker, WaitingForWorkerWithWater, WaitingForWater, Growing,
    WrongSeason, InsufficientTime, Resting,
}
public enum ProcessorStatus { WaitingForRaw, Processing, WaitingForReserve, ReadyToProcess }
public enum RawReserveFailure { None, InvalidCrop, InvalidQuantity }

public readonly record struct CropDefinition(
    CropKind Kind, string CropName, string BuildingName, string ProductName,
    int GrowthDays, int HarvestQuantity, int ProcessingHalfDays,
    int PricePercent, int RawPricePercent, GrowingSeasons GrowingSeasons);
public readonly record struct PlotSnapshot(
    BuildingKind Building, CropKind CropKind, CropStage Crop, int RemainingSeconds,
    bool HasWater = false, double GrowthProgress = 0);
public readonly record struct FarmDetailsSnapshot(
    CropDefinition Crop, FarmStatus Status, int RawPriceCents, int RawStock, WeatherKind CurrentWeather = WeatherKind.Sunny);
public readonly record struct ProcessorDetailsSnapshot(
    CropDefinition Crop, ProcessorStatus Status, int ProductPriceCents, int ProductStock);
public readonly record struct TickResult(int Harvested, int Produced, bool WorkerActed, bool DayAdvanced);
public readonly record struct SaleResult(
    int Quantity, int RevenueCents, TradeFailure Failure = TradeFailure.None)
{
    public bool Success => Failure == TradeFailure.None;
    public string? ErrorMessage => new TradeResult(Failure, Quantity, RevenueCents).ErrorMessage;
}

public sealed class FarmGame : IDisposable
{
    public const int MapSize = 384;
    public const int BuildingCostCents = 1000;
    private const int CellCount = MapSize * MapSize;

    public static IReadOnlyList<CropDefinition> Crops => CropCatalog.Crops;
    private static readonly Vector2I[] InitialFarmCells =
    {
        new(189, 189), new(192, 189), new(195, 189),
    };
    private static readonly Vector2I[] InitialProcessorCells =
    {
        new(189, 192), new(192, 192),
    };

    private readonly LandOccupancy _occupancy = new(CellCount);
    private readonly FarmingSystem _farming;
    private readonly WeatherSystem _weather;
    private readonly ProcessingSystem _processing = new(CellCount);
    private readonly WorkerScheduler _workerScheduler = new(
        Array.ConvertAll(InitialFarmCells, cell => BuildingFootprint.WorkCell(cell, BuildingKind.Farm)));
    private readonly GoodsInventory _inventory = new();
    private readonly Wallet _wallet = new(5000);
    private readonly MarketQuotes _market;
    private readonly TradingService _trading;
    private readonly TradeOrderBook _tradeOrders;
    private readonly GameCalendar _calendar;
    private readonly CultivationPlanBook _cultivation;
    private readonly List<ProductionResult> _presentationResults = new();
    private IReadOnlyList<ProductionResult>? _presentationSnapshot;
    private readonly GameLog? _log;

    public int MoneyCents => _wallet.BalanceCents;
    public int AvailableMoneyCents => _wallet.AvailableCents;
    public int FrozenMoneyCents => _wallet.FrozenCents;
    public int CurrentDay => (int)_calendar.Snapshot.ElapsedDays + 1;
    public CalendarSnapshot Calendar => _calendar.Snapshot;
    public bool IsPaused => _calendar.IsPaused;

    /**
     * <summary>读取当前真实活动天气区域，不提供未来预排表。</summary>
     * <remarks>原样转发天气模块的不可变快照；活动状态未变化复用引用，实际变化发布新快照且不改写旧对象。</remarks>
     */
    public IReadOnlyList<WeatherRegionSnapshot> ActiveWeatherRegions => _weather.ActiveRegions;

    /**
     * <summary>按基础格查询当前真实天气。</summary>
     * <param name="cell">地图内基础格。</param>
     * <returns>晴天、多云或降雨。</returns>
     */
    public WeatherKind GetWeather(Vector2I cell) => _weather.GetWeather(cell);

    /**
     * <summary>开发／测试显式模式设置互斥矩形天气，自然模式拒绝修改。</summary>
     * <param name="cells">地图内非空基础格矩形。</param>
     * <param name="kind">所设置天气。</param>
     * <param name="error">拒绝原因；失败零修改。</param>
     * <returns>是否成功设置；设置不推进经营或立即供水。</returns>
     */
    public bool TrySetWeatherRegion(Rect2I cells, WeatherKind kind, out string error) =>
        _weather.TrySetRegion(cells, kind, out error);

    /**
     * <summary>开发／测试显式模式移除指定矩形天气，恢复默认晴天。</summary>
     * <param name="cells">既有区域完整矩形。</param>
     * <param name="error">拒绝原因。</param>
     * <returns>是否成功清除，失败零修改。</returns>
     */
    public bool TryClearWeatherRegion(Rect2I cells, out string error) =>
        _weather.TryClearRegion(cells, out error);

    /**
     * <summary>查询本局已绑定的日志观察上下文；未启用采集时为空。</summary>
     */
    public GameLog? Log => _log;
    public int CurrentFlourPriceCents => GetProductPriceCents(CropKind.Wheat);
    public double DailyPriceChangePercent =>
        (double)GetQuote(new CommodityId(CropKind.Wheat, CommodityKind.Product)).ChangePercent;

    /**
     * <summary>创建真实中心设施与报价，并在日志启用时记录初始化基线。</summary>
     * <param name="marketSeed">报价与初始设施随机种子；省略时沿用原随机来源。</param>
     * <param name="logging">组装点持有的采集会话；省略时不采集或写文件。</param>
     * <param name="purpose">本局的真实用途，默认玩家主局。</param>
     * <param name="weatherMode">默认自然预排；开发／测试可选显式区域模式，创建后不切换。</param>
     * <param name="weatherRules">本局已验证不可变天气配置；省略时在此组装点选择正式 Default，创建后不替换。</param>
     * <remarks>日志不拥有经营资源；调用方先释放局再关闭会话。</remarks>
     */
    public FarmGame(int? marketSeed = null, RuntimeLog? logging = null, GamePurpose purpose = GamePurpose.Main, WeatherMode weatherMode = WeatherMode.Natural, WeatherRules? weatherRules = null) : this(marketSeed, 0, logging, purpose, weatherMode, weatherRules) { }

    internal FarmGame(int? marketSeed, uint elapsedSeconds, RuntimeLog? logging = null, GamePurpose purpose = GamePurpose.Main, WeatherMode weatherMode = WeatherMode.Natural, WeatherRules? weatherRules = null)
    {
        if (purpose is not (GamePurpose.Main or GamePurpose.ScenarioIndependent))
            throw new ArgumentOutOfRangeException(nameof(purpose));
        _calendar = new GameCalendar(elapsedSeconds);
        int seed = marketSeed ?? Random.Shared.Next();
        _farming = new FarmingSystem(CellCount, seed);
        _weather = new WeatherSystem(seed, weatherRules ?? WeatherRules.Default, weatherMode, elapsedSeconds);
        _cultivation = new CultivationPlanBook(_farming);
        _market = new MarketQuotes(seed, _calendar.Snapshot.ElapsedDays);
        _trading = new TradingService(_inventory, _wallet, _market);
        _tradeOrders = new TradeOrderBook(_inventory, _wallet, _market, _trading);
        InitializeCenter(new Random(seed));
        _log = logging?.BindGame(this, seed, collectProduction: true, purpose);
        _tradeOrders.AttachLog(_log?.Orders);
        _trading.SetLogging(_log?.Trading);
        _farming.Diagnostics = _log?.ProductionDiagnostics;
        _processing.Diagnostics = _log?.ProductionDiagnostics;
        _workerScheduler.Diagnostics = _log?.WorkerDiagnostics;
    }

    private void InitializeCenter(Random random)
    {
        GrowingSeasons currentSeason = (GrowingSeasons)(1 << (int)_calendar.Snapshot.Season);
        var candidates = new List<CropKind>();
        foreach (CropDefinition crop in Crops)
            if ((crop.GrowingSeasons & currentSeason) != 0)
                candidates.Add(crop.Kind);

        int primaryIndex = random.Next(candidates.Count);
        CropKind primary = candidates[primaryIndex];
        bool split = random.Next(2) == 1;
        CropKind secondary = primary;
        if (split)
        {
            int secondaryIndex = random.Next(candidates.Count - 1);
            if (secondaryIndex >= primaryIndex)
                secondaryIndex++;
            secondary = candidates[secondaryIndex];
        }

        for (int i = 0; i < InitialFarmCells.Length; i++)
            PlaceInitial(InitialFarmCells[i], BuildingKind.Farm, i == 2 ? secondary : primary);
        for (int i = 0; i < InitialProcessorCells.Length; i++)
            PlaceInitial(InitialProcessorCells[i], BuildingKind.Processor, i == 1 ? secondary : primary);
    }

    public static CropDefinition GetCrop(CropKind crop) => CropCatalog.Get(crop);
    public static int GetBuildingCostCents(BuildingKind building) => building switch
    {
        BuildingKind.Farm or BuildingKind.Processor => BuildingCostCents,
        BuildingKind.Road => 100,
        _ => throw new ArgumentOutOfRangeException(nameof(building)),
    };
    public IReadOnlyList<WorkerSnapshot> GetWorkers() => _workerScheduler.GetSnapshots();
    /**
     * <summary>读取按锚点升序排列的当前建筑空间快照。</summary>
     * <remarks>原样转发土地模块的不可变快照；占地未变复用引用，成功建拆后发布新快照，旧对象不变；生产和天气变化不使占地快照失效。</remarks>
     * <returns>只读空间实例集合。</returns>
     */
    public IReadOnlyList<BuildingSpaceSnapshot> GetBuildingSpaces() => _occupancy.Instances;
    public BuildingSpaceSnapshot? GetBuildingSpace(Vector2I cell) =>
        MapCoordinates.ContainsCell(cell) ? _occupancy.GetSpace(IndexOf(cell)) : null;
    public int GetRawStock(CropKind crop) => _inventory.GetRaw(crop);
    public int GetRawReserve(CropKind crop) => _inventory.GetRawReserve(crop);

    /**
     * <summary>设置指定原料的自动加工保留底线。</summary>
     * <remarks>设置不触发领取，也不影响已经投入的原料。</remarks>
     * <param name="crop">原料所属的作物品种。</param>
     * <param name="quantity">要保留的原料份数，须为非负整数。</param>
     * <param name="origin">真实 Player 或 Scenario 来源。</param>
     * <returns>成功返回 None；无效作物返回 InvalidCrop，负数数量返回 InvalidQuantity；失败时底线不变。</returns>
     */
    public RawReserveFailure SetRawReserve(CropKind crop, int quantity, CommandOrigin origin = CommandOrigin.Player)
    {
        ValidateCommandOrigin(origin);
        var observation = _log?.Gameplay.BeginSetRawReserve(crop, quantity, origin);
        RawReserveFailure result;
        try { result = SetRawReserveCore(crop, quantity); }
        catch (Exception error)
        {
            observation?.Faulted(error);
            throw;
        }
        observation?.Complete(result);
        return result;
    }

    private RawReserveFailure SetRawReserveCore(CropKind crop, int quantity)
    {
        if (!CropCatalog.IsDefined(crop))
            return RawReserveFailure.InvalidCrop;
        if (quantity < 0)
            return RawReserveFailure.InvalidQuantity;
        _inventory.SetRawReserve(crop, quantity);
        return RawReserveFailure.None;
    }
    public int GetProductStock(CropKind crop) => _inventory.GetProduct(crop);
    public int GetStock(CommodityId commodity) => _inventory.Get(commodity);
    public int GetAvailableStock(CommodityId commodity) => _inventory.GetAvailable(commodity);
    public int GetFrozenStock(CommodityId commodity) => _inventory.GetFrozen(commodity);
    public MarketQuoteSnapshot GetQuote(CommodityId commodity) => _market.GetQuote(commodity);
    public MarketSnapshot GetMarketSnapshot() => _market.GetSnapshot();
    public int GetProductPriceCents(CropKind crop) =>
        GetQuote(new CommodityId(crop, CommodityKind.Product)).PriceCents;
    public int GetRawPriceCents(CropKind crop) =>
        GetQuote(new CommodityId(crop, CommodityKind.Raw)).PriceCents;

    public PlotSnapshot GetPlot(Vector2I cell)
    {
        if (TryGetPlot(cell, out PlotSnapshot plot) == LandFailure.OutOfBounds)
            throw new ArgumentOutOfRangeException(nameof(cell));
        return plot;
    }

    public LandFailure TryGetPlot(Vector2I cell, out PlotSnapshot plot)
    {
        if (!MapCoordinates.ContainsCell(cell))
        {
            plot = default;
            return LandFailure.OutOfBounds;
        }
        int cellIndex = IndexOf(cell);
        int index = _occupancy.ResolveAnchorIndex(cellIndex);
        if (index < 0)
        {
            plot = new PlotSnapshot(BuildingKind.None, default, CropStage.None, 0);
            return LandFailure.None;
        }
        plot = _occupancy.Get(index) switch
        {
            BuildingKind.Farm => FarmPlot(index),
            BuildingKind.Processor => ProcessorPlot(index),
            BuildingKind.Road => new PlotSnapshot(BuildingKind.Road, default, CropStage.None, 0),
            _ => new PlotSnapshot(BuildingKind.None, default, CropStage.None, 0),
        };
        return LandFailure.None;
    }

    private PlotSnapshot FarmPlot(int index)
    {
        FarmSnapshot farm = _farming.Get(index);
        return new PlotSnapshot(BuildingKind.Farm, farm.CropKind, farm.Stage,
            farm.RemainingSeconds, farm.HasWater, farm.Stage == CropStage.Growing
                ? 1d - (double)farm.RemainingTimeUnits /
                    (GetCrop(farm.CropKind).GrowthDays * GameTimeUnits.PerDay) : 0);
    }

    private PlotSnapshot ProcessorPlot(int index)
    {
        ProcessorSnapshot processor = _processing.Get(index);
        return new PlotSnapshot(BuildingKind.Processor, processor.CropKind,
            CropStage.None, processor.RemainingSeconds);
    }

    public FarmDetailsSnapshot GetFarmDetails(Vector2I cell)
    {
        PlotSnapshot plot = GetPlot(cell);
        if (plot.Building != BuildingKind.Farm)
            throw new InvalidOperationException("该土地没有农田");
        CropDefinition crop = GetCrop(plot.CropKind);
        PlantingFailure plantingFailure = plot.Crop == CropStage.None
            ? PlantingRules.Check(plot.CropKind, _calendar.Snapshot, plot.HasWater)
            : PlantingFailure.None;
        FarmStatus status = plot.Crop switch
        {
            CropStage.Seeded => FarmStatus.WaitingForWater,
            CropStage.Growing => FarmStatus.Growing,
            _ when !_farming.Get(_occupancy.ResolveAnchorIndex(IndexOf(cell))).SowingEnabled => FarmStatus.Resting,
            _ when plantingFailure == PlantingFailure.WrongSeason => FarmStatus.WrongSeason,
            _ when plantingFailure == PlantingFailure.InsufficientTime => FarmStatus.InsufficientTime,
            _ when plot.HasWater => FarmStatus.WaitingForWorkerWithWater,
            _ => FarmStatus.WaitingForWorker,
        };
        return new FarmDetailsSnapshot(crop, status, GetRawPriceCents(crop.Kind),
            GetRawStock(crop.Kind), _weather.GetFarmWeather(_occupancy.GetSpace(IndexOf(cell))!));
    }

    public ProcessorDetailsSnapshot GetProcessorDetails(Vector2I cell)
    {
        PlotSnapshot plot = GetPlot(cell);
        if (plot.Building != BuildingKind.Processor)
            throw new InvalidOperationException("该土地没有加工场地");
        CropDefinition crop = GetCrop(plot.CropKind);
        ProcessorStatus status = _processing.GetStatus(_occupancy.ResolveAnchorIndex(IndexOf(cell)), _inventory);
        return new ProcessorDetailsSnapshot(crop, status, GetProductPriceCents(crop.Kind),
            GetProductStock(crop.Kind));
    }

    internal void FillWorldForBenchmark()
    {
        _log?.Production?.UnregisteredMutation();
        _cultivation.Clear();
        _occupancy.Clear();
        _farming.Clear();
        _processing.Clear();
        for (int row = 0; row < MapSize; row += 3)
        {
            for (int col = 0; col < MapSize; col += 3)
            {
                int index = row * MapSize + col;
                CropKind crop = (CropKind)(((row / 3) * (MapSize / 6) + col / 6) % Crops.Count);
                if (col % 6 == 0)
                {
                    _occupancy.Place(index, BuildingKind.Farm);
                    _farming.SetGrowingForBenchmark(index, crop);
                }
                else
                {
                    _occupancy.Place(index, BuildingKind.Processor);
                    _processing.SetProcessingForBenchmark(index, crop);
                }
            }
        }
    }

    internal void FillWorldForPresentationBenchmark()
    {
        FillWorldForBenchmark();
        foreach (Vector2I cell in new[] { new Vector2I(246, 162), new(252, 156), new(258, 150) })
        {
            int index = IndexOf(cell);
            _farming.Remove(index);
            _farming.Place(index, CropKind.Radish);
        }
        AdvanceTick();
    }

    internal bool HasConsistentState()
    {
        int expectedOccupiedCells = 0;
        int previousAnchorIndex = -1;
        foreach (BuildingSpaceSnapshot space in _occupancy.Instances)
        {
            if (space.AnchorIndex <= previousAnchorIndex ||
                space.AnchorIndex != IndexOf(space.AnchorCell))
                return false;
            previousAnchorIndex = space.AnchorIndex;
            foreach (Vector2I offset in space.Footprint.Offsets)
            {
                Vector2I cell = space.AnchorCell + offset;
                if (!MapCoordinates.ContainsCell(cell) || _occupancy.GetSpace(IndexOf(cell)) != space)
                    return false;
                expectedOccupiedCells++;
            }
        }
        int occupiedCells = 0;
        for (int i = 0; i < CellCount; i++)
        {
            bool hasFarm = _farming.HasFarm(i);
            bool hasProcessor = _processing.HasProcessor(i);
            BuildingKind building = _occupancy.Get(i);
            if (building != BuildingKind.None)
                occupiedCells++;
            bool isAnchor = _occupancy.ResolveAnchorIndex(i) == i;
            if (hasFarm != (isAnchor && building == BuildingKind.Farm) ||
                hasProcessor != (isAnchor && building == BuildingKind.Processor))
                return false;
        }
        return occupiedCells == expectedOccupiedCells;
    }

    /**
     * <summary>只读预检整座设施的描述、朝向、占地和可用资金。</summary>
     * <param name="cell">候选锚点基础格。</param>
     * <param name="building">农田、加工场地或道路。</param>
     * <param name="crop">生产设施的作物；道路忽略此参数。</param>
     * <param name="orientation">生产设施的 q0～q3 朝向；道路仅允许 q0，默认 q0。</param>
     * <returns>允许及建造费用，或稳定拒绝原因和零费用。</returns>
     */
    public PlacementCheck CheckPlacement(Vector2I cell, BuildingKind building, CropKind crop,
        BuildingOrientation orientation = BuildingOrientation.Q0) =>
        CheckPlacementCore(cell, building, crop, orientation);

    private PlacementCheck CheckPlacementCore(Vector2I cell, BuildingKind building, CropKind crop,
        BuildingOrientation orientation)
    {
        if (building is not (BuildingKind.Farm or BuildingKind.Processor or BuildingKind.Road))
            return new PlacementCheck(LandFailure.InvalidBuilding, 0);
        return PlacementRules.Check(cell, building, crop, _occupancy, _wallet.AvailableCents,
            GetBuildingCostCents(building), orientation);
    }

    /**
     * <summary>重验并完整建造一座指定朝向的设施，只收取标准建造费。</summary>
     * <param name="cell">候选锚点基础格。</param>
     * <param name="building">农田、加工场地或道路。</param>
     * <param name="crop">生产设施的作物；道路忽略此参数。</param>
     * <param name="origin">真实 Player 或 Scenario 来源。</param>
     * <param name="orientation">生产设施的 q0～q3 朝向；道路仅允许 q0，默认 q0。</param>
     * <returns>实际扣费，或稳定拒绝原因；正常拒绝不修改资源或实例。</returns>
     * <remarks>朝向由土地登记后固定；当前生产设施均为 3×3，占地、工作中心和生产规则不随朝向改变。</remarks>
     */
    public PlacementResult TryPlace(Vector2I cell, BuildingKind building, CropKind crop,
        CommandOrigin origin = CommandOrigin.Player, BuildingOrientation orientation = BuildingOrientation.Q0)
    {
        ValidateCommandOrigin(origin);
        var observation = _log?.Gameplay.BeginPlace(cell, building, crop, origin, orientation);
        PlacementResult result;
        try { result = TryPlaceCore(cell, building, crop, orientation); }
        catch (Exception error)
        {
            observation?.Faulted(error);
            throw;
        }
        observation?.Complete(result);
        return result;
    }

    private PlacementResult TryPlaceCore(Vector2I cell, BuildingKind building, CropKind crop,
        BuildingOrientation orientation)
    {
        PlacementCheck check = CheckPlacementCore(cell, building, crop, orientation);
        _log?.Placement.Completed(cell, building, crop, orientation, check);
        if (!check.Allowed)
            return new PlacementResult(check.Failure, 0);

        int index = IndexOf(cell);
        if (!_wallet.TrySpend(check.CostCents))
            throw new InvalidOperationException("放置检查与扣费状态不一致");
        PlaceBuilding(index, building, crop, orientation);
        if (building == BuildingKind.Processor)
            StartIdleProcessors();
        return new PlacementResult(LandFailure.None, check.CostCents);
    }

    /**
     * <summary>只读检查整批新道路的材质、全部范围、占用与可用金币。</summary>
     * <param name="cells">不重复的非空候选基础格；不跳过已有道路。</param>
     * <param name="material">本批道路共同材质。</param>
     * <returns>稳定拒绝原因和宽整数总费用；不预留土地或资金。</returns>
     */
    public RoadPlacementCheck CheckRoadPlacement(IReadOnlyList<Vector2I> cells, RoadMaterial material)
    {
        ArgumentNullException.ThrowIfNull(cells);
        long cost = (long)cells.Count * GetBuildingCostCents(BuildingKind.Road);
        if (!PlacementRules.IsValidRoadMaterial(material))
            return new(LandFailure.InvalidRoadMaterial, cells.Count, cost);
        if (cells.Count == 0)
            return new(LandFailure.InvalidRoadRange, 0, cost);
        var unique = new HashSet<Vector2I>();
        foreach (Vector2I cell in cells)
        {
            if (!MapCoordinates.ContainsCell(cell))
                return new(LandFailure.OutOfBounds, cells.Count, cost);
            if (!unique.Add(cell))
                return new(LandFailure.InvalidRoadRange, cells.Count, cost);
        }
        foreach (Vector2I cell in cells)
            if (_occupancy.Get(IndexOf(cell)) != BuildingKind.None)
                return new(LandFailure.Occupied, cells.Count, cost);
        return new(_wallet.AvailableCents < cost ? LandFailure.InsufficientFunds : LandFailure.None,
            cells.Count, cost);
    }

    /**
     * <summary>执行时重验并一次完成整批道路建造与扣费。</summary>
     * <param name="cells">不重复的非空候选基础格。</param>
     * <param name="material">本批道路共同材质。</param>
     * <param name="origin">真实 Player 或 Scenario 来源。</param>
     * <returns>成功的实际格数与扣费，或零修改拒绝。</returns>
     * <remarks>仅登记逐格道路，不记录路段归属；暂停允许操作，冻结金币不可使用。</remarks>
     */
    public RoadPlacementResult TryPlaceRoads(IReadOnlyList<Vector2I> cells, RoadMaterial material,
        CommandOrigin origin = CommandOrigin.Player)
    {
        ValidateCommandOrigin(origin);
        ArgumentNullException.ThrowIfNull(cells);
        var observation = _log?.Gameplay.BeginPlaceRoads(cells, material, origin);
        RoadPlacementResult result;
        try
        {
            RoadPlacementCheck check = CheckRoadPlacement(cells, material);
            if (!check.Allowed)
                result = new(check.Failure, 0, 0);
            else
            {
                if (!_wallet.TrySpend((int)check.CostCents))
                    throw new InvalidOperationException("道路检查与扣费状态不一致");
                foreach (Vector2I cell in cells)
                    _occupancy.Place(IndexOf(cell), BuildingKind.Road, roadMaterial: material);
                result = new(LandFailure.None, cells.Count, check.CostCents);
            }
        }
        catch (Exception error)
        {
            observation?.Faulted(error);
            throw;
        }
        observation?.Complete(result);
        return result;
    }

    /**
     * <summary>完整检查范围后仅拆除其中道路，不退款、不移除其他设施。</summary>
     * <param name="cells">基础格范围；允许空格、生产设施及重复格，越界整批拒绝。</param>
     * <param name="origin">真实 Player 或 Scenario 来源。</param>
     * <returns>实际移除道路格数；没有道路或范围越界时零修改拒绝。</returns>
     */
    public RoadRemovalResult TryRemoveRoads(IReadOnlyList<Vector2I> cells,
        CommandOrigin origin = CommandOrigin.Player)
    {
        ValidateCommandOrigin(origin);
        ArgumentNullException.ThrowIfNull(cells);
        var observation = _log?.Gameplay.BeginRemoveRoads(cells, origin);
        RoadRemovalResult result;
        var roads = new List<BuildingSpaceSnapshot>();
        try
        {
            bool outOfBounds = false;
            foreach (Vector2I cell in cells)
                if (!MapCoordinates.ContainsCell(cell))
                {
                    outOfBounds = true;
                    break;
                }
            if (outOfBounds)
                result = new(LandFailure.OutOfBounds, 0);
            else
            {
                var unique = new HashSet<Vector2I>();
                foreach (Vector2I cell in cells)
                    if (unique.Add(cell) && _occupancy.GetSpace(IndexOf(cell)) is { Building: BuildingKind.Road } road)
                        roads.Add(road);
                if (roads.Count == 0)
                    result = new(LandFailure.NoRoads, 0);
                else
                {
                    foreach (BuildingSpaceSnapshot road in roads)
                        _occupancy.Remove(road.AnchorIndex);
                    result = new(LandFailure.None, roads.Count);
                }
            }
        }
        catch (Exception error)
        {
            observation?.Faulted(error);
            throw;
        }
        observation?.Complete(result, roads);
        return result;
    }

    public string? BuildFarm(Vector2I cell, CommandOrigin origin = CommandOrigin.Player) =>
        PlacementError(TryPlace(cell, BuildingKind.Farm, CropKind.Wheat, origin));

    public string? BuildProcessor(Vector2I cell, CropKind crop, CommandOrigin origin = CommandOrigin.Player) =>
        PlacementError(TryPlace(cell, BuildingKind.Processor, crop, origin));

    /**
     * <summary>立即手动改种并解除本田共享年度表，强制重启当前轮。</summary>
     * <param name="cell">任一农田子格。</param>
     * <param name="crop">合法作物品种。</param>
     * <param name="origin">真实 Player 或 Scenario 来源。</param>
     * <returns>成功为空，正常拒绝为中文原因。</returns>
     */
    public string? SetFarmCrop(Vector2I cell, CropKind crop, CommandOrigin origin = CommandOrigin.Player)
    {
        ValidateCommandOrigin(origin);
        var observation = _log?.Cultivation.BeginManualControl(cell, crop, CultivationMode.Immediate, origin);
        string? result;
        try { result = SetFarmCropCore(cell, crop); }
        catch (Exception error)
        {
            observation?.Faulted(error);
            throw;
        }
        observation?.Complete(result);
        return result;
    }

    private string? SetFarmCropCore(Vector2I cell, CropKind crop)
    {
        if (!MapCoordinates.ContainsCell(cell))
            return PlacementRules.ErrorMessage(LandFailure.OutOfBounds);
        if (!CropCatalog.IsDefined(crop))
            return "无效作物";
        int index = _occupancy.ResolveAnchorIndex(IndexOf(cell));
        if (index < 0 || _occupancy.Get(index) != BuildingKind.Farm)
            return "该土地没有农田";
        _cultivation.TakeManualControl(index, crop, preserveCurrent: false);
        return null;
    }

    /**
     * <summary>设置本田的手动下一轮目标并解除共享耕作表关联。</summary>
     * <remarks>保留已播种或生长中的本轮；空田立即设置目标。命令不推进经营。</remarks>
     * <param name="cell">任一农田子格。</param>
     * <param name="crop">合法作物品种。</param>
     * <param name="origin">真实 Player 或 Scenario 来源。</param>
     * <returns>成功为空，正常拒绝为中文原因。</returns>
     */
    public string? PrepareFarmCrop(Vector2I cell, CropKind crop, CommandOrigin origin = CommandOrigin.Player)
    {
        ValidateCommandOrigin(origin);
        var observation = _log?.Cultivation.BeginManualControl(cell, crop, CultivationMode.PrepareNext, origin);
        string? result;
        try { result = PrepareFarmCropCore(cell, crop); }
        catch (Exception error)
        {
            observation?.Faulted(error);
            throw;
        }
        observation?.Complete(result);
        return result;
    }

    private string? PrepareFarmCropCore(Vector2I cell, CropKind crop)
    {
        if (!MapCoordinates.ContainsCell(cell))
            return PlacementRules.ErrorMessage(LandFailure.OutOfBounds);
        if (!CropCatalog.IsDefined(crop))
            return "无效作物";
        int index = _occupancy.ResolveAnchorIndex(IndexOf(cell));
        if (index < 0 || _occupancy.Get(index) != BuildingKind.Farm)
            return "该土地没有农田";
        _cultivation.TakeManualControl(index, crop, preserveCurrent: true);
        return null;
    }

    /**
     * <summary>查询选种的当前适季性及预计成熟风险，不执行选种。</summary>
     * <param name="cell">任一农田子格。</param>
     * <param name="crop">合法作物品种。</param>
     * <returns>不适季、预计时间不足提示或无风险；时间不足不阻止播种。</returns>
     */
    public PlantingFailure GetPlantingCheck(Vector2I cell, CropKind crop)
    {
        PlotSnapshot plot = GetPlot(cell);
        if (plot.Building != BuildingKind.Farm)
            throw new InvalidOperationException("该土地没有农田");
        return PlantingRules.Check(crop, Calendar, plot.HasWater);
    }

    /**
     * <summary>只读查询共享年度表及引用数量。</summary>
     * <returns>独立只读快照。</returns>
     */
    public IReadOnlyList<CultivationPlanSnapshot> GetCultivationPlans() => _cultivation.GetSnapshots();
    /**
     * <summary>只读查询指定共享年度表及其引用数量。</summary>
     * <param name="id">共享表编号。</param>
     * <returns>独立只读快照；不存在时为空。</returns>
     */
    public CultivationPlanSnapshot? GetCultivationPlan(int id) => _cultivation.GetSnapshot(id);
    /**
     * <summary>只读检查作物条年度排程，允许在未命名草稿中编辑。</summary>
     * <param name="entries">完整候选年度作物条。</param>
     * <returns>正常拒绝或风险条编号，不要求耕作表名称。</returns>
     */
    public CultivationValidation CheckCultivationEntries(IReadOnlyList<CultivationEntry> entries) =>
        CultivationPlanBook.ValidateEntries(entries);
    /**
     * <summary>检查提交草稿的名称、执行方式与完整年度排程。</summary>
     * <param name="request">名称、表级模式和年度作物条。</param>
     * <returns>正常拒绝或风险条编号。</returns>
     */
    public CultivationValidation CheckCultivationPlan(CultivationPlanRequest request) => CultivationPlanBook.Validate(request);
    /**
     * <summary>创建共享年度耕作表，不推进经营。</summary>
     * <param name="request">完整表设置。</param>
     * <param name="origin">真实 Player 或 Scenario 来源。</param>
     * <returns>新表编号或正常拒绝。</returns>
     */
    public CultivationCommandResult CreateCultivationPlan(CultivationPlanRequest request, CommandOrigin origin = CommandOrigin.Player)
    {
        ValidateCommandOrigin(origin);
        var observation = _log?.Cultivation.BeginCreate(request, origin);
        CultivationCommandResult result;
        try { result = _cultivation.Create(request); }
        catch (Exception error)
        {
            observation?.Faulted(error);
            throw;
        }
        observation?.Complete(result);
        return result;
    }
    /**
     * <summary>完整更新共享表，保留全部引用田的当前轮并重算安排。</summary>
     * <param name="id">共享表编号。</param>
     * <param name="request">完整表设置。</param>
     * <param name="origin">真实 Player 或 Scenario 来源。</param>
     * <returns>同一编号或零修改的正常拒绝。</returns>
     */
    public CultivationCommandResult UpdateCultivationPlan(int id, CultivationPlanRequest request, CommandOrigin origin = CommandOrigin.Player)
    {
        ValidateCommandOrigin(origin);
        var observation = _log?.Cultivation.BeginUpdate(id, request, origin);
        CultivationCommandResult result;
        try { result = _cultivation.Update(id, request, (long)Calendar.ElapsedSeconds * GameTimeUnits.PerSecond); }
        catch (Exception error)
        {
            observation?.Faulted(error);
            throw;
        }
        observation?.Complete(result);
        return result;
    }
    /**
     * <summary>删除共享年度表并解除全部引用田的计划安排。</summary>
     * <remarks>保留各田当前作物、当前轮和水分，恢复按当前作物自动复种；不推进经营或改变资源。</remarks>
     * <param name="id">共享年度表编号。</param>
     * <param name="origin">真实 Player 或 Scenario 来源。</param>
     * <returns>成功为空；表不存在时返回中文原因且零修改。</returns>
     */
    public string? DeleteCultivationPlan(int id, CommandOrigin origin = CommandOrigin.Player)
    {
        ValidateCommandOrigin(origin);
        var observation = _log?.Cultivation.BeginDelete(id, origin);
        string? result;
        try { result = _cultivation.Delete(id); }
        catch (Exception error)
        {
            observation?.Faulted(error);
            throw;
        }
        observation?.Complete(result);
        return result;
    }
    /**
     * <summary>对选定农田原子应用同一共享表，保留正在种植的本轮。</summary>
     * <param name="id">共享表编号。</param>
     * <param name="cells">农田任意子格列表，重复引用同一实例仅应用一次。</param>
     * <param name="origin">真实 Player 或 Scenario 来源。</param>
     * <returns>成功为空；任一目标无效时全部不修改。</returns>
     */
    public string? ApplyCultivationPlan(int id, IReadOnlyList<Vector2I> cells, CommandOrigin origin = CommandOrigin.Player)
    {
        ValidateCommandOrigin(origin);
        var observation = _log?.Cultivation.BeginApply(id, cells, origin);
        string? result;
        IReadOnlyList<int>? resolvedIndices;
        try { result = ApplyCultivationPlanCore(id, cells, out resolvedIndices); }
        catch (Exception error)
        {
            observation?.Faulted(error);
            throw;
        }
        observation?.Complete(result, resolvedIndices);
        return result;
    }

    private string? ApplyCultivationPlanCore(int id, IReadOnlyList<Vector2I> cells, out IReadOnlyList<int>? resolvedIndices)
    {
        resolvedIndices = null;
        if (!_cultivation.Contains(id))
            return "耕作表不存在";
        if (cells.Count == 0)
        {
            resolvedIndices = Array.Empty<int>();
            return "请先选择农田";
        }
        var indices = new SortedSet<int>();
        foreach (Vector2I cell in cells)
        {
            if (!MapCoordinates.ContainsCell(cell))
                return PlacementRules.ErrorMessage(LandFailure.OutOfBounds);
            int index = _occupancy.ResolveAnchorIndex(IndexOf(cell));
            if (index < 0 || _occupancy.Get(index) != BuildingKind.Farm)
                return "所选土地没有农田";
            indices.Add(index);
        }
        resolvedIndices = new List<int>(indices);
        _cultivation.Apply(id, resolvedIndices, (long)Calendar.ElapsedSeconds * GameTimeUnits.PerSecond);
        return null;
    }
    /**
     * <summary>只读查询本田共享引用和已缓存的下一轮安排。</summary>
     * <param name="cell">任一农田子格。</param>
     * <returns>只读安排；非农田属于调用错误。</returns>
     */
    public FarmCultivationSnapshot GetFarmCultivation(Vector2I cell)
    {
        if (GetPlot(cell).Building != BuildingKind.Farm)
            throw new InvalidOperationException("该土地没有农田");
        return _cultivation.GetFarm(_occupancy.ResolveAnchorIndex(IndexOf(cell)));
    }

    public string? RemoveBuilding(Vector2I cell, CommandOrigin origin = CommandOrigin.Player)
    {
        ValidateCommandOrigin(origin);
        var observation = _log?.Gameplay.BeginRemove(cell, origin);
        string? result;
        try { result = RemoveBuildingCore(cell); }
        catch (Exception error)
        {
            observation?.Faulted(error);
            throw;
        }
        observation?.Complete(result);
        return result;
    }

    private string? RemoveBuildingCore(Vector2I cell)
    {
        if (!MapCoordinates.ContainsCell(cell))
            return PlacementRules.ErrorMessage(LandFailure.OutOfBounds);
        int index = _occupancy.ResolveAnchorIndex(IndexOf(cell));
        if (index < 0)
            return "该土地没有建筑";
        BuildingKind building = _occupancy.Get(index);
        switch (building)
        {
            case BuildingKind.Farm:
                _cultivation.RemoveFarm(index);
                _farming.Remove(index);
                break;
            case BuildingKind.Processor:
                _processing.Remove(index);
                break;
            case BuildingKind.Road:
                break;
        }
        _occupancy.Remove(index);
        _presentationResults.RemoveAll(result => IndexOf(result.AnchorCell) == index);
        _presentationSnapshot = null;
        return null;
    }

    public TickResult AdvanceTick()
    {
        using var observation = _log?.Simulation.BeginTick();
        _log?.Production?.BeginAdvance();
        bool completed = false;
        try
        {
            TickResult result = AdvanceTickCore(observation);
            completed = true;
            return result;
        }
        catch (Exception error)
        {
            observation?.Faulted(error);
            throw;
        }
        finally
        {
            _log?.Production?.EndAdvance(completed);
            _log?.Diagnostics.Poll();
        }
    }

    private TickResult AdvanceTickCore(SimulationLogOperation? observation)
    {
        if (_calendar.IsPaused)
            return default;
        if (_calendar.Snapshot.ElapsedSeconds == uint.MaxValue)
            throw new InvalidOperationException("模拟时间已达到上限");
        return AdvanceEventTick(observation);
    }

    /**
     * <summary>一次请求推进无外部输入区间，平静数据直接累计，真实事件按原相位结算。</summary>
     * <remarks>整段先校验日历容量；检查点可提交正式命令，修改后须返回 false 停止并重算请求。单秒与批量读取同一天气状态；天气开始与结束是完整经营事件。</remarks>
     * <param name="maxTicks">最多推进的完整经营秒数；零或暂停时不推进。</param>
     * <param name="checkpoint">真实事件及请求终点的稳定回调，true 继续，false 停止；不能嵌套推进。</param>
     * <returns>实际秒数、宽整数产出、事件/平静秒数及检查点停止标识。</returns>
     */
    public SimulationAdvanceResult AdvanceTicks(uint maxTicks, Func<SimulationCheckpoint, bool>? checkpoint = null)
    {
        using var observation = _log?.Simulation.BeginBatch();
        long? started = _log?.Performance?.BeginBatch();
        _log?.Production?.BeginAdvance();
        bool completed = false;
        SimulationAdvanceResult result = default;
        try
        {
            result = AdvanceTicksCore(maxTicks, checkpoint, observation);
            completed = true;
            return result;
        }
        catch (Exception error)
        {
            observation?.Faulted(error);
            throw;
        }
        finally
        {
            _log?.Production?.EndAdvance(completed);
            _log?.Diagnostics.Poll();
            _log?.Performance?.EndBatch(started, completed, result);
        }
    }

    private SimulationAdvanceResult AdvanceTicksCore(uint maxTicks, Func<SimulationCheckpoint, bool>? checkpoint,
        SimulationLogOperation? observation)
    {
        if (_calendar.IsPaused || maxTicks == 0)
            return default;
        if (maxTicks > uint.MaxValue - Calendar.ElapsedSeconds)
            throw new InvalidOperationException("模拟时间请求超出上限");
        observation?.EnterPhase(SimulationLogPhase.Orders);
        _tradeOrders.BeginAdvanceRequest();
        uint advanced = 0, quiet = 0, events = 0, interval = 0;
        long harvested = 0, produced = 0;
        bool workerActed = false, dayAdvanced = false;
        while (advanced < maxTicks)
        {
            uint remaining = maxTicks - advanced;
            observation?.EnterPhase(SimulationLogPhase.EventSearch);
            uint nextEvent = GetNextEventSeconds();
            uint quietSpan = Math.Min(remaining, nextEvent - 1);
            bool intervalDayAdvanced = false;
            if (quietSpan > 0)
            {
                intervalDayAdvanced = AdvanceQuietSeconds(quietSpan, observation);
                advanced += quietSpan;
                quiet += quietSpan;
                interval += quietSpan;
                dayAdvanced |= intervalDayAdvanced;
            }
            TickResult result = new(0, 0, false, intervalDayAdvanced);
            bool isEvent = advanced < maxTicks;
            if (isEvent)
            {
                result = AdvanceEventTick(observation);
                advanced++;
                events++;
                interval++;
                harvested += result.Harvested;
                produced += result.Produced;
                workerActed |= result.WorkerActed;
                dayAdvanced |= result.DayAdvanced;
                result = result with { DayAdvanced = result.DayAdvanced || intervalDayAdvanced };
            }
            if (checkpoint != null)
            {
                observation?.EnterPhase(SimulationLogPhase.Checkpoint);
                if (!checkpoint(new SimulationCheckpoint(
                    advanced, interval, Calendar.ElapsedSeconds, result, isEvent)))
                    return new(advanced, harvested, produced, workerActed, dayAdvanced, true, quiet, events);
            }
            interval = 0;
        }
        return new(advanced, harvested, produced, workerActed, dayAdvanced, false, quiet, events);
    }

    private uint GetNextEventSeconds()
    {
        uint next = Math.Min(_calendar.SecondsUntilNextSeason,
            _calendar.SecondsUntilDay(_market.NextEventDay));
        next = Math.Min(next, _weather.GetNextEventSeconds(Calendar.ElapsedSeconds));
        next = Math.Min(next, _cultivation.GetNextEventSeconds(Calendar.ElapsedSeconds));
        next = Math.Min(next, _workerScheduler.GetNextEventSeconds(_farming, Calendar));
        if (_tradeOrders.NeedsNextTickCheck)
            next = 1;
        foreach (BuildingSpaceSnapshot space in _occupancy.Instances)
        {
            if (space.Building == BuildingKind.Farm)
            {
                next = Math.Min(next, _farming.GetNextEventSeconds(space.AnchorIndex));
                if (!_farming.Get(space.AnchorIndex).HasWater && _weather.GetFarmWeather(space) == WeatherKind.Rain)
                    next = 1;
            }
            else if (space.Building == BuildingKind.Processor)
                next = Math.Min(next, _processing.GetNextEventSeconds(space.AnchorIndex, _inventory));
        }
        return next;
    }

    private bool AdvanceQuietSeconds(uint seconds, SimulationLogOperation? observation)
    {
        observation?.EnterPhase(SimulationLogPhase.QuietAdvance);
        ClearPresentationResults();
        uint previousDay = Calendar.ElapsedDays;
        foreach (BuildingSpaceSnapshot space in _occupancy.Instances)
        {
            if (space.Building == BuildingKind.Farm)
                _farming.AdvanceQuietSeconds(space.AnchorIndex, seconds, _weather.GetFarmWeather(space));
            else if (space.Building == BuildingKind.Processor)
                _processing.AdvanceQuietSeconds(space.AnchorIndex, seconds);
        }
        _workerScheduler.AdvanceQuietSeconds(seconds);
        observation?.EnterPhase(SimulationLogPhase.Calendar);
        if (!_calendar.TryAdvanceSeconds(seconds))
            throw new InvalidOperationException("模拟时间已达到上限");
        _weather.AdvanceTo(Calendar.ElapsedSeconds);
        if (Calendar.ElapsedDays == previousDay)
            return false;
        _market.Advance(Calendar, _log?.Market);
        return true;
    }

    private TickResult AdvanceEventTick(SimulationLogOperation? observation)
    {
        observation?.EnterPhase(SimulationLogPhase.TickPreparation);
        var timing = _log?.Performance?.BeginEventTick(Calendar.ElapsedSeconds);
        ClearPresentationResults();
        ApplyRain();
        int harvested = 0;
        int produced = 0;
        long nextTimeUnits = ((long)_calendar.Snapshot.ElapsedSeconds + 1) * GameTimeUnits.PerSecond;
        foreach (BuildingSpaceSnapshot space in _occupancy.Instances)
        {
            int i = space.AnchorIndex;
            BuildingKind building = space.Building;
            if (building == BuildingKind.Farm)
            {
                observation?.EnterPhase(SimulationLogPhase.Harvest);
                timing?.BeginStage();
                var before = _log?.ProductionDiagnostics.BeginFarm(i, _farming);
                if (_farming.AdvanceGrowth(i, out CropHarvestResult harvest, _weather.GetFarmWeather(space)))
                    harvested += CollectHarvest(i, harvest, nextTimeUnits, before);
                timing?.EndStage("Harvest");
            }
            else if (building == BuildingKind.Processor)
            {
                observation?.EnterPhase(SimulationLogPhase.Processing);
                timing?.BeginStage();
                var before = _log?.ProductionDiagnostics.BeginProcessor(i, _processing, _inventory);
                if (_processing.Advance(i, out CropKind productCrop))
                {
                    _inventory.AddProduct(productCrop, 1);
                    _log?.Production?.Produced(productCrop);
                    _log?.ProductionDiagnostics.Processed(i, before, _processing, _inventory, 1);
                    RecordPresentationResult(i, productCrop, ProductionResultKind.Product, 1);
                    produced++;
                }
                timing?.EndStage("Processing");
            }
        }
        observation?.EnterPhase(SimulationLogPhase.Harvest);
        Season nextSeason = GameCalendar.GetDate((uint)(nextTimeUnits / GameTimeUnits.PerDay)).Season;
        if (nextSeason != _calendar.Snapshot.Season)
        {
            timing?.BeginStage();
            foreach (int index in _farming.Indices)
            {
                var before = _log?.ProductionDiagnostics.BeginFarm(index, _farming);
                if (_farming.TryMatureBeforeDisallowedSeason(index, nextSeason, out CropHarvestResult rescue))
                    harvested += CollectHarvest(index, rescue, nextTimeUnits, before);
            }
            timing?.EndStage("Harvest");
        }
        observation?.EnterPhase(SimulationLogPhase.RawClaim);
        timing?.BeginStage();
        StartIdleProcessors();
        timing?.EndStage("RawClaim");
        observation?.EnterPhase(SimulationLogPhase.Workers);
        timing?.BeginStage();
        bool workerActed = _workerScheduler.AdvanceOneSecond(_farming, _calendar.Snapshot,
            (number, work) =>
            {
                _log?.Production?.WorkerCompleted(work.Kind);
                RecordPresentationResult(work.CellIndex, _farming.Get(work.CellIndex).CropKind,
                    work.Kind == FarmWorkKind.Sow ? ProductionResultKind.Sow : ProductionResultKind.Water,
                    0, number, work);
            });
        _cultivation.RecordSownCrops();
        timing?.EndStage("Workers");
        observation?.EnterPhase(SimulationLogPhase.Calendar);
        timing?.BeginStage();
        bool dayAdvanced = AdvanceDay();
        _cultivation.Synchronize(nextTimeUnits);
        timing?.EndStage("Calendar");
        observation?.EnterPhase(SimulationLogPhase.Orders);
        timing?.BeginStage();
        _tradeOrders.Execute(_calendar.Snapshot);
        timing?.EndStage("Orders");
        timing?.Complete();
        return new TickResult(harvested, produced, workerActed, dayAdvanced);
    }

    private int CollectHarvest(int index, CropHarvestResult harvest, long nextTimeUnits, FarmSnapshot? before)
    {
        CropKind crop = harvest.Crop;
        int quantity = harvest.Quantity;
        _inventory.AddRaw(crop, quantity);
        _log?.Production?.Harvested(crop, quantity);
        _log?.ProductionDiagnostics.Harvested(index, before, _farming, quantity);
        RecordPresentationResult(index, crop, ProductionResultKind.Harvest, quantity);
        _cultivation.Harvested(index, nextTimeUnits);
        return quantity;
    }

    /**
     * <summary>读取最近完整经营秒的逐实例真实结果，平静秒与下一秒覆盖旧结果。</summary>
     * <remarks>不可修改的独立快照；初次挂接跳过已有秒，重复读取按秒去重，播放前重验有效性。</remarks>
     * <returns>播种、浇水、自动收获及加工成功；不含等待、失败或历史队列。</returns>
     */
    public IReadOnlyList<ProductionResult> GetPresentationResults() =>
        _presentationSnapshot ??= Array.AsReadOnly(_presentationResults.ToArray());

    /**
     * <summary>验证表现结果仍属于当前秒与原实例，农田动作仍属于原轮次。</summary>
     * <param name="result">从本局最近结果快照读取的结果。</param>
     * <returns>拆除重建、农田改种重启、换季清理或新经营秒使旧结果失效。</returns>
     */
    public bool IsPresentationResultCurrent(ProductionResult result)
    {
        if (result.ElapsedSeconds != Calendar.ElapsedSeconds || !_presentationResults.Contains(result))
            return false;
        return IsPresentationTargetCurrent(result);
    }

    /**
     * <summary>验证已经开始播放的成功结果仍属于原设施及有效工作轮次，允许片段跨经营秒完成。</summary>
     * <remarks>仅用于已经消费的片段；开始播放仍须使用最近秒结果及 IsPresentationResultCurrent，不补播历史。</remarks>
     * <param name="result">本局已消费的真实成功结果。</param>
     * <returns>原实例仍在且农田工作凭据有效；拆建、改种或换季清理使其失效。</returns>
     */
    public bool IsPresentationTargetCurrent(ProductionResult result)
    {
        if (result.Space == null || !ReferenceEquals(GetBuildingSpace(result.AnchorCell), result.Space))
            return false;
        return result.CompletedWork is not FarmWorkRequest work || _farming.IsWorkRevisionCurrent(work);
    }

    private void ClearPresentationResults()
    {
        _presentationResults.Clear();
        _presentationSnapshot = null;
    }

    private void RecordPresentationResult(int index, CropKind crop, ProductionResultKind kind,
        int quantity, int workerNumber = 0, FarmWorkRequest? completedWork = null)
    {
        _presentationResults.Add(new ProductionResult(Calendar.ElapsedSeconds + 1,
            new Vector2I(index % MapSize, index / MapSize), crop, kind, quantity, workerNumber)
        {
            CompletedWork = completedWork,
            Space = _occupancy.GetSpace(index),
        });
        _presentationSnapshot = null;
    }

    /**
     * <summary>按当前报价完整买入单商品，并关联原始指令和真实结算。</summary>
     * <param name="commodity">请求商品，非法枚举保持原值并正常拒绝。</param>
     * <param name="quantity">原请求份数，须为正整数。</param>
     * <param name="origin">实际 Player 或 Scenario 来源；其他值在业务提交前抛出 ArgumentOutOfRangeException。</param>
     * <returns>真实成交或资源零修改的正常拒绝；业务异常原样传播。</returns>
     */
    public TradeResult Buy(CommodityId commodity, int quantity, CommandOrigin origin = CommandOrigin.Player)
    {
        ValidateCommandOrigin(origin);
        return ExecuteTrade(_log?.Trading.BeginBuy(commodity, quantity, origin), () => _trading.Buy(commodity, quantity));
    }

    private static TradeResult ExecuteTrade(TradeLogOperation? observation, Func<TradeResult> execute)
    {
        TradeResult result;
        try { result = execute(); }
        catch (Exception error)
        {
            observation?.Faulted(error);
            throw;
        }
        observation?.Complete(result);
        return result;
    }

    private static void ValidateCommandOrigin(CommandOrigin origin)
    {
        if (origin is not CommandOrigin.Player and not CommandOrigin.Scenario)
            throw new ArgumentOutOfRangeException(nameof(origin));
    }

    /**
     * <summary>结束本局日志关联；重复释放不产生第二条结束事件。</summary>
     * <remarks>经营状态保持原有唯一拥有者，释放不修改资金、库存或生产。</remarks>
     */
    public void Dispose() => _log?.Dispose();

    /**
     * <summary>按执行时报价完整卖出指定数量，观察真实提交结果。</summary>
     * <param name="commodity">原请求商品。</param>
     * <param name="quantity">原请求份数。</param>
     * <param name="origin">真实 Player 或 Scenario 来源；非法来源在提交前抛参数异常。</param>
     * <returns>真实结算或资源零修改的拒绝。</returns>
     */
    public TradeResult Sell(CommodityId commodity, int quantity, CommandOrigin origin = CommandOrigin.Player)
    {
        ValidateCommandOrigin(origin);
        return ExecuteTrade(_log?.Trading.BeginSell(commodity, quantity, origin), () => _trading.Sell(commodity, quantity));
    }

    /**
     * <summary>完整卖出某商品可用库存；空库存成功返回零。</summary>
     * <param name="commodity">原请求商品。</param>
     * <param name="origin">真实 Player 或 Scenario 来源。</param>
     * <returns>实际全售结果；不出售冻结库存。</returns>
     */
    public TradeResult SellCommodityAll(CommodityId commodity, CommandOrigin origin = CommandOrigin.Player)
    {
        ValidateCommandOrigin(origin);
        return ExecuteTrade(_log?.Trading.BeginSellAll(commodity, origin), () => _trading.SellAll(commodity));
    }

    /**
     * <summary>读取按建单顺序排列的独立只读委托快照。</summary>
     * <returns>全部活动和已结束委托，查询不执行交易。</returns>
     */
    public IReadOnlyList<TradeOrderSnapshot> GetTradeOrders() => _tradeOrders.GetSnapshots();

    internal bool IsDiagnosticOrderActive(int id) => _tradeOrders.IsDiagnosticOrderActive(id);

    /**
     * <summary>创建委托；一次单冻结资源，持续策略不冻结。</summary>
     * <param name="request">商品、完整条件组、数量、预算和现金保留设置。</param>
     * <param name="origin">真实 Player 或 Scenario 来源。</param>
     * <returns>新委托 ID 或零修改的中文拒绝原因。</returns>
     * <remarks>锁定当前现金基准，不立即执行；暂停时仍可提交。</remarks>
     */
    public TradeOrderCommandResult CreateTradeOrder(TradeOrderRequest request, CommandOrigin origin = CommandOrigin.Player)
    {
        ValidateCommandOrigin(origin);
        return ExecuteOrder(_log?.Orders.BeginCreate(request, origin), () => _tradeOrders.Create(request));
    }

    /**
     * <summary>完整替换活动委托并重验冻结，保持原 ID、顺序和现金基准。</summary>
     * <param name="id">原活动委托 ID。</param>
     * <param name="request">新的完整设置。</param>
     * <param name="origin">真实 Player 或 Scenario 来源。</param>
     * <returns>成功或零修改的正常拒绝。</returns>
     */
    public TradeOrderCommandResult UpdateTradeOrder(int id, TradeOrderRequest request, CommandOrigin origin = CommandOrigin.Player)
    {
        ValidateCommandOrigin(origin);
        return ExecuteOrder(_log?.Orders.BeginEdit(id, request, origin), () => _tradeOrders.Update(id, request));
    }

    /**
     * <summary>撤销活动委托并释放该单资源；结束记录仍保留。</summary>
     * <param name="id">活动委托 ID。</param>
     * <param name="origin">真实 Player 或 Scenario 来源。</param>
     * <returns>成功或已经结束的正常拒绝。</returns>
     */
    public TradeOrderCommandResult CancelTradeOrder(int id, CommandOrigin origin = CommandOrigin.Player)
    {
        ValidateCommandOrigin(origin);
        return ExecuteOrder(_log?.Orders.BeginCancel(id, origin), () => _tradeOrders.Cancel(id));
    }

    /**
     * <summary>启用或停用持续策略，不推进经营。</summary>
     * <param name="id">活动持续策略 ID。</param>
     * <param name="enabled">是否启用。</param>
     * <param name="origin">真实 Player 或 Scenario 来源。</param>
     * <returns>成功或不支持该操作的正常拒绝。</returns>
     */
    public TradeOrderCommandResult SetTradeOrderEnabled(int id, bool enabled, CommandOrigin origin = CommandOrigin.Player)
    {
        ValidateCommandOrigin(origin);
        return ExecuteOrder(_log?.Orders.BeginSetEnabled(id, enabled, origin), () => _tradeOrders.SetEnabled(id, enabled));
    }

    private static TradeOrderCommandResult ExecuteOrder(OrderLogOperation? observation, Func<TradeOrderCommandResult> execute)
    {
        TradeOrderCommandResult result;
        try { result = execute(); }
        catch (Exception error)
        {
            observation?.Faulted(error);
            throw;
        }
        observation?.Complete(result);
        return result;
    }

    /**
     * <summary>一次完整出售七种加工品的可用库存；日志消费同次结算的逐商品明细。</summary>
     * <param name="origin">真实 Player 或 Scenario 来源。</param>
     * <returns>原兼容接口的真实合计或资源零修改的拒绝。</returns>
     */
    public SaleResult SellAll(CommandOrigin origin = CommandOrigin.Player)
    {
        ValidateCommandOrigin(origin);
        ProductSaleLogOperation? observation = _log?.Trading.BeginSellAllProducts(origin);
        ProductSaleResult result;
        try { result = _trading.SellAllProductsDetailed(); }
        catch (Exception error)
        {
            observation?.Faulted(error);
            throw;
        }
        observation?.Complete(result);
        return LegacySale(result.Trade);
    }
    /**
     * <summary>经同一单商品全售入口卖出指定原料，保持旧返回格式。</summary>
     * <param name="crop">请求原料作物。</param>
     * <param name="origin">真实 Player 或 Scenario 来源。</param>
     * <returns>实际成交合计或资源零修改的拒绝。</returns>
     */
    public SaleResult SellRaw(CropKind crop, CommandOrigin origin = CommandOrigin.Player) =>
        LegacySale(SellCommodityAll(new CommodityId(crop, CommodityKind.Raw), origin));

    private static SaleResult LegacySale(TradeResult result) =>
        new((int)result.Quantity, (int)result.TotalCents, result.Failure);

    private bool AdvanceDay()
    {
        CalendarSnapshot previousCalendar = _calendar.Snapshot;
        if (!_calendar.TryAdvanceSeconds(1))
            throw new InvalidOperationException("模拟时间已达到上限");
        CalendarSnapshot calendar = _calendar.Snapshot;
        _weather.AdvanceTo(calendar.ElapsedSeconds);
        if (calendar.Season != previousCalendar.Season)
        {
            CropClearResult cleared = _farming.ClearDisallowedCrops(calendar.Season);
            _log?.Production?.Cleared(cleared);
        }
        if (calendar.ElapsedDays == previousCalendar.ElapsedDays)
            return false;
        _market.Advance(calendar, _log?.Market);
        return true;
    }

    /**
     * <summary>设置经营暂停状态，不推进时间或改变未完成秒进度。</summary>
     * <param name="paused">目标暂停状态。</param>
     * <param name="origin">真实 Player 或 Scenario 来源。</param>
     * <remarks>重复设置仍是一次命令，仅实际变化产生暂停变化事件。</remarks>
     */
    public void SetPaused(bool paused, CommandOrigin origin = CommandOrigin.Player)
    {
        ValidateCommandOrigin(origin);
        var observation = _log?.Time.BeginPause(paused, origin);
        try { _calendar.SetPaused(paused); }
        catch (Exception error)
        {
            observation?.Faulted(error);
            throw;
        }
        observation?.Complete(IsPaused);
    }

    private void ApplyRain()
    {
        foreach (BuildingSpaceSnapshot space in _occupancy.Instances)
            if (space.Building == BuildingKind.Farm && _weather.GetFarmWeather(space) == WeatherKind.Rain)
                _farming.SupplyWater(space.AnchorIndex);
    }

    private static string? PlacementError(PlacementResult result) =>
        result.Success ? null : PlacementRules.ErrorMessage(result.Failure);

    private void PlaceInitial(Vector2I cell, BuildingKind building, CropKind crop)
    {
        PlacementCheck check = PlacementRules.Check(cell, building, crop, _occupancy,
            _wallet.BalanceCents, 0);
        if (!check.Allowed)
            throw new InvalidOperationException("开局建筑放置无效");
        int index = IndexOf(cell);
        PlaceBuilding(index, building, crop);
    }

    private void StartIdleProcessors()
    {
        foreach (BuildingSpaceSnapshot space in _occupancy.Instances)
            if (space.Building == BuildingKind.Processor && _processing.TryStart(space.AnchorIndex, _inventory))
                _log?.Production?.Consumed(_processing.Get(space.AnchorIndex).CropKind);
    }

    private void PlaceFarm(int index, CropKind crop, BuildingOrientation orientation)
    {
        _farming.Place(index, crop);
        _occupancy.Place(index, BuildingKind.Farm, orientation);
    }

    private void PlaceProcessor(int index, CropKind crop, BuildingOrientation orientation)
    {
        _processing.Place(index, crop);
        _occupancy.Place(index, BuildingKind.Processor, orientation);
    }

    private void PlaceBuilding(int index, BuildingKind building, CropKind crop,
        BuildingOrientation orientation = BuildingOrientation.Q0)
    {
        switch (building)
        {
            case BuildingKind.Farm:
                PlaceFarm(index, crop, orientation);
                break;
            case BuildingKind.Processor:
                PlaceProcessor(index, crop, orientation);
                break;
            case BuildingKind.Road:
                _occupancy.Place(index, BuildingKind.Road, orientation);
                break;
        }
    }

    private static int IndexOf(Vector2I cell)
    {
        if (!MapCoordinates.ContainsCell(cell))
            throw new ArgumentOutOfRangeException(nameof(cell));
        return cell.Y * MapSize + cell.X;
    }
}
