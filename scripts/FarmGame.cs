using System;
using Godot;

public enum CropStage
{
    None,
    Seeded,
    Growing,
}

public enum BuildingKind
{
    None,
    Farm,
    Mill,
}

public readonly record struct PlotSnapshot(bool IsUnlocked, BuildingKind Building, CropStage Crop, int RemainingTicks);
public readonly record struct TickResult(int WheatHarvested, int FlourProduced, bool WorkerActed);

public sealed class FarmGame
{
    public const int MapSize = 128;
    public const int GrowthTicks = 5;
    public const int MillingTicks = 10;
    public const int FlourSalePrice = 5;
    public const int LandCost = 10;

    private readonly PlotState[] _plots = new PlotState[MapSize * MapSize];
    private int _nextWorkerPlotIndex;

    public int Money { get; private set; }
    public int WheatStock { get; private set; }
    public int FlourStock { get; private set; }
    public int FreeLandGrants { get; private set; } = 2;

    public PlotSnapshot GetPlot(Vector2I cell)
    {
        PlotState plot = _plots[IndexOf(cell)];
        return new PlotSnapshot(plot.IsUnlocked, plot.Building, plot.Crop, plot.RemainingTicks);
    }

    public string? UnlockLand(Vector2I cell)
    {
        ref PlotState plot = ref _plots[IndexOf(cell)];
        if (plot.IsUnlocked)
            return "该土地已解锁";
        if (FreeLandGrants == 0 && Money < LandCost)
            return "金币不足，无法解锁土地";

        if (FreeLandGrants > 0)
            FreeLandGrants--;
        else
            Money -= LandCost;
        plot.IsUnlocked = true;
        return null;
    }

    public string? BuildFarm(Vector2I cell)
    {
        return Build(cell, BuildingKind.Farm);
    }

    public string? BuildMill(Vector2I cell)
    {
        string? error = Build(cell, BuildingKind.Mill);
        if (error == null)
            StartIdleMills();
        return error;
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
                    WheatStock++;
                    harvested++;
                }
            }
            else if (plot.Building == BuildingKind.Mill && plot.RemainingTicks > 0)
            {
                plot.RemainingTicks--;
                if (plot.RemainingTicks == 0)
                {
                    FlourStock++;
                    produced++;
                }
            }
        }
        StartIdleMills();
        bool workerActed = WorkOneFarm();
        return new TickResult(harvested, produced, workerActed);
    }

    public int SellAll()
    {
        int sold = FlourStock;
        Money += sold * FlourSalePrice;
        FlourStock = 0;
        return sold;
    }

    private string? Build(Vector2I cell, BuildingKind building)
    {
        ref PlotState plot = ref _plots[IndexOf(cell)];
        if (!plot.IsUnlocked)
            return "请先解锁土地";
        if (plot.Building != BuildingKind.None)
            return "该土地已有建筑";
        plot.Building = building;
        return null;
    }

    private void StartIdleMills()
    {
        for (int i = 0; i < _plots.Length && WheatStock > 0; i++)
        {
            ref PlotState plot = ref _plots[i];
            if (plot.Building != BuildingKind.Mill || plot.RemainingTicks != 0)
                continue;
            WheatStock--;
            plot.RemainingTicks = MillingTicks;
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
                plot.RemainingTicks = GrowthTicks;
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
        public CropStage Crop;
        public int RemainingTicks;
    }
}
