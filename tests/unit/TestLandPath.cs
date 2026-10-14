using System;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Terrain;

public partial class TestLandPath : Node
{
    public override void _Ready() => GetTree().Quit(RunChecks() ? 0 : 1);
    public static bool RunChecks()
    {
        using TerrainMap map = TerrainMap.Create(110, FarmGame.ParseTerrainRules(TestTerrainMap.AllLandJson(), "路径全陆地配置"));
        LandPosition start = LandPosition.FromCoordinates(10.25, 10.5), target = LandPosition.FromCoordinates(13.25, 10.5);
        LandPathResult result = map.FindLandPath(start, target);
        if (result.Status != LandPathStatus.Success || result.Path is not LandPath path || path.TotalLength != 3 || path.GetTravelTicks(3) != 1 || path.GetPositionAtDistance(3) != target || path.GetPositionAtDistance(99) != target)
            return Fail("完整路线长度、整趟计时或末端截断错误。");
        LandPosition beyond = LandPosition.FromCoordinates(Math.BitIncrement(13.25), 10.5);
        if (map.FindLandPath(start, beyond).Path!.GetTravelTicks(3) != 2) return Fail("三格倍数之上的真实距离不得用epsilon压成一秒。");
        LandPosition position = start;
        for (int i = 0; i < 100; i++)
        {
            LandPosition next = LandPosition.FromCoordinates(i % 2 == 0 ? 90.3 : 12.7, i % 3 == 0 ? 88.4 : 17.2);
            LandPath leg = map.FindLandPath(position, next).Path!;
            position = leg.GetPositionAtDistance(3);
            if (!double.IsFinite(position.X) || !double.IsFinite(position.Y)) return Fail("连续取消接续出现展示Inf/NaN。");
            LandPath repeat = map.FindLandPath(position, next).Path!;
            if (repeat.GetPositionAtDistance(0) != position || !repeat.Equals(map.FindLandPath(position, next).Path)) return Fail("接续重建改变真实起点或路径值不稳定。");
        }
        LandPath zero = map.FindLandPath(position, position).Path!;
        if (zero.GetTravelTicks(3) != 0 || zero.GetDirectionAtDistance(0) != Vector2.Zero || zero.GetPositionAtDistance(1) != position) return Fail("零长度路线行为错误。");
        if (map.FindLandPath(LandPosition.FromCoordinates(-.375, 10), target).Status != LandPathStatus.Success || map.FindLandPath(LandPosition.FromCoordinates(Math.BitDecrement(-.375), 10), target).Status != LandPathStatus.InvalidStart)
            return Fail("地图边缘圆净空判定错误。");
        GD.Print("[通过] 连续路线距离、取消接续、数值增长和边缘净空"); return true;
    }
    private static bool Fail(string reason) { GD.PushError($"[失败] 连续路线：{reason}"); return false; }
}
