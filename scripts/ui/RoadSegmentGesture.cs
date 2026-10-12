using System;
using System.Collections.Generic;
using Godot;

namespace FarmExchange.UI;

// 只保存当前未提交段；正式道路没有段编号或历史。
internal sealed class RoadSegmentGesture
{
    private bool? _xFirst;
    public Vector2I? StartCell { get; private set; }
    public Vector2I EndCell { get; private set; }
    public bool HasTurnOrder => _xFirst.HasValue;

    public void Begin(Vector2I cell)
    {
        Reset();
        StartCell = EndCell = cell;
    }

    public void UpdateEndpoint(Vector2I cell)
    {
        if (StartCell != null) EndCell = cell;
    }

    // 只由镜头已确认的、不按住鼠标的真实地图移动调用。
    public void TrackPointer(Vector2I cell, bool removing = false)
    {
        UpdateEndpoint(cell);
        if (StartCell is not Vector2I start || !removing && _xFirst.HasValue) return;
        Vector2I delta = cell - start;
        int x = Math.Abs(delta.X), y = Math.Abs(delta.Y);
        if (x != y) _xFirst = x > y;
    }

    public bool CanSubmit(bool removing) => removing || HasTurnOrder ||
        StartCell is Vector2I start && (EndCell.X == start.X || EndCell.Y == start.Y);

    public IReadOnlyList<Vector2I> GetCells(bool removing)
    {
        if (StartCell is not Vector2I start) return Array.Empty<Vector2I>();
        bool xFirst = _xFirst ?? Math.Abs(EndCell.X - start.X) >= Math.Abs(EndCell.Y - start.Y);
        var cells = new List<Vector2I> { start };
        Vector2I corner = xFirst ? new Vector2I(EndCell.X, start.Y) : new Vector2I(start.X, EndCell.Y);
        AppendLine(cells, start, corner);
        if (!removing) AppendLine(cells, corner, EndCell);
        return cells;
    }

    public void Reset()
    {
        StartCell = null;
        EndCell = default;
        _xFirst = null;
    }

    private static void AppendLine(List<Vector2I> cells, Vector2I start, Vector2I end)
    {
        Vector2I step = new(Math.Sign(end.X - start.X), Math.Sign(end.Y - start.Y));
        for (Vector2I cell = start + step; step != Vector2I.Zero && cell != end + step; cell += step)
            cells.Add(cell);
    }
}
