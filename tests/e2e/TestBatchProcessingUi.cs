using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
using FarmExchange.Land;
using FarmExchange.UI;
using FarmExchange.Weather;
using FarmExchange.World;

public partial class TestBatchProcessingUi : Node
{
    public override async void _Ready()
    {
        try
        {
            bool passed = await RunChecksAsync(this);
            if (passed) GD.Print("批量加工真实详情、完整周期、暂停及1080P可读性检查通过");
            GetTree().Quit(passed ? 0 : 1);
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    public static async Task<bool> RunChecksAsync(Node parent)
    {
        Vector2I originalSize = parent.GetWindow().Size;
        int? originalSeed = Main.InitialSeed;
        WeatherMode originalWeather = Main.InitialWeatherMode;
        Main? main = null;
        try
        {
            parent.GetWindow().Size = new Vector2I(1920, 1080);
            Main.InitialSeed = 35;
            Main.InitialWeatherMode = WeatherMode.Explicit;
            await Frames(parent);
            main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
            parent.AddChild(main);
            main.SetProcess(false);
            FarmGame game = main.Game;
            foreach (BuildingSpaceSnapshot space in game.GetBuildingSpaces().ToArray())
                if (game.RemoveBuilding(space.AnchorCell) != null) return Fail("清理中心设施失败");
            Vector2I cell = new(189, 192);
            if (!game.TryPlace(cell, BuildingKind.Processor, CropKind.Radish).Success)
                return Fail("真实加工场地建造失败");

            bool passed = await ShowAndCheck(parent, main, cell, 0, "待原料", "waiting-raw-1920.png");
            var raw = new CommodityId(CropKind.Radish, CommodityKind.Raw);
            if (!game.Buy(raw, 9).Success || main.AdvanceSimulation(1) != 1)
                return Fail("满批原料买入或真实领取失败");
            int batchSeconds = game.GetPlot(cell).RemainingSeconds;
            passed = await ShowAndCheck(parent, main, cell, 9, "加工中", "full-batch-1920.png") && passed;
            string fullRelation = Find<Label>(main, "ProcessorRelation").Text;
            string fullProgress = Find<Label>(main, "ProcessorActualProgress").Text;
            game.SetPaused(true);
            if (!game.Buy(raw, 4).Success || main.AdvanceSimulation(10) != 0 ||
                game.GetProcessorDetails(cell).BatchQuantity != 9 || game.GetRawStock(CropKind.Radish) != 4 ||
                game.GetPlot(cell).RemainingSeconds != batchSeconds ||
                Find<Label>(main, "ProcessorRelation").Text != fullRelation ||
                Find<Label>(main, "ProcessorActualProgress").Text != fullProgress)
                return Fail("暂停或中途买入改变已锁定满批、库存或进度");
            if (game.SetRawReserve(CropKind.Radish, 4) != RawReserveFailure.None)
                return Fail("底线夹具设置失败");
            game.SetPaused(false);
            if (main.AdvanceSimulation(batchSeconds) != (uint)batchSeconds ||
                game.GetProductStock(CropKind.Radish) != 9 || game.GetRawStock(CropKind.Radish) != 4)
                return Fail("满批完成没有一次入库9份并保留公共原料");
            passed = await ShowAndCheck(parent, main, cell, 0, "底线保留", "waiting-reserve-1920.png") && passed;

            game.SetRawReserve(CropKind.Radish, 0);
            passed = await ShowAndCheck(parent, main, cell, 0, "待领取", "ready-to-process-1920.png") && passed;
            if (main.AdvanceSimulation(1) != 1 || game.GetPlot(cell).RemainingSeconds != batchSeconds)
                return Fail("少量批没有使用满批的完整加工时长");
            passed = await ShowAndCheck(parent, main, cell, 4, "加工中", "small-batch-1920.png") && passed;
            if (main.AdvanceSimulation(batchSeconds - 1) != (uint)(batchSeconds - 1) ||
                game.GetProductStock(CropKind.Radish) != 9 || game.GetProcessorDetails(cell).BatchQuantity != 4)
                return Fail("少量批提前完工或在完成前丢失锁定数量");
            if (main.AdvanceSimulation(1) != 1 || game.GetProductStock(CropKind.Radish) != 13 ||
                game.GetRawStock(CropKind.Radish) != 0)
                return Fail("少量批完成没有按真实4份入库");
            passed = await ShowAndCheck(parent, main, cell, 0, "待原料", "completed-1920.png") && passed;
            if (!game.Buy(raw, 4).Success || main.AdvanceSimulation(1) != 1)
                return Fail("拆除损失夹具启动失败");
            passed = await ShowAndCheck(parent, main, cell, 4, "加工中", "before-removal-1920.png") && passed;
            Find<Button>(Find<ProcessorDetailsPanel>(main, "ProcessorDetailsPanel"), "RemoveButton")
                .EmitSignal(Button.SignalName.Pressed);
            if (game.GetPlot(cell).Building != BuildingKind.None || game.GetRawStock(CropKind.Radish) != 0 ||
                game.GetProductStock(CropKind.Radish) != 13)
                return Fail("真实移除按钮没有丢弃整个未完成批次");
            return passed;
        }
        finally
        {
            main?.QueueFree();
            Main.InitialSeed = originalSeed;
            Main.InitialWeatherMode = originalWeather;
            parent.GetWindow().Size = originalSize;
            await Frames(parent);
        }
    }

    private static async Task<bool> ShowAndCheck(Node parent, Main main, Vector2I cell,
        int quantity, string status, string screenshot)
    {
        WorldMap map = main.GetNode<WorldMap>("WorldMap");
        map.SyncFromGame();
        map.EmitSignal(WorldMap.SignalName.SelectionChanged, cell);
        await Frames(parent);
        ProcessorDetailsSnapshot details = main.Game.GetProcessorDetails(cell);
        ProcessorDetailsPanel panel = Find<ProcessorDetailsPanel>(main, "ProcessorDetailsPanel");
        string expectedRelation = quantity > 0 ?
            $"本批投入 {quantity} 份{details.Crop.CropName} → {quantity} 份{details.Crop.ProductName}" :
            $"1 份{details.Crop.CropName} → 1 份{details.Crop.ProductName}";
        if (details.BatchQuantity != quantity || Find<Label>(panel, "ProcessorRelation").Text != expectedRelation ||
            Find<Label>(panel, "ProcessorCurrentStatus").Text != status ||
            Find<Label>(panel, "ProcessorProductStock").Text != main.Game.GetProductStock(CropKind.Radish).ToString() ||
            Find<ProgressBar>(panel, "ProcessorProgress").Visible != (quantity > 0))
            return Fail("详情没有显示真实批量、状态、成品份数或活动进度：" + screenshot);
        string rules = Find<Label>(panel, "ProcessorBatchRules").Text;
        if (!rules.Contains("每批最多 9 份，投入与产出 1:1") ||
            !rules.Contains("每批加工 0.5 天，少量批次同样耗时") ||
            (quantity > 0 && !Find<Label>(panel, "ProcessorRemovalLoss").Text.Contains($"本批已投入的 {quantity} 份原料")))
            return Fail("单批规则或整批移除损失说明不完整");
        ScrollContainer scroll = Find<ScrollContainer>(Find<DraggableWindow>(main, "DetailWindow"), "WindowScroll");
        foreach (string name in new[] { "ProcessorBuildingTitle", "ProcessorCurrentStatus", "ProcessorActualProgress",
            "ProcessorRelation", "ProcessorBatchRules", "ProcessorProductStock", "ProcessorProductPrice",
            "ProcessorNextStep", "ProcessorRemovalLoss" })
        {
            Label label = Find<Label>(panel, name);
            if (!label.IsVisibleInTree() || !scroll.GetGlobalRect().Encloses(label.GetGlobalRect()) ||
                label.GetVisibleLineCount() != label.GetLineCount() || label.GetLineCount() > 3)
                return Fail("1080P加工文字被裁切、隐藏或逐字竖排：" + name);
        }
        if (!scroll.GetGlobalRect().Encloses(Find<Button>(panel, "RemoveButton").GetGlobalRect()))
            return Fail("1080P移除操作没有完整显示");
        if (DisplayServer.GetName() == "headless") return true;
        await parent.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string directory = ProjectSettings.GlobalizePath("res://build/issue35-ui-validation");
        if (DirAccess.MakeDirRecursiveAbsolute(directory) != Error.Ok ||
            parent.GetViewport().GetTexture().GetImage().SavePng(directory + "/" + screenshot) != Error.Ok)
            return Fail("1080P截图保存失败：" + screenshot);
        return true;
    }

    private static async Task Frames(Node parent)
    {
        await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static T Find<T>(Node node, string name) where T : Node =>
        node.FindChild(name, true, false) as T ?? throw new InvalidOperationException(name);
    private static bool Fail(string message) { GD.PushError("批量加工详情：" + message); return false; }
}
