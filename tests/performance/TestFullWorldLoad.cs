using System.Diagnostics;
using Godot;
using FarmExchange.Gameplay;

public partial class TestFullWorldLoad : Node
{
    public override void _Ready()
    {
        bool passed = RunChecks();
        GetTree().Quit(passed ? 0 : 1);
    }

    public static bool RunChecks()
    {
        var game = new FarmGame(12345);
        game.FillWorldForBenchmark();
        int farms = 0;
        int processors = 0;
        int[] farmCropCounts = new int[FarmGame.Crops.Count];
        int[] processorCropCounts = new int[FarmGame.Crops.Count];
        int occupiedCells = 0;
        foreach (var space in game.GetBuildingSpaces())
        {
            PlotSnapshot plot = game.GetPlot(space.AnchorCell);
            if (plot.Building == BuildingKind.None || plot.RemainingSeconds <= 0)
                return Fail("满地图负载场景存在非活动实体");
            foreach (Vector2I offset in space.Footprint.Offsets)
            {
                Vector2I cell = space.AnchorCell + offset;
                if (game.GetPlot(cell) != plot || game.GetBuildingSpace(cell) != space)
                    return Fail("满地图负载场景子格没有关联同一生产实例");
                occupiedCells++;
            }
            if (plot.Building == BuildingKind.Farm)
            {
                farms++;
                farmCropCounts[(int)plot.CropKind]++;
            }
            else
            {
                processors++;
                processorCropCounts[(int)plot.CropKind]++;
            }
        }
        if (occupiedCells != 147456 || game.GetBuildingSpaces().Count != 16384)
            return Fail("满地图负载测试未填满147456占用子格和16384生产实例");
        if (farms != 8192 || processors != 8192)
            return Fail("满地图负载测试的农田与加工场地数量错误");
        for (int crop = 0; crop < FarmGame.Crops.Count; crop++)
            if (farmCropCounts[crop] == 0 || processorCropCounts[crop] == 0)
                return Fail("满地图负载测试没有同时覆盖七种农田和加工场地");
        var watch = Stopwatch.StartNew();
        int distantFarmSeconds = game.GetPlot(new Vector2I(0, 0)).RemainingSeconds;
        int distantProcessorSeconds = game.GetPlot(new Vector2I(383, 383)).RemainingSeconds;
        game.AdvanceTick();
        if (game.GetPlot(new Vector2I(0, 0)).RemainingSeconds != distantFarmSeconds - 1 ||
            game.GetPlot(new Vector2I(383, 383)).RemainingSeconds != distantProcessorSeconds - 1)
            return Fail("地图角落的实体没有在 tick 中继续生产或加工");
        for (int tick = 1; tick < 50; tick++)
            game.AdvanceTick();
        watch.Stop();
        if (game.Calendar.ElapsedSeconds != 50 || game.CurrentDay != 1 ||
            game.GetProductStock(CropKind.Radish) == 0)
            return Fail("满地图负载测试没有持续推进生产和日期");
        GD.Print($"满地图负载测试：16,384 实例、147,456 占用子格，50 tick 耗时 {watch.Elapsed.TotalMilliseconds:F2} ms，平均 {watch.Elapsed.TotalMilliseconds / 50:F3} ms/tick");
        return true;
    }

    private static bool Fail(string message)
    {
        GD.PushError(message);
        return false;
    }
}
