using System;
using System.Linq;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
using FarmExchange.Time;
using FarmExchange.UI;
using FarmExchange.Weather;
using FarmExchange.World;

public partial class TestWeatherPresentation : Node
{
    public override void _Ready()
    {
        GetWindow().Size = new Vector2I(1920, 1080);
        GetTree().Quit(RunChecks(this) ? 0 : 1);
    }

    public static bool RunChecks(Node parent)
    {
        using var game = new FarmGame(34, weatherMode: WeatherMode.Explicit, terrainRules: TerrainTestFixtures.AllLandRules);
        var scope = new Node2D();
        parent.AddChild(scope);
        var camera = new Camera2D { Position = MapCoordinates.GridPositionToLocal(new Vector2(191.5f, 191.5f)) };
        scope.AddChild(camera);
        camera.MakeCurrent();
        camera.ForceUpdateScroll();
        var driver = new SimulationDriver();
        var map = new WorldMap();
        scope.AddChild(map);
        map.SetGame(game, driver);
        var details = new FarmDetailsPanel();
        scope.AddChild(details);
        try
        {
            map._Process(.1);
            WeatherPresentation weather = map.GetNode<WeatherPresentation>("Weather");
            if (weather.VisibleRegionCount != 0 || weather.RainSamples.Count != 0)
                return Fail("默认晴天仍存在云影或雨点");
            Rect2I rain = new(180, 180, 12, 24);
            Rect2I cloudy = new(192, 180, 12, 24);
            if (!game.TrySetWeatherRegion(rain, WeatherKind.Rain, out _) ||
                !game.TrySetWeatherRegion(cloudy, WeatherKind.Cloudy, out _))
                return Fail("局部云雨测试区域设置失败");
            uint elapsed = game.Calendar.ElapsedSeconds;
            int stock = game.GetRawStock(CropKind.Radish);
            map._Process(.2);
            if (weather.VisibleRegionCount != 2 || weather.RainSamples.Count == 0 || weather.RainSamples.Count > 180)
                return Fail("可见局部云雨或雨点密度不符合真实状态");
            foreach (var drop in weather.RainSamples)
                if (!rain.HasPoint(MapCoordinates.LocalPositionToCell(drop.Landing)))
                    return Fail("真实雨点落点超出雨区");
            double time = weather.AnimationSeconds;
            Vector2 point = weather.RainSamples[0].Landing;
            float phase = weather.RainSamples[0].Phase;
            game.SetPaused(true);
            map._Process(10);
            if (weather.AnimationSeconds != time || weather.RainSamples[0] != (point, phase))
                return Fail("暂停没有冻结雨线及落地动画");
            game.SetPaused(false);
            foreach (double rate in new[] { .5, 1, 2, 16 })
            {
                driver.SetDevelopmentRate(rate);
                double before = weather.AnimationSeconds;
                map._Process(.25);
                double expected = .25 * (SimulationDriver.IsPublicRateAllowed(rate) ? rate : 1);
                if (Math.Abs(weather.AnimationSeconds - before - expected) > .00001 || weather.RainSamples.Count > 180)
                    return Fail("公共倍率或高开发倍率雨点控制不一致");
            }
            if (game.Calendar.ElapsedSeconds != elapsed || game.GetRawStock(CropKind.Radish) != stock)
                return Fail("天气绘制推进了经营或修改库存");
            if (!CheckProcessorMotion(game, map)) return false;
            Vector2I farm = new(189, 189);
            details.Refresh(game.GetFarmDetails(farm));
            if (details.GetNode<Label>("FarmCurrentWeather").Text != "当前天气：降雨" ||
                details.FindChild("FarmHarvestQuantity", true, false) is not Label quantity ||
                quantity.Text != game.GetFarmDetails(farm).Crop.HarvestQuantity.ToString())
                return Fail("详情没有读取整田天气或基准产量");
            camera.Position = MapCoordinates.CellToLocalCenter(new Vector2I(0, 0));
            camera.ForceUpdateScroll();
            map._Process(.2);
            if (weather.VisibleRegionCount != 0 || weather.RainSamples.Count != 0)
                return Fail("天气仍绘制离屏区域或保留离屏雨点");
            if (!game.TryClearWeatherRegion(rain, out _) || !game.TryClearWeatherRegion(cloudy, out _))
                return Fail("天气清除夹具失败");
            details.Refresh(game.GetFarmDetails(farm));
            if (details.GetNode<Label>("FarmCurrentWeather").Text != "当前天气：晴天")
                return Fail("天气清除后详情仍显示旧雨");
            GD.Print("局部天气表现、真实落点、暂停、倍率、离屏与详情检查通过");
            return true;
        }
        finally { scope.Free(); }
    }

    private static bool CheckProcessorMotion(FarmGame game, WorldMap map)
    {
        Vector2I processor = new(189, 192);
        if (game.RemoveBuilding(processor) != null ||
            !game.TryPlace(processor, BuildingKind.Processor, CropKind.Wheat).Success)
            return Fail("真实磨坊启停夹具创建失败");
        map.SyncFromGame();
        FacilityMotion motion = map.GetNode<Node2D>("DepthSorted").GetChildren().OfType<FacilityMotion>()
            .Single(node => node.Position == MapCoordinates.CellToLocalCenter(processor + Vector2I.One));
        Sprite2D sails = motion.GetNode<Sprite2D>("Art/Sails");
        int idleFrame = sails.Frame;
        if (game.GetProcessorDetails(processor).Status != ProcessorStatus.WaitingForRaw ||
            game.GetPlot(processor).RemainingSeconds != 0)
            return Fail("待料磨坊没有来自同一实际快照的零剩余时间");
        map._Process(.5);
        if (sails.Frame != idleFrame) return Fail("待料磨坊仍在旋转");
        if (!game.Buy(new CommodityId(CropKind.Wheat, CommodityKind.Raw), 1).Success)
            return Fail("真实磨坊原料买入失败");
        game.AdvanceTick();
        map.SyncFromGame();
        if (game.GetProcessorDetails(processor).Status != ProcessorStatus.Processing ||
            game.GetPlot(processor).RemainingSeconds <= 0)
            return Fail("真实加工中与公开剩余时间判定不一致");
        map._Process(.5);
        if (sails.Frame == idleFrame) return Fail("真实加工没有启动磨坊旋转");
        int remaining = game.GetPlot(processor).RemainingSeconds;
        game.AdvanceTicks((uint)remaining);
        map.SyncFromGame();
        int endedFrame = sails.Frame;
        map._Process(.5);
        if (game.GetProcessorDetails(processor).Status != ProcessorStatus.WaitingForRaw ||
            game.GetPlot(processor).RemainingSeconds != 0 || sails.Frame != endedFrame)
            return Fail("实际完工待料没有停止磨坊旋转");
        return true;
    }

    private static bool Fail(string message) { GD.PushError(message); return false; }
}
