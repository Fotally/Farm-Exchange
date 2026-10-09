using System.Collections.Generic;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Land;
using Microsoft.Extensions.Logging;

namespace FarmExchange.Logging;

/**
 * <summary>投影实际放置命令重验已经走过的短路路径，只接受实际返回的失败码。</summary>
 * <remarks>只由命令执行路径调用，纯预检与界面查询不记录诊断。</remarks>
 */
internal sealed class PlacementDiagnostics
{
    private static readonly LogEventDescriptor Checked = new(100, "RuleChecked", "FarmExchange.Land.PlacementRules", LogLevel.Debug, false);
    private readonly GameLog _context;
    internal PlacementDiagnostics(GameLog context) => _context = context;

    internal void Completed(Vector2I cell, BuildingKind building, CropKind crop, PlacementCheck result)
    {
        if (!_context.Diagnostics.ShouldCapture(Checked.Name, cell: cell)) return;
        _context.Observe(() =>
        {
            var fields = _context.Context("Command");
            fields.Remove("Phase");
            fields["RuleKind"] = "Placement";
            fields["Cell"] = new Dictionary<string, object?> { ["X"] = cell.X, ["Y"] = cell.Y };
            fields["BuildingKind"] = building.ToString();
            fields["Crop"] = crop.ToString();
            var checks = new Dictionary<string, object?> { ["Building"] = result.Failure != LandFailure.InvalidBuilding };
            if (result.Failure != LandFailure.InvalidBuilding)
            {
                if (building != BuildingKind.Road) checks["Crop"] = result.Failure != LandFailure.InvalidCrop;
                if (result.Failure != LandFailure.InvalidCrop)
                {
                    checks["Bounds"] = result.Failure != LandFailure.OutOfBounds;
                    if (result.Failure != LandFailure.OutOfBounds)
                    {
                        checks["Occupancy"] = result.Failure != LandFailure.Occupied;
                        if (result.Failure != LandFailure.Occupied) checks["Funds"] = result.Failure != LandFailure.InsufficientFunds;
                    }
                }
            }
            fields["CheckResults"] = checks;
            fields["Outcome"] = result.Allowed ? "Success" : "Rejected";
            _context.Diagnostics.Capture(Checked, "明确坐标的实际放置检查", fields);
        });
    }
}
