using System;
using System.Collections.Generic;
using Godot;
using FarmExchange.Economy;
using FarmExchange.Farming;
using FarmExchange.Land;
using FarmExchange.Market;
using FarmExchange.Processing;
using FarmExchange.Time;
using FarmExchange.Workers;
using FarmExchange.World;
using GoodsInventory = FarmExchange.Inventory.Inventory;

namespace FarmExchange.Gameplay;

public enum CropStage { None, Seeded, Growing }
public enum BuildingKind { None, Farm, Processor }
public enum CropKind { Wheat, Corn, Rice, Potato, Sunflower, Sugarcane, Radish }
public enum FarmStatus { WaitingForWorker, WaitingForWater, Growing }
public enum ProcessorStatus { WaitingForRaw, Processing }

public readonly record struct CropDefinition(
    CropKind Kind, string CropName, string BuildingName, string ProductName,
    int GrowthDays, int HarvestQuantity, int ProcessingHalfDays,
    int PricePercent, int RawPricePercent);
public readonly record struct PlotSnapshot(
    BuildingKind Building, CropKind CropKind, CropStage Crop, int RemainingSeconds);
public readonly record struct FarmDetailsSnapshot(
    CropDefinition Crop, FarmStatus Status, int RawPriceCents, int RawStock);
public readonly record struct ProcessorDetailsSnapshot(
    CropDefinition Crop, ProcessorStatus Status, int ProductPriceCents, int ProductStock);
public readonly record struct TickResult(int Harvested, int Produced, bool WorkerActed, bool DayAdvanced);
public readonly record struct SaleResult(int Quantity, int RevenueCents);

public sealed class FarmGame
{
    public const int MapSize = 128;
    public const int BuildingCostCents = 1000;
    private const int CellCount = MapSize * MapSize;

    public static IReadOnlyList<CropDefinition> Crops => CropCatalog.Crops;
    private static readonly Vector2I[] InitialFarmCells =
    {
        new(63, 63), new(64, 63), new(65, 63),
    };
    private static readonly Vector2I[] InitialProcessorCells =
    {
        new(63, 64), new(64, 64),
    };

    private readonly LandOccupancy _occupancy = new(CellCount);
    private readonly FarmingSystem _farming = new(CellCount);
    private readonly ProcessingSystem _processing = new(CellCount);
    private readonly WorkerScheduler _workerScheduler = new();
    private readonly GoodsInventory _inventory = new();
    private readonly Wallet _wallet = new(5000);
    private readonly MarketPriceCurve _marketPriceCurve;
    private readonly GameCalendar _calendar = new();

    public int MoneyCents => _wallet.BalanceCents;
    public int CurrentDay => (int)_calendar.Snapshot.ElapsedDays + 1;
    public CalendarSnapshot Calendar => _calendar.Snapshot;
    public bool IsPaused => _calendar.IsPaused;
    public int CurrentFlourPriceCents { get; private set; }
    public double DailyPriceChangePercent { get; private set; }

    public FarmGame(int? marketSeed = null)
    {
        int seed = marketSeed ?? Random.Shared.Next();
        _marketPriceCurve = new MarketPriceCurve(seed);
        CurrentFlourPriceCents = _marketPriceCurve.GetPriceCents(CurrentDay);
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
    public int GetRawStock(CropKind crop) => _inventory.GetRaw(crop);
    public int GetProductStock(CropKind crop) => _inventory.GetProduct(crop);
    public int GetProductPriceCents(CropKind crop) =>
        (CurrentFlourPriceCents * GetCrop(crop).PricePercent + 50) / 100;
    public int GetRawPriceCents(CropKind crop) =>
        (GetProductPriceCents(crop) * GetCrop(crop).RawPricePercent + 50) / 100;

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
        int index = IndexOf(cell);
        plot = _occupancy.Get(index) switch
        {
            BuildingKind.Farm => FarmPlot(index),
            BuildingKind.Processor => ProcessorPlot(index),
            _ => new PlotSnapshot(BuildingKind.None, default, CropStage.None, 0),
        };
        return LandFailure.None;
    }

