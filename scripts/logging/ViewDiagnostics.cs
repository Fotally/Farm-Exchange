using Godot;
using Microsoft.Extensions.Logging;

namespace FarmExchange.Logging;

/**
 * <summary>观察实际镜头、窗口及已执行的缓存绘制变化，稳定帧不生成明细。</summary>
 */
internal sealed class ViewDiagnostics
{
    private static readonly LogEventDescriptor Changed = new(107, "ViewChanged", "FarmExchange.World.WorldMap", LogLevel.Debug, false);
    private readonly GameLog _context;
    private bool _initialized;
    private Vector2I _window;
    private Vector2 _viewport, _center;
    private float _zoom;
    private int _rebuilt;

    internal ViewDiagnostics(GameLog context) => _context = context;

    internal void Observe(Vector2I window, Vector2 viewport, float playerZoom, Vector2 center, int rebuiltTotal)
    {
        bool windowChanged = window != _window;
        bool viewportChanged = viewport != _viewport;
        bool zoomChanged = playerZoom != _zoom;
        bool centerChanged = center != _center;
        int rebuilt = rebuiltTotal - _rebuilt;
        bool changed = _initialized && (windowChanged || viewportChanged || zoomChanged || centerChanged || rebuilt > 0);
        _initialized = true;
        _window = window;
        _viewport = viewport;
        _zoom = playerZoom;
        _center = center;
        _rebuilt = rebuiltTotal;
        if (!changed || !_context.Diagnostics.ShouldCapture(Changed.Name)) return;
        RecordChanged(window, viewport, playerZoom, center, rebuilt,
            windowChanged, viewportChanged, zoomChanged, centerChanged);
    }

    // 含捕获 lambda 的慢路径必须独立，避免编译器在每帧门禁之前创建闭包。
    private void RecordChanged(Vector2I window, Vector2 viewport, float playerZoom, Vector2 center, int rebuilt,
        bool windowChanged, bool viewportChanged, bool zoomChanged, bool centerChanged)
    {
        _context.Observe(() =>
        {
            var fields = _context.Context("Presentation");
            if (windowChanged) fields["WindowSize"] = new System.Collections.Generic.Dictionary<string, object?> { ["Width"] = window.X, ["Height"] = window.Y };
            if (viewportChanged) fields["ViewportSize"] = new System.Collections.Generic.Dictionary<string, object?> { ["Width"] = viewport.X, ["Height"] = viewport.Y };
            if (zoomChanged) fields["PlayerZoom"] = playerZoom;
            if (centerChanged) fields["CameraCenter"] = new System.Collections.Generic.Dictionary<string, object?> { ["X"] = center.X, ["Y"] = center.Y };
            if (rebuilt > 0) fields["RebuiltChunkCount"] = rebuilt;
            _context.Diagnostics.Capture(Changed, "实际视图发生变化", fields);
        });
    }
}
