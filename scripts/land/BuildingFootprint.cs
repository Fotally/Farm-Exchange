using System;
using System.Collections.Generic;
using Godot;
using FarmExchange.Gameplay;

namespace FarmExchange.Land;

public sealed class BuildingFootprint
{
    private static readonly BuildingFootprint Production = new(new[]
    {
        new Vector2I(0, 0), new Vector2I(1, 0), new Vector2I(2, 0),
        new Vector2I(0, 1), new Vector2I(1, 1), new Vector2I(2, 1),
        new Vector2I(0, 2), new Vector2I(1, 2), new Vector2I(2, 2),
    }, new Vector2I(1, 1));
    private static readonly BuildingFootprint Road = new(new[] { Vector2I.Zero }, Vector2I.Zero);

    public IReadOnlyList<Vector2I> Offsets { get; }
    public Vector2I WorkOffset { get; }

    private BuildingFootprint(Vector2I[] offsets, Vector2I workOffset)
    {
        Offsets = Array.AsReadOnly(offsets);
        WorkOffset = workOffset;
    }

    public static BuildingFootprint Get(BuildingKind building) => building switch
    {
        BuildingKind.Farm or BuildingKind.Processor => Production,
        BuildingKind.Road => Road,
        _ => throw new ArgumentOutOfRangeException(nameof(building)),
    };

    public static Vector2I WorkCell(Vector2I anchorCell, BuildingKind building) =>
        anchorCell + Get(building).WorkOffset;
}
