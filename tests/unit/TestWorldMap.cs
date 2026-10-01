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
            new(0, 0), new(383, 0), new(0, 383), new(383, 383), new(144, 237),
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
            MapCoordinates.GridPositionToLocal(new Vector2(383, 383)) != new Vector2(0, 12256))
        {
            GD.PushError("分数格位置没有沿用统一等距换算");
            return false;
        }

        if (MapCoordinates.LocalPositionToCell(new Vector2(31f, 0f)) != new Vector2I(0, 0) ||
            MapCoordinates.LocalPositionToCell(new Vector2(33f, 0f)) != new Vector2I(1, -1) ||
            MapCoordinates.ContainsCell(new Vector2I(-1, 0)) ||
            MapCoordinates.ContainsCell(new Vector2I(384, 0)) ||
            MapCoordinates.ContainsCell(new Vector2I(0, 384)))
        {
            GD.PushError("格边界或地图外判断错误");
            return false;
        }

        if (map.LocalBounds() != new Rect2(-12288f, -16f, 24576f, 12288f))
        {
            GD.PushError("地图范围错误");
            return false;
        }
        if (map.ClampGlobalCameraCenter(new Vector2(30000f, 6128f)) != new Vector2(12256f, 6128f) ||
            map.ClampGlobalCameraCenter(new Vector2(-30000f, 6128f)) != new Vector2(-12256f, 6128f) ||
            map.ClampGlobalCameraCenter(new Vector2(0f, -1000f)) != Vector2.Zero ||
            map.ClampGlobalCameraCenter(new Vector2(0f, 30000f)) != new Vector2(0f, 12256f))
        {
            GD.PushError("镜头可以越出 384×384 地图菱形范围");
            return false;
        }
        if (MapCoordinates.TileWidth != 64 || MapCoordinates.TileHeight != 32 ||
            MapCoordinates.GridRectangleBounds(new Vector2I(7, 7), 3, 3) != new Rect2(-96, 208, 192, 96) ||
            MapCoordinates.GridRectangleOutline(Vector2I.Zero, 384, 384)[2] != new Vector2(0, 12272))
        {
            GD.PushError("基础格、3×3设施尺寸或地图边缘错误");
            return false;
        }
        return true;
    }
}
