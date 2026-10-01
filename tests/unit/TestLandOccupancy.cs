using System;
using System.Collections.Generic;
using Godot;
using FarmExchange.Farming;
using FarmExchange.Gameplay;
using FarmExchange.Land;

public partial class TestLandOccupancy : Node
{
    public override void _Ready() => GetTree().Quit(RunChecks() ? 0 : 1);

    public static bool RunChecks() => CheckFootprints() && CheckInstances() && CheckEveryRemovalCell() &&
        CheckBoundaries() && CheckConflictAtomicity() && CheckPlacementRules();

    private static bool CheckFootprints()
    {
        BuildingFootprint farm = BuildingFootprint.Get(BuildingKind.Farm);
        BuildingFootprint processor = BuildingFootprint.Get(BuildingKind.Processor);
        BuildingFootprint road = BuildingFootprint.Get(BuildingKind.Road);
        var offsets = new HashSet<Vector2I>(farm.Offsets);
        if (farm.Offsets.Count != 9 || offsets.Count != 9 ||
            processor.Offsets.Count != 9 || farm.WorkOffset != Vector2I.One ||
            processor.WorkOffset != Vector2I.One || road.Offsets.Count != 1 ||
            road.Offsets[0] != Vector2I.Zero || road.WorkOffset != Vector2I.Zero)
            return Fail("固定生产占地、道路占地或工作中心错误");
        for (int row = 0; row < 3; row++)
            for (int col = 0; col < 3; col++)
                if (!offsets.Contains(new Vector2I(col, row)))
                    return Fail("生产占地不是完整的 3×3 格");
        if (!Throws<NotSupportedException>(() => ((IList<Vector2I>)farm.Offsets)[0] = Vector2I.One) ||
            !Throws<ArgumentOutOfRangeException>(() => BuildingFootprint.Get(BuildingKind.None)) ||
            !Throws<ArgumentOutOfRangeException>(() => BuildingFootprint.Get((BuildingKind)999)) ||
            BuildingFootprint.WorkCell(new Vector2I(5, 7), BuildingKind.Farm) != new Vector2I(6, 8))
            return Fail("固定占地可被外部改写，或非法类型与工作格换算错误");
        return true;
    }

    private static bool CheckInstances()
    {
        LandOccupancy land = NewLand();
        IReadOnlyList<BuildingSpaceSnapshot> empty = land.Instances;
        Vector2I farmAnchor = new(5, 7);
        Vector2I processorAnchor = new(21, 11);
        land.Place(IndexOf(processorAnchor), BuildingKind.Processor);
        land.Place(IndexOf(farmAnchor), BuildingKind.Farm);
        land.Place(0, BuildingKind.Road);
        IReadOnlyList<BuildingSpaceSnapshot> spaces = land.Instances;
        if (empty.Count != 0 || spaces.Count != 3 || spaces[0].Building != BuildingKind.Road ||
            spaces[1].AnchorIndex != IndexOf(farmAnchor) || spaces[2].AnchorIndex != IndexOf(processorAnchor) ||
            !ReferenceEquals(spaces, land.Instances) ||
            !Throws<NotSupportedException>(() => ((IList<BuildingSpaceSnapshot>)spaces).Clear()))
            return Fail("实例快照不是稳定锚点顺序的不可变缓存");

        foreach (BuildingSpaceSnapshot space in spaces)
        {
            Vector2I expectedWork = space.AnchorCell + (space.Building == BuildingKind.Road ? Vector2I.Zero : Vector2I.One);
            if (space.WorkCell != expectedWork)
                return Fail("空间快照没有提供正确工作中心");
            foreach (Vector2I offset in space.Footprint.Offsets)
            {
                int index = IndexOf(space.AnchorCell + offset);
                if (land.Get(index) != space.Building || land.ResolveAnchorIndex(index) != space.AnchorIndex ||
                    !ReferenceEquals(land.GetSpace(index), space))
                    return Fail("占用子格未解析到同一空间实例");
            }
        }
        land.Remove(IndexOf(farmAnchor + new Vector2I(2, 2)));
        foreach (Vector2I offset in spaces[1].Footprint.Offsets)
        {
            int index = IndexOf(farmAnchor + offset);
            if (land.Get(index) != BuildingKind.None || land.ResolveAnchorIndex(index) != -1 || land.GetSpace(index) != null)
                return Fail("从末端子格拆除后仍有占用残片");
        }
        IReadOnlyList<BuildingSpaceSnapshot> remaining = land.Instances;
        if (remaining.Count != 2 || spaces.Count != 3 || ReferenceEquals(spaces, remaining) ||
            land.Get(IndexOf(processorAnchor)) != BuildingKind.Processor || land.Get(0) != BuildingKind.Road)
            return Fail("整体拆除改变了旧快照或相邻实例");
        land.Place(IndexOf(farmAnchor), BuildingKind.Farm);
        if (land.Instances.Count != 3 || ReferenceEquals(spaces[1], land.GetSpace(IndexOf(farmAnchor))))
            return Fail("重建没有创建独立空间实例");
        land.Remove(0);
        land.Remove(IndexOf(processorAnchor + Vector2I.One));
        land.Clear();
        if (land.Instances.Count != 0 || land.Get(IndexOf(farmAnchor)) != BuildingKind.None ||
            !Throws<InvalidOperationException>(() => land.Remove(IndexOf(farmAnchor))))
            return Fail("道路拆除或夹具清理未完整释放土地");
        return true;
    }

