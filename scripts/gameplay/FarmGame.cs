using System;
using System.Collections.Generic;
using Godot;
using FarmExchange.Economy;
using FarmExchange.Farming;
using FarmExchange.Market;
using GoodsInventory = FarmExchange.Inventory.Inventory;

namespace FarmExchange.Gameplay;

public enum CropStage { None, Seeded, Growing }
public enum BuildingKind { None, Farm, Processor }
public enum CropKind { Wheat, Corn, Rice, Potato, Sunflower, Sugarcane }

public readonly record struct CropDefinition(
    CropKind Kind, string CropName, string BuildingName, string ProductName,
    int GrowthTicks, int ProcessingTicks, int PricePercent, int RawPricePercent);
public readonly record struct PlotSnapshot(
    BuildingKind Building, CropKind CropKind, CropStage Crop, int RemainingTicks);
public readonly record struct TickResult(int Harvested, int Produced, bool WorkerActed, bool DayAdvanced);
public readonly record struct SaleResult(int Quantity, int RevenueCents);

public sealed class FarmGame
{
    public const int MapSize = 128;
    public const int TicksPerDay = 10;
    public const int BuildingCostCents = 1000;

    public static IReadOnlyList<CropDefinition> Crops => CropCatalog.Crops;
    private static readonly Vector2I[] InitialFarmCells =
    {
        new(63, 63), new(64, 63), new(65, 63),
    };
    private static readonly Vector2I[] InitialProcessorCells =
    {
        new(63, 64), new(64, 64),
    };

    private readonly PlotState[] _plots = new PlotState[MapSize * MapSize];
    private readonly GoodsInventory _inventory = new();
    private readonly Wallet _wallet = new(5000);
    private readonly MarketPriceCurve _marketPriceCurve;
    private int _nextWorkerPlotIndex;
    private int _ticksIntoDay;

