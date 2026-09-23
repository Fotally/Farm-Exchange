using System;
using System.Collections.Generic;
using Godot;

public enum CropStage { None, Seeded, Growing }
public enum BuildingKind { None, Farm, Processor }
public enum CropKind { Wheat, Corn, Rice, Potato, Sunflower, Sugarcane }

public readonly record struct CropDefinition(
    CropKind Kind, string CropName, string BuildingName, string ProductName,
    int GrowthTicks, int ProcessingTicks, int PricePercent);
public readonly record struct PlotSnapshot(
    bool IsUnlocked, BuildingKind Building, CropKind CropKind, CropStage Crop, int RemainingTicks);
public readonly record struct TickResult(int Harvested, int Produced, bool WorkerActed, bool DayAdvanced);
public readonly record struct SaleResult(int Quantity, int RevenueCents);

public sealed class FarmGame
{
    public const int MapSize = 128;
    public const int TicksPerDay = 10;
    public const int LandCostCents = 1000;

    private static readonly CropDefinition[] CropDefinitions =
    {
        new(CropKind.Wheat, "小麦", "磨坊", "面粉", 5, 3, 100),
        new(CropKind.Corn, "玉米", "玉米加工坊", "玉米粉", 6, 4, 100),
        new(CropKind.Rice, "水稻", "碾米坊", "大米", 7, 4, 120),
        new(CropKind.Potato, "马铃薯", "淀粉坊", "淀粉", 6, 4, 80),
        new(CropKind.Sunflower, "向日葵", "榨油坊", "葵花籽油", 9, 5, 160),
        new(CropKind.Sugarcane, "甘蔗", "制糖坊", "蔗糖", 10, 6, 200),
    };
    public static IReadOnlyList<CropDefinition> Crops { get; } = Array.AsReadOnly(CropDefinitions);
    private static readonly Vector2I[] InitialFarmCells =
    {
        new(63, 63), new(64, 63), new(65, 63),
    };
    private static readonly Vector2I[] InitialProcessorCells =
    {
        new(63, 64), new(64, 64),
    };

    private readonly PlotState[] _plots = new PlotState[MapSize * MapSize];
    private readonly int[] _rawStock = new int[CropDefinitions.Length];
    private readonly int[] _productStock = new int[CropDefinitions.Length];
    private readonly MarketPriceCurve _marketPriceCurve;
    private int _nextWorkerPlotIndex;
    private int _ticksIntoDay;

    public int MoneyCents { get; private set; } = 5000;
    public int FreeLandGrants { get; private set; } = 2;
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
        CropKind primary = (CropKind)random.Next(CropDefinitions.Length);
        bool split = random.Next(2) == 1;
        CropKind secondary = primary;
        if (split)
        {
            int secondaryIndex = random.Next(CropDefinitions.Length - 1);
            if (secondaryIndex >= (int)primary)
                secondaryIndex++;
            secondary = (CropKind)secondaryIndex;
        }

        for (int i = 0; i < InitialFarmCells.Length; i++)
        {
            ref PlotState plot = ref _plots[IndexOf(InitialFarmCells[i])];
            plot.IsUnlocked = true;
            plot.Building = BuildingKind.Farm;
            plot.CropKind = i == 2 ? secondary : primary;
        }
        for (int i = 0; i < InitialProcessorCells.Length; i++)
        {
            ref PlotState plot = ref _plots[IndexOf(InitialProcessorCells[i])];
            plot.IsUnlocked = true;
            plot.Building = BuildingKind.Processor;
            plot.CropKind = i == 1 ? secondary : primary;
        }
    }

    public static CropDefinition GetCrop(CropKind crop) => CropDefinitions[(int)crop];
    public int GetRawStock(CropKind crop) => _rawStock[(int)crop];
    public int GetProductStock(CropKind crop) => _productStock[(int)crop];
    public int GetProductPriceCents(CropKind crop) =>
        (CurrentFlourPriceCents * GetCrop(crop).PricePercent + 50) / 100;

    public PlotSnapshot GetPlot(Vector2I cell)
    {
        PlotState plot = _plots[IndexOf(cell)];
        return new PlotSnapshot(plot.IsUnlocked, plot.Building, plot.CropKind, plot.Crop, plot.RemainingTicks);
    }

    public string? UnlockLand(Vector2I cell)
    {
        ref PlotState plot = ref _plots[IndexOf(cell)];
        if (plot.IsUnlocked)
            return "该土地已解锁";
        if (FreeLandGrants == 0 && MoneyCents < LandCostCents)
            return "金币不足，无法解锁土地";
        if (FreeLandGrants > 0)
            FreeLandGrants--;
        else
            MoneyCents -= LandCostCents;
        plot.IsUnlocked = true;
        return null;
    }

    public string? BuildFarm(Vector2I cell) => Build(cell, BuildingKind.Farm, CropKind.Wheat);

    public string? BuildProcessor(Vector2I cell, CropKind crop)
    {
        string? error = Build(cell, BuildingKind.Processor, crop);
        if (error == null)
            StartIdleProcessors();
        return error;
    }

    public string? SetFarmCrop(Vector2I cell, CropKind crop)
    {
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
                    _rawStock[(int)plot.CropKind]++;
                    harvested++;
                }
            }
            else if (plot.Building == BuildingKind.Processor && plot.RemainingTicks > 0)
            {
                plot.RemainingTicks--;
                if (plot.RemainingTicks == 0)
                {
                    _productStock[(int)plot.CropKind]++;
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
        int sold = 0;
        int revenue = 0;
        foreach (CropDefinition crop in Crops)
        {
            int index = (int)crop.Kind;
            sold += _productStock[index];
            revenue += _productStock[index] * GetProductPriceCents(crop.Kind);
            _productStock[index] = 0;
        }
        MoneyCents += revenue;
        return new SaleResult(sold, revenue);
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
        if (!plot.IsUnlocked)
            return "请先解锁土地";
        if (plot.Building != BuildingKind.None)
            return "该土地已有建筑";
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
            int index = (int)plot.CropKind;
            if (_rawStock[index] == 0)
                continue;
            _rawStock[index]--;
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
        public bool IsUnlocked;
        public BuildingKind Building;
        public CropKind CropKind;
        public CropStage Crop;
        public int RemainingTicks;
    }
}