    private static bool CheckBoundaries()
    {
        LandOccupancy land = NewLand();
        int last = FarmGame.MapSize - 1;
        Vector2I[] corners = { Vector2I.Zero, new(last, 0), new(0, last), new(last, last) };
        foreach (Vector2I corner in corners)
        {
            if (land.CheckFootprint(corner, BuildingKind.Road) != LandFailure.None)
                return Fail("地图角落不能放置单格道路");
            land.Place(IndexOf(corner), BuildingKind.Road);
            land.Remove(IndexOf(corner));
        }
        Vector2I[] invalidAnchors = { new(-1, 0), new(0, -1), new(last + 1, 0), new(0, last + 1),
            new(last - 1, 0), new(0, last - 1), new(last, last) };
        foreach (Vector2I anchor in invalidAnchors)
        {
            if (land.CheckFootprint(anchor, BuildingKind.Farm) != LandFailure.OutOfBounds || land.Instances.Count != 0)
                return Fail("部分越界占地通过预检或改变土地");
        }
        if (!Throws<ArgumentOutOfRangeException>(() => land.Place(-1, BuildingKind.Farm)) ||
            !Throws<ArgumentOutOfRangeException>(() => land.Place(IndexOf(new(last - 1, 0)), BuildingKind.Farm)) ||
            !Throws<ArgumentOutOfRangeException>(() => land.Place(FarmGame.MapSize * FarmGame.MapSize, BuildingKind.Road)) ||
            land.Instances.Count != 0)
            return Fail("越界执行没有拒绝或留下部分占用");
        Vector2I edgeAnchor = new(last - 2, last - 2);
        land.Place(IndexOf(edgeAnchor), BuildingKind.Processor);
        if (land.Get(IndexOf(new(last, last))) != BuildingKind.Processor || land.Instances.Count != 1)
            return Fail("完整贴边生产设施无法放置");
        return true;
    }

    private static bool CheckEveryRemovalCell()
    {
        LandOccupancy land = NewLand();
        Vector2I anchor = new(1, 2);
        foreach (BuildingKind kind in new[] { BuildingKind.Farm, BuildingKind.Processor })
        {
            BuildingFootprint footprint = BuildingFootprint.Get(kind);
            foreach (Vector2I removalOffset in footprint.Offsets)
            {
                land.Place(IndexOf(anchor), kind);
                land.Remove(IndexOf(anchor + removalOffset));
                if (land.Instances.Count != 0)
                    return Fail("从某一子格拆除后仍保留空间实例");
                foreach (Vector2I offset in footprint.Offsets)
                    if (land.GetSpace(IndexOf(anchor + offset)) != null)
                        return Fail("从某一子格拆除后仍保留部分占地");
            }
        }
        return true;
    }