    private PlotSnapshot FarmPlot(int index)
    {
        FarmSnapshot farm = _farming.Get(index);
        return new PlotSnapshot(BuildingKind.Farm, farm.CropKind, farm.Stage, farm.RemainingSeconds);
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
        FarmStatus status = plot.Crop switch
        {
            CropStage.Seeded => FarmStatus.WaitingForWater,
            CropStage.Growing => FarmStatus.Growing,
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
        ProcessorStatus status = plot.RemainingSeconds > 0
            ? ProcessorStatus.Processing : ProcessorStatus.WaitingForRaw;
        return new ProcessorDetailsSnapshot(crop, status, GetProductPriceCents(crop.Kind),
            GetProductStock(crop.Kind));
    }

    internal void FillWorldForBenchmark()
    {
        _occupancy.Clear();
        _farming.Clear();
        _processing.Clear();
        for (int row = 0; row < MapSize; row++)
        {
            for (int col = 0; col < MapSize; col++)
            {
                int index = row * MapSize + col;
                CropKind crop = (CropKind)((row * (MapSize / 2) + col / 2) % Crops.Count);
                if (col % 2 == 0)
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

    internal bool HasConsistentState()
    {
        for (int i = 0; i < CellCount; i++)
        {
            bool hasFarm = _farming.HasFarm(i);
            bool hasProcessor = _processing.HasProcessor(i);
            BuildingKind building = _occupancy.Get(i);
            if (hasFarm != (building == BuildingKind.Farm) ||
                hasProcessor != (building == BuildingKind.Processor))
                return false;
        }
        return true;
    }

    public PlacementCheck CheckPlacement(Vector2I cell, BuildingKind building, CropKind crop) =>
        PlacementRules.Check(cell, building, crop, _occupancy, _wallet.BalanceCents,
            BuildingCostCents);

    public PlacementResult TryPlace(Vector2I cell, BuildingKind building, CropKind crop)
    {
        PlacementCheck check = CheckPlacement(cell, building, crop);
        if (!check.Allowed)
            return new PlacementResult(check.Failure, 0);

        int index = IndexOf(cell);
        if (!_wallet.TrySpend(check.CostCents))
            throw new InvalidOperationException("放置检查与扣费状态不一致");
        if (building == BuildingKind.Farm)
            PlaceFarm(index, crop);
        else
        {
            PlaceProcessor(index, crop);
            StartIdleProcessors();
        }
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
        int index = IndexOf(cell);
        if (_occupancy.Get(index) != BuildingKind.Farm)
            return "该土地没有农田";
        _farming.SetCrop(index, crop);
        return null;
    }

    public string? RemoveBuilding(Vector2I cell)
    {
        if (!MapCoordinates.ContainsCell(cell))
            return PlacementRules.ErrorMessage(LandFailure.OutOfBounds);
        int index = IndexOf(cell);
        BuildingKind building = _occupancy.Get(index);
        if (building == BuildingKind.None)
            return "该土地没有建筑";
        if (building == BuildingKind.Farm)
            _farming.Remove(index);
        else
            _processing.Remove(index);
        _occupancy.Remove(index);
        return null;
    }

    public TickResult AdvanceTick()
    {
        if (_calendar.IsPaused)
            return default;
        if (_calendar.Snapshot.ElapsedSeconds == uint.MaxValue)
            throw new InvalidOperationException("模拟时间已达到上限");
        int harvested = 0;
        int produced = 0;
        for (int i = 0; i < CellCount; i++)
        {
            BuildingKind building = _occupancy.Get(i);
            if (building == BuildingKind.Farm && _farming.AdvanceGrowth(i, out CropKind harvestedCrop))
            {
                int quantity = GetCrop(harvestedCrop).HarvestQuantity;
                _inventory.AddRaw(harvestedCrop, quantity);
                harvested += quantity;
            }
            else if (building == BuildingKind.Processor && _processing.Advance(i, out CropKind productCrop))
            {
                _inventory.AddProduct(productCrop, 1);
                produced++;
            }
        }
        StartIdleProcessors();
        bool workerActed = _workerScheduler.WorkOne(_farming);
        bool dayAdvanced = AdvanceDay();
        return new TickResult(harvested, produced, workerActed, dayAdvanced);
    }

    public SaleResult SellAll()
    {
        long sold = 0;
        long revenue = 0;
        foreach (CropDefinition crop in Crops)
        {
            int quantity = _inventory.GetProduct(crop.Kind);
            sold += quantity;
            revenue += (long)quantity * GetProductPriceCents(crop.Kind);
        }
        int soldQuantity = checked((int)sold);
        int revenueCents = checked((int)revenue);
        _wallet.Credit(revenueCents);
        _inventory.TakeAllProducts();
        return new SaleResult(soldQuantity, revenueCents);
    }

    public SaleResult SellRaw(CropKind crop)
    {
        int sold = _inventory.GetRaw(crop);
        int revenueCents = checked((int)((long)sold * GetRawPriceCents(crop)));
        _wallet.Credit(revenueCents);
        _inventory.TakeAllRaw(crop);
        return new SaleResult(sold, revenueCents);
    }

    private bool AdvanceDay()
    {
        uint previousDays = _calendar.Snapshot.ElapsedDays;
        if (!_calendar.TryAdvanceSeconds(1))
            throw new InvalidOperationException("模拟时间已达到上限");
        if (_calendar.Snapshot.ElapsedDays == previousDays)
            return false;
        int previousPrice = CurrentFlourPriceCents;
        CurrentFlourPriceCents = _marketPriceCurve.GetPriceCents(CurrentDay);
        DailyPriceChangePercent =
            (CurrentFlourPriceCents - previousPrice) * 100.0 / previousPrice;
        return true;
    }

    public void SetPaused(bool paused) => _calendar.SetPaused(paused);

    private static string? PlacementError(PlacementResult result) =>
        result.Success ? null : PlacementRules.ErrorMessage(result.Failure);

    private void PlaceInitial(Vector2I cell, BuildingKind building, CropKind crop)
    {
        PlacementCheck check = PlacementRules.Check(cell, building, crop, _occupancy,
            _wallet.BalanceCents, 0);
        if (!check.Allowed)
            throw new InvalidOperationException("开局建筑放置无效");
        int index = IndexOf(cell);
        if (building == BuildingKind.Farm)
            PlaceFarm(index, crop);
        else
            PlaceProcessor(index, crop);
    }

    private void StartIdleProcessors()
    {
        for (int i = 0; i < CellCount; i++)
            if (_occupancy.Get(i) == BuildingKind.Processor)
                _processing.TryStart(i, _inventory);
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

    private static int IndexOf(Vector2I cell)
    {
        if (!MapCoordinates.ContainsCell(cell))
            throw new ArgumentOutOfRangeException(nameof(cell));
        return cell.Y * MapSize + cell.X;
    }
}
