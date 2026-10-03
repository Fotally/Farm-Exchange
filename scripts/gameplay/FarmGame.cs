using System;
using System.Collections.Generic;
using Godot;
using FarmExchange.Cultivation;
using FarmExchange.Economy;
using FarmExchange.Farming;
using FarmExchange.Inventory;
using FarmExchange.Land;
using FarmExchange.Market;
using FarmExchange.Processing;
using FarmExchange.Time;
using FarmExchange.Trading;
using FarmExchange.Workers;
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
    bool HasWater = false);
public readonly record struct FarmDetailsSnapshot(
    CropDefinition Crop, FarmStatus Status, int RawPriceCents, int RawStock);
public readonly record struct ProcessorDetailsSnapshot(
    CropDefinition Crop, ProcessorStatus Status, int ProductPriceCents, int ProductStock);
public readonly record struct TickResult(int Harvested, int Produced, bool WorkerActed, bool DayAdvanced);
public readonly record struct SaleResult(
    int Quantity, int RevenueCents, TradeFailure Failure = TradeFailure.None)
{
    public bool Success => Failure == TradeFailure.None;
    public string? ErrorMessage => new TradeResult(Failure, Quantity, RevenueCents).ErrorMessage;
}

public sealed class FarmGame
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
    private readonly FarmingSystem _farming = new(CellCount);
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

    public int MoneyCents => _wallet.BalanceCents;
    public int AvailableMoneyCents => _wallet.AvailableCents;
    public int FrozenMoneyCents => _wallet.FrozenCents;
    public int CurrentDay => (int)_calendar.Snapshot.ElapsedDays + 1;
    public CalendarSnapshot Calendar => _calendar.Snapshot;
    public bool IsPaused => _calendar.IsPaused;
    public int CurrentFlourPriceCents => GetProductPriceCents(CropKind.Wheat);
    public double DailyPriceChangePercent =>
        (double)GetQuote(new CommodityId(CropKind.Wheat, CommodityKind.Product)).ChangePercent;

    public FarmGame(int? marketSeed = null) : this(marketSeed, 0) { }

    internal FarmGame(int? marketSeed, uint elapsedSeconds)
    {
        _calendar = new GameCalendar(elapsedSeconds);
        _cultivation = new CultivationPlanBook(_farming);
        int seed = marketSeed ?? Random.Shared.Next();
        _market = new MarketQuotes(seed, _calendar.Snapshot.ElapsedDays);
        _trading = new TradingService(_inventory, _wallet, _market);
        _tradeOrders = new TradeOrderBook(_inventory, _wallet, _market, _trading);
        InitializeCenter(new Random(seed));
    }

    private void InitializeCenter(Random random)
    {
        CropKind primary = (CropKind)random.Next(Crops.Count);
        bool split = random.Next(2) == 1;
        CropKind secondary = primary;
        if (split)
        {
            int secondaryIndex = random.Next(Crops.Count - 1);
            if (secondaryIndex >= (int)primary)
                secondaryIndex++;
            secondary = (CropKind)secondaryIndex;
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
    public IReadOnlyList<BuildingSpaceSnapshot> GetBuildingSpaces() => _occupancy.Instances;
    public BuildingSpaceSnapshot? GetBuildingSpace(Vector2I cell) =>
        MapCoordinates.ContainsCell(cell) ? _occupancy.GetSpace(IndexOf(cell)) : null;
    public int GetRawStock(CropKind crop) => _inventory.GetRaw(crop);
    public int GetRawReserve(CropKind crop) => _inventory.GetRawReserve(crop);

    public RawReserveFailure SetRawReserve(CropKind crop, int quantity)
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
            farm.RemainingSeconds, farm.HasWater);
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
            GetRawStock(crop.Kind));
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

    public PlacementCheck CheckPlacement(Vector2I cell, BuildingKind building, CropKind crop)
    {
        if (building is not (BuildingKind.Farm or BuildingKind.Processor or BuildingKind.Road))
            return new PlacementCheck(LandFailure.InvalidBuilding, 0);
        return PlacementRules.Check(cell, building, crop, _occupancy, _wallet.AvailableCents,
            GetBuildingCostCents(building));
    }

    public PlacementResult TryPlace(Vector2I cell, BuildingKind building, CropKind crop)
    {
        PlacementCheck check = CheckPlacement(cell, building, crop);
        if (!check.Allowed)
            return new PlacementResult(check.Failure, 0);

        int index = IndexOf(cell);
        if (!_wallet.TrySpend(check.CostCents))
            throw new InvalidOperationException("放置检查与扣费状态不一致");
        PlaceBuilding(index, building, crop);
        if (building == BuildingKind.Processor)
            StartIdleProcessors();
        return new PlacementResult(LandFailure.None, check.CostCents);
    }

    public string? BuildFarm(Vector2I cell) =>
        PlacementError(TryPlace(cell, BuildingKind.Farm, CropKind.Wheat));

    public string? BuildProcessor(Vector2I cell, CropKind crop) =>
        PlacementError(TryPlace(cell, BuildingKind.Processor, crop));

    public string? SetFarmCrop(Vector2I cell, CropKind crop)
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

    /** <summary>设置本田的手动下一轮目标并解除共享耕作表关联。</summary>
     * <remarks>保留已播种或生长中的本轮；空田立即设置目标。命令不推进经营。</remarks>
     * <param name="cell">任一农田子格。</param><param name="crop">合法作物品种。</param>
     * <returns>成功为空，正常拒绝为中文原因。</returns> */
    public string? PrepareFarmCrop(Vector2I cell, CropKind crop)
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

    /** <summary>查询选种的当前适季性及预计成熟风险，不执行选种。</summary>
     * <param name="cell">任一农田子格。</param><param name="crop">合法作物品种。</param>
     * <returns>不适季、预计时间不足提示或无风险；时间不足不阻止播种。</returns> */
    public PlantingFailure GetPlantingCheck(Vector2I cell, CropKind crop)
    {
        PlotSnapshot plot = GetPlot(cell);
        if (plot.Building != BuildingKind.Farm)
            throw new InvalidOperationException("该土地没有农田");
        return PlantingRules.Check(crop, Calendar, plot.HasWater);
    }

    /** <summary>只读查询共享年度表及引用数量。</summary><returns>独立只读快照。</returns> */
    public IReadOnlyList<CultivationPlanSnapshot> GetCultivationPlans() => _cultivation.GetSnapshots();
    /** <summary>检查完整草稿的年度排程冲突与禁生季风险。</summary>
     * <param name="request">名称、表级模式和年度作物条。</param><returns>正常拒绝或风险条编号。</returns> */
    public CultivationValidation CheckCultivationPlan(CultivationPlanRequest request) => CultivationPlanBook.Validate(request);
    /** <summary>创建共享年度耕作表，不推进经营。</summary>
     * <param name="request">完整表设置。</param><returns>新表编号或正常拒绝。</returns> */
    public CultivationCommandResult CreateCultivationPlan(CultivationPlanRequest request) => _cultivation.Create(request);
    /** <summary>完整更新共享表，保留全部引用田的当前轮并重算安排。</summary>
     * <param name="id">共享表编号。</param><param name="request">完整表设置。</param>
     * <returns>同一编号或零修改的正常拒绝。</returns> */
    public CultivationCommandResult UpdateCultivationPlan(int id, CultivationPlanRequest request) =>
        _cultivation.Update(id, request, (long)Calendar.ElapsedSeconds * GameTimeUnits.PerSecond);
    /** <summary>对选定农田原子应用同一共享表，保留正在种植的本轮。</summary>
     * <param name="id">共享表编号。</param><param name="cells">农田任意子格列表，重复引用同一实例仅应用一次。</param>
     * <returns>成功为空；任一目标无效时全部不修改。</returns> */
    public string? ApplyCultivationPlan(int id, IReadOnlyList<Vector2I> cells)
    {
        if (!_cultivation.Contains(id))
            return "耕作表不存在";
        if (cells.Count == 0)
            return "请先选择农田";
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
        _cultivation.Apply(id, new List<int>(indices), (long)Calendar.ElapsedSeconds * GameTimeUnits.PerSecond);
        return null;
    }
    /** <summary>只读查询本田共享引用和已缓存的下一轮安排。</summary>
     * <param name="cell">任一农田子格。</param><returns>只读安排；非农田属于调用错误。</returns> */
    public FarmCultivationSnapshot GetFarmCultivation(Vector2I cell)
    {
        if (GetPlot(cell).Building != BuildingKind.Farm)
            throw new InvalidOperationException("该土地没有农田");
        return _cultivation.GetFarm(_occupancy.ResolveAnchorIndex(IndexOf(cell)));
    }

    public string? RemoveBuilding(Vector2I cell)
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
        return null;
    }

    public TickResult AdvanceTick(bool isRaining = false)
    {
        if (_calendar.IsPaused)
            return default;
        if (_calendar.Snapshot.ElapsedSeconds == uint.MaxValue)
            throw new InvalidOperationException("模拟时间已达到上限");
        if (isRaining)
            ApplyRain();
        int harvested = 0;
        int produced = 0;
        long nextTimeUnits = ((long)_calendar.Snapshot.ElapsedSeconds + 1) * GameTimeUnits.PerSecond;
        foreach (BuildingSpaceSnapshot space in _occupancy.Instances)
        {
            int i = space.AnchorIndex;
            BuildingKind building = space.Building;
            if (building == BuildingKind.Farm && _farming.AdvanceGrowth(i, out CropKind harvestedCrop))
            {
                harvested += CollectHarvest(i, harvestedCrop, nextTimeUnits);
            }
            else if (building == BuildingKind.Processor && _processing.Advance(i, out CropKind productCrop))
            {
                _inventory.AddProduct(productCrop, 1);
                produced++;
            }
        }
        Season nextSeason = GameCalendar.GetDate((uint)(nextTimeUnits / GameTimeUnits.PerDay)).Season;
        if (nextSeason != _calendar.Snapshot.Season)
            foreach (int index in _farming.Indices)
                if (_farming.TryMatureBeforeDisallowedSeason(index, nextSeason, out CropKind rescuedCrop))
                    harvested += CollectHarvest(index, rescuedCrop, nextTimeUnits);
        StartIdleProcessors();
        bool workerActed = _workerScheduler.AdvanceOneSecond(_farming, _calendar.Snapshot);
        _cultivation.RecordSownCrops();
        bool dayAdvanced = AdvanceDay();
        _cultivation.Synchronize(nextTimeUnits);
        _tradeOrders.Execute(_calendar.Snapshot);
        return new TickResult(harvested, produced, workerActed, dayAdvanced);
    }

    private int CollectHarvest(int index, CropKind crop, long nextTimeUnits)
    {
        int quantity = GetCrop(crop).HarvestQuantity;
        _inventory.AddRaw(crop, quantity);
        _cultivation.Harvested(index, nextTimeUnits);
        return quantity;
    }

    public TradeResult Buy(CommodityId commodity, int quantity) => _trading.Buy(commodity, quantity);
    public TradeResult Sell(CommodityId commodity, int quantity) => _trading.Sell(commodity, quantity);
    public TradeResult SellCommodityAll(CommodityId commodity) => _trading.SellAll(commodity);

    /** <summary>读取按建单顺序排列的独立只读委托快照。</summary>
     * <returns>全部活动和已结束委托，查询不执行交易。</returns> */
    public IReadOnlyList<TradeOrderSnapshot> GetTradeOrders() => _tradeOrders.GetSnapshots();

    /** <summary>创建委托；一次单冻结资源，持续策略不冻结。</summary>
     * <param name="request">商品、完整条件组、数量、预算和现金保留设置。</param>
     * <returns>新委托 ID 或零修改的中文拒绝原因。</returns>
     * <remarks>锁定当前现金基准，不立即执行；暂停时仍可提交。</remarks> */
    public TradeOrderCommandResult CreateTradeOrder(TradeOrderRequest request) => _tradeOrders.Create(request);

    /** <summary>完整替换活动委托并重验冻结，保持原 ID、顺序和现金基准。</summary>
     * <param name="id">原活动委托 ID。</param>
     * <param name="request">新的完整设置。</param>
     * <returns>成功或零修改的正常拒绝。</returns> */
    public TradeOrderCommandResult UpdateTradeOrder(int id, TradeOrderRequest request) => _tradeOrders.Update(id, request);

    /** <summary>撤销活动委托并释放该单资源；结束记录仍保留。</summary>
     * <param name="id">活动委托 ID。</param>
     * <returns>成功或已经结束的正常拒绝。</returns> */
    public TradeOrderCommandResult CancelTradeOrder(int id) => _tradeOrders.Cancel(id);

    /** <summary>启用或停用持续策略，不推进经营。</summary>
     * <param name="id">活动持续策略 ID。</param>
     * <param name="enabled">是否启用。</param>
     * <returns>成功或不支持该操作的正常拒绝。</returns> */
    public TradeOrderCommandResult SetTradeOrderEnabled(int id, bool enabled) => _tradeOrders.SetEnabled(id, enabled);

    public SaleResult SellAll() => LegacySale(_trading.SellAllProducts());
    public SaleResult SellRaw(CropKind crop) =>
        LegacySale(SellCommodityAll(new CommodityId(crop, CommodityKind.Raw)));

    private static SaleResult LegacySale(TradeResult result) =>
        new((int)result.Quantity, (int)result.TotalCents, result.Failure);

    private bool AdvanceDay()
    {
        CalendarSnapshot previousCalendar = _calendar.Snapshot;
        if (!_calendar.TryAdvanceSeconds(1))
            throw new InvalidOperationException("模拟时间已达到上限");
        CalendarSnapshot calendar = _calendar.Snapshot;
        if (calendar.Season != previousCalendar.Season)
            _farming.ClearDisallowedCrops(calendar.Season);
        if (calendar.ElapsedDays == previousCalendar.ElapsedDays)
            return false;
        _market.Advance(calendar);
        return true;
    }

    public void SetPaused(bool paused) => _calendar.SetPaused(paused);

    private void ApplyRain()
    {
        foreach (BuildingSpaceSnapshot space in _occupancy.Instances)
            if (space.Building == BuildingKind.Farm)
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
            if (space.Building == BuildingKind.Processor)
                _processing.TryStart(space.AnchorIndex, _inventory);
    }

    private void PlaceFarm(int index, CropKind crop)
    {
        _farming.Place(index, crop);
        _occupancy.Place(index, BuildingKind.Farm);
    }

    private void PlaceProcessor(int index, CropKind crop)
    {
        _processing.Place(index, crop);
        _occupancy.Place(index, BuildingKind.Processor);
    }

    private void PlaceBuilding(int index, BuildingKind building, CropKind crop)
    {
        switch (building)
        {
            case BuildingKind.Farm:
                PlaceFarm(index, crop);
                break;
            case BuildingKind.Processor:
                PlaceProcessor(index, crop);
                break;
            case BuildingKind.Road:
                _occupancy.Place(index, BuildingKind.Road);
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