    private static bool CheckConflictAtomicity()
    {
        LandOccupancy land = NewLand();
        Vector2I anchor = new(9, 13);
        int blockingIndex = IndexOf(anchor + new Vector2I(2, 2));
        land.Place(blockingIndex, BuildingKind.Road);
        IReadOnlyList<BuildingSpaceSnapshot> before = land.Instances;
        if (land.CheckFootprint(anchor, BuildingKind.Farm) != LandFailure.Occupied ||
            !Throws<InvalidOperationException>(() => land.Place(IndexOf(anchor), BuildingKind.Farm)) ||
            !ReferenceEquals(before, land.Instances) || land.Get(blockingIndex) != BuildingKind.Road)
            return Fail("末端子格冲突没有完整拒绝或改写已有道路");
        foreach (Vector2I offset in BuildingFootprint.Get(BuildingKind.Farm).Offsets)
            if (IndexOf(anchor + offset) != blockingIndex && land.Get(IndexOf(anchor + offset)) != BuildingKind.None)
                return Fail("末端冲突失败留下部分占地");
        land.Remove(blockingIndex);
        land.Place(IndexOf(anchor), BuildingKind.Farm);
        if (!Throws<InvalidOperationException>(() => land.Place(IndexOf(anchor + Vector2I.One), BuildingKind.Road)) ||
            land.Instances.Count != 1)
            return Fail("单格道路覆盖了生产设施子格");
        before = land.Instances;
        if (land.CheckFootprint(anchor, BuildingKind.None) != LandFailure.InvalidBuilding ||
            !Throws<ArgumentOutOfRangeException>(() => land.Place(IndexOf(anchor), (BuildingKind)999)) ||
            !ReferenceEquals(before, land.Instances))
            return Fail("无效建筑执行改变了占用或实例快照");
        return true;
    }

    private static bool CheckPlacementRules()
    {
        LandOccupancy land = NewLand();
        Vector2I anchor = new(17, 23);
        PlacementCheck Check(Vector2I cell, BuildingKind kind = BuildingKind.Farm,
            CropKind crop = CropKind.Wheat, int balance = 1000, int cost = 1000) =>
            PlacementRules.Check(cell, kind, crop, land, balance, cost);
        if (Check(anchor) != new PlacementCheck(LandFailure.None, 1000) ||
            Check(anchor, balance: 999) != new PlacementCheck(LandFailure.InsufficientFunds, 0) ||
            Check(anchor, BuildingKind.None) != new PlacementCheck(LandFailure.InvalidBuilding, 0) ||
            Check(anchor, crop: (CropKind)999) != new PlacementCheck(LandFailure.InvalidCrop, 0) ||
            Check(new(FarmGame.MapSize - 2, 0)) != new PlacementCheck(LandFailure.OutOfBounds, 0) ||
            Check(anchor, BuildingKind.Road, (CropKind)999, 100, 100) != new PlacementCheck(LandFailure.None, 100) ||
            land.Instances.Count != 0)
            return Fail("放置描述、完整范围、资金或道路作物检查错误");
        land.Place(IndexOf(anchor + new Vector2I(2, 2)), BuildingKind.Road);
        if (Check(anchor) != new PlacementCheck(LandFailure.Occupied, 0) ||
            Check(anchor, balance: 0).Failure != LandFailure.Occupied || land.Instances.Count != 1)
            return Fail("预检没有重新读取末端占用，或失败原因顺序改变");
        land.Clear();
        land.Place(IndexOf(new(FarmGame.MapSize - 1, 0)), BuildingKind.Road);
        if (Check(new(FarmGame.MapSize - 2, 0)).Failure != LandFailure.OutOfBounds)
            return Fail("范围失败应优先于范围内的占用冲突");
        return true;
    }

    private static LandOccupancy NewLand() => new(FarmGame.MapSize * FarmGame.MapSize);
    private static int IndexOf(Vector2I cell) => cell.Y * FarmGame.MapSize + cell.X;

    private static bool Throws<TException>(Action action) where TException : Exception
    {
        try { action(); return false; }
        catch (TException) { return true; }
    }

    private static bool Fail(string message)
    {
        GD.PushError(message);
        return false;
    }
}