    public int MoneyCents => _wallet.BalanceCents;
    public int CurrentDay { get; private set; } = 1;
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
        {
            ref PlotState plot = ref _plots[IndexOf(InitialFarmCells[i])];
            plot.Building = BuildingKind.Farm;
            plot.CropKind = i == 2 ? secondary : primary;
        }
        for (int i = 0; i < InitialProcessorCells.Length; i++)
        {
            ref PlotState plot = ref _plots[IndexOf(InitialProcessorCells[i])];
            plot.Building = BuildingKind.Processor;
            plot.CropKind = i == 1 ? secondary : primary;
        }
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
        PlotState plot = _plots[IndexOf(cell)];
        return new PlotSnapshot(plot.Building, plot.CropKind, plot.Crop, plot.RemainingTicks);
    }

    internal void FillWorldForBenchmark()
    {
        for (int row = 0; row < MapSize; row++)
        {
            for (int col = 0; col < MapSize; col++)
            {
                ref PlotState plot = ref _plots[row * MapSize + col];
                CropKind crop = (CropKind)((row * (MapSize / 2) + col / 2) % Crops.Count);
                bool farm = col % 2 == 0;
                plot.Building = farm ? BuildingKind.Farm : BuildingKind.Processor;
                plot.CropKind = crop;
                plot.Crop = farm ? CropStage.Growing : CropStage.None;
                plot.RemainingTicks = farm
                    ? GetCrop(crop).GrowthTicks
                    : GetCrop(crop).ProcessingTicks;
            }
        }
    }

    public string? BuildFarm(Vector2I cell) => Build(cell, BuildingKind.Farm, CropKind.Wheat);

    public string? BuildProcessor(Vector2I cell, CropKind crop)
    {
        if (!CropCatalog.IsDefined(crop))
            return "无效作物";
        string? error = Build(cell, BuildingKind.Processor, crop);
        if (error == null)
            StartIdleProcessors();
        return error;
    }

    public string? SetFarmCrop(Vector2I cell, CropKind crop)
    {
        if (!CropCatalog.IsDefined(crop))
            return "无效作物";
        ref PlotState plot = ref _plots[IndexOf(cell)];
        if (plot.Building != BuildingKind.Farm)
            return "该土地没有农田";
        if (plot.CropKind == crop)
            return null;
        plot.CropKind = crop;
        plot.Crop = CropStage.None;
        plot.RemainingTicks = 0;
        return null;
    }

    public string? RemoveBuilding(Vector2I cell)
    {
        ref PlotState plot = ref _plots[IndexOf(cell)];
        if (plot.Building == BuildingKind.None)
            return "该土地没有建筑";
        plot.Building = BuildingKind.None;
        plot.Crop = CropStage.None;
        plot.RemainingTicks = 0;
        return null;
    }

    public TickResult AdvanceTick()
    {
        int harvested = 0;
        int produced = 0;
        for (int i = 0; i < _plots.Length; i++)
        {
            ref PlotState plot = ref _plots[i];
            if (plot.Building == BuildingKind.Farm && plot.Crop == CropStage.Growing)
            {
                plot.RemainingTicks--;
                if (plot.RemainingTicks == 0)
                {
                    plot.Crop = CropStage.None;
                    _inventory.AddRaw(plot.CropKind, 1);
                    harvested++;
                }
            }
            else if (plot.Building == BuildingKind.Processor && plot.RemainingTicks > 0)
            {
                plot.RemainingTicks--;
                if (plot.RemainingTicks == 0)
                {
                    _inventory.AddProduct(plot.CropKind, 1);
                    produced++;
                }
            }
        }
        StartIdleProcessors();
        bool workerActed = WorkOneFarm();
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
        _ticksIntoDay++;
        if (_ticksIntoDay < TicksPerDay)
            return false;
        _ticksIntoDay = 0;
        int previousPrice = CurrentFlourPriceCents;
        CurrentDay++;
        CurrentFlourPriceCents = _marketPriceCurve.GetPriceCents(CurrentDay);
        DailyPriceChangePercent =
            (CurrentFlourPriceCents - previousPrice) * 100.0 / previousPrice;
        return true;
    }

    private string? Build(Vector2I cell, BuildingKind building, CropKind crop)
    {
        ref PlotState plot = ref _plots[IndexOf(cell)];
        if (plot.Building != BuildingKind.None)
            return "该土地已有建筑";
        if (!_wallet.TrySpend(BuildingCostCents))
            return "金币不足，无法建造建筑";
        plot.Building = building;
        plot.CropKind = crop;
        return null;
    }

    private void StartIdleProcessors()
    {
        for (int i = 0; i < _plots.Length; i++)
        {
            ref PlotState plot = ref _plots[i];
            if (plot.Building != BuildingKind.Processor || plot.RemainingTicks != 0)
                continue;
            if (!_inventory.TryTakeRawForProcessing(plot.CropKind))
                continue;
            plot.RemainingTicks = GetCrop(plot.CropKind).ProcessingTicks;
        }
    }

    private bool WorkOneFarm()
    {
        for (int offset = 0; offset < _plots.Length; offset++)
        {
            int index = (_nextWorkerPlotIndex + offset) % _plots.Length;
            ref PlotState plot = ref _plots[index];
            if (plot.Building != BuildingKind.Farm || plot.Crop == CropStage.Growing)
                continue;
            if (plot.Crop == CropStage.None)
                plot.Crop = CropStage.Seeded;
            else
            {
                plot.Crop = CropStage.Growing;
                plot.RemainingTicks = GetCrop(plot.CropKind).GrowthTicks;
            }
            _nextWorkerPlotIndex = (index + 1) % _plots.Length;
            return true;
        }
        return false;
    }

    private static int IndexOf(Vector2I cell)
    {
        if (cell.X < 0 || cell.X >= MapSize || cell.Y < 0 || cell.Y >= MapSize)
            throw new ArgumentOutOfRangeException(nameof(cell));
        return cell.Y * MapSize + cell.X;
    }

    private struct PlotState
    {
        public BuildingKind Building;
        public CropKind CropKind;
        public CropStage Crop;
        public int RemainingTicks;
    }
}
