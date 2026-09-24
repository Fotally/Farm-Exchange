using Godot;

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
            if (map.CellAtWorld(center) != cell)
            {
                GD.PushError($"格中心选择错误：{cell}");
                return false;
            }
        }

        if (map.CellAtWorld(new Vector2(31f, 0f)) != new Vector2I(0, 0) ||
            map.CellAtWorld(new Vector2(33f, 0f)) != new Vector2I(1, -1))
        {
            GD.PushError("格边界选择错误");
            return false;
        }

        if (map.WorldBounds() != new Rect2(-4096f, -16f, 8192f, 4096f))
        {
            GD.PushError("地图范围错误");
            return false;
        }
        if (map.ClampCameraCenter(new Vector2(9000f, 2032f)) != new Vector2(4064f, 2032f) ||
            map.ClampCameraCenter(new Vector2(-9000f, 2032f)) != new Vector2(-4064f, 2032f) ||
            map.ClampCameraCenter(new Vector2(0f, -1000f)) != Vector2.Zero ||
            map.ClampCameraCenter(new Vector2(0f, 9000f)) != new Vector2(0f, 4064f))
        {
            GD.PushError("镜头可以越出 128×128 地图菱形范围");
            return false;
        }
        return true;
    }
}
