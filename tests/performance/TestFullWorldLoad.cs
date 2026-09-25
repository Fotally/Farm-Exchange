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
        for (int row = 0; row < FarmGame.MapSize; row++)
        {
            for (int col = 0; col < FarmGame.MapSize; col++)
            {
                PlotSnapshot plot = game.GetPlot(new Vector2I(col, row));
                if (!plot.IsUnlocked || plot.Building == BuildingKind.None || plot.RemainingTicks <= 0)
                    return Fail("满地图负载场景存在空地、锁定土地或非活动实体");
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
        }
        if (farms != 8192 || processors != 8192)
            return Fail("满地图负载测试的农田与加工场地数量错误");
        for (int crop = 0; crop < FarmGame.Crops.Count; crop++)
            if (farmCropCounts[crop] == 0 || processorCropCounts[crop] == 0)
                return Fail("满地图负载测试没有同时覆盖六种农田和加工场地");
        var watch = Stopwatch.StartNew();
        int distantFarmTicks = game.GetPlot(new Vector2I(0, 0)).RemainingTicks;
        int distantProcessorTicks = game.GetPlot(new Vector2I(127, 127)).RemainingTicks;
        game.AdvanceTick();
        if (game.GetPlot(new Vector2I(0, 0)).RemainingTicks != distantFarmTicks - 1 ||
            game.GetPlot(new Vector2I(127, 127)).RemainingTicks != distantProcessorTicks - 1)
            return Fail("地图角落的实体没有在 tick 中继续生产或加工");
        for (int tick = 1; tick < 50; tick++)
            game.AdvanceTick();
        watch.Stop();
        if (game.CurrentDay != 6 || game.GetProductStock(CropKind.Wheat) == 0)
            return Fail("满地图负载测试没有持续推进生产和日期");
        GD.Print($"满地图负载测试：50 tick 耗时 {watch.Elapsed.TotalMilliseconds:F2} ms，平均 {watch.Elapsed.TotalMilliseconds / 50:F3} ms/tick");
        return true;
    }

    private static bool Fail(string message)
    {
        GD.PushError(message);
        return false;
    }
}
