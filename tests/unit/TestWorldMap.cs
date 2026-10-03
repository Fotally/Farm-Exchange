using Godot;
using System.Collections.Generic;
using System.Linq;
using FarmExchange.Gameplay;
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
        return CheckPreviewEdges();
    }

    private static bool CheckPreviewEdges()
    {
        // 使用当前规格之外的 2×4 占地验证外围几何，没有新增可建建筑类型。
        var offsets = new HashSet<Vector2I>();
        for (int row = 0; row < 4; row++)
            for (int col = 0; col < 2; col++)
                offsets.Add(new Vector2I(col, row));
        var anchor = new Vector2I(7, 382);
        var game = new FarmGame(12345);
        Vector2I conflict = anchor + new Vector2I(0, 1);
        if (!game.TryPlace(conflict, BuildingKind.Road, default).Success)
        {
            GD.PushError("非当前规格占地的冲突夹具无法建造道路");
            return false;
        }
        var cells = PlacementPreviewGeometry.GetCells(anchor, offsets.ToArray(), game);
        if (cells.Length != 8 || cells.Select(cell => cell.Cell).Distinct().Count() != 8 ||
            cells.Count(cell => cell.Blocked) != 5 ||
            cells.Any(cell => !offsets.Contains(cell.Cell - anchor) ||
                cell.Blocked != (cell.Cell == conflict || !MapCoordinates.ContainsCell(cell.Cell))))
        {
            GD.PushError("2×4 占地的覆盖、数量或逐格冲突/越界反馈未读取实际偏移");
            return false;
        }
        var edges = new HashSet<(Vector2, Vector2)>();
        foreach (Vector2I offset in offsets)
            foreach (var edge in PlacementPreviewGeometry.GetCellOuterEdges(anchor, offset, offsets))
                if (!edges.Add(edge))
                {
                    GD.PushError("不同占地规格的预览外围出现重复边");
                    return false;
                }
        Vector2[] outline = MapCoordinates.GridRectangleOutline(anchor, 2, 4);
        if (edges.Count != 12)
        {
            GD.PushError("2×4 占地外围未从实际偏移生成12条外边");
            return false;
        }
        foreach (var (start, end) in edges)
        {
            bool onOuterEdge = false;
            for (int side = 0; side < outline.Length; side++)
            {
                Vector2 from = outline[side];
                Vector2 to = outline[(side + 1) % outline.Length];
                if (Geometry2D.GetClosestPointToSegment(start, from, to).IsEqualApprox(start) &&
                    Geometry2D.GetClosestPointToSegment(end, from, to).IsEqualApprox(end))
                    onOuterEdge = true;
            }
            if (!onOuterEdge)
            {
                GD.PushError("预览外围绘制了内部边或限制了越界边的位置");
                return false;
            }
        }
        return true;
    }
}
