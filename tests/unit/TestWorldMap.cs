using Godot;
using FarmExchange.World;

public partial class TestWorldMap : Node
{
    public override void _Ready()
    {
        bool passed = RunChecks();
        if (passed)
            GD.Print("地图坐标检查通过");
        GetTree().Quit(passed ? 0 : 1);
    }

    public static bool RunChecks()
    {
        var map = new WorldMap();
        bool passed = Check(map);
        map.Free();
        return passed;
    }

    private static bool Check(WorldMap map)
    {
        Vector2I[] cells =
        {
            new(0, 0), new(127, 0), new(0, 127), new(127, 127), new(48, 79),
        };
        foreach (Vector2I cell in cells)
        {
            Vector2 center = new((cell.X - cell.Y) * 32f, (cell.X + cell.Y) * 16f);
            if (!MapCoordinates.ContainsCell(cell) ||
                MapCoordinates.CellToLocalCenter(cell) != center ||
                MapCoordinates.LocalPositionToCell(center) != cell)
            {
                GD.PushError($"格中心往返错误：{cell}");
                return false;
            }
        }

        if (MapCoordinates.GridPositionToLocal(new Vector2(63.5f, 62.25f)) != new Vector2(40f, 2012f) ||
            MapCoordinates.GridPositionToLocal(new Vector2(127, 127)) != new Vector2(0, 4064))
        {
            GD.PushError("分数格位置没有沿用统一等距换算");
            return false;
        }

        if (MapCoordinates.LocalPositionToCell(new Vector2(31f, 0f)) != new Vector2I(0, 0) ||
            MapCoordinates.LocalPositionToCell(new Vector2(33f, 0f)) != new Vector2I(1, -1) ||
            MapCoordinates.ContainsCell(new Vector2I(-1, 0)) ||
            MapCoordinates.ContainsCell(new Vector2I(128, 0)) ||
            MapCoordinates.ContainsCell(new Vector2I(0, 128)))
        {
            GD.PushError("格边界或地图外判断错误");
            return false;
        }

        if (map.LocalBounds() != new Rect2(-4096f, -16f, 8192f, 4096f))
        {
            GD.PushError("地图范围错误");
            return false;
        }
        if (map.ClampGlobalCameraCenter(new Vector2(9000f, 2032f)) != new Vector2(4064f, 2032f) ||
            map.ClampGlobalCameraCenter(new Vector2(-9000f, 2032f)) != new Vector2(-4064f, 2032f) ||
            map.ClampGlobalCameraCenter(new Vector2(0f, -1000f)) != Vector2.Zero ||
            map.ClampGlobalCameraCenter(new Vector2(0f, 9000f)) != new Vector2(0f, 4064f))
        {
            GD.PushError("镜头可以越出 128×128 地图菱形范围");
            return false;
        }
        return true;
    }
}
