using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using FarmExchange.Weather;
using FarmExchange.Farming;
using FarmExchange.Gameplay;
using FarmExchange.Land;
using FarmExchange.Time;
using FarmExchange.Workers;
using FarmExchange.Terrain;

public partial class TestWorkerScheduler : Node
{
    public override async void _Ready() => GetTree().Quit(RunChecks() && await RunWaterChecksAsync(GetTree()) ? 0 : 1);

    public static bool RunChecks() => CheckMovementAndWorkSeconds() && CheckThreeWorkersAndClaims() &&
        CheckFarmWorkCredentials() && CheckInvalidClaimsReleasedFirst() && CheckRainAndFractionalPosition() &&
        CheckPlantingRevalidation() && CheckPauseAndDeterminism() && CheckUnequalAssignments() &&
        CheckBudgetsAndContinuousProduction() && CheckStableAnchorCursor() && CheckRepeatedCancellation();

    private static bool CheckRepeatedCancellation()
    {
        var farms = new FarmingSystem(FarmGame.MapSize * FarmGame.MapSize);
        var workers = new WorkerScheduler(TerrainTestFixtures.AllLandTerrain, new Vector2I(190, 190));
        var calendar = new GameCalendar();
        int? previous = null;
        for (int iteration = 0; iteration < 100; iteration++)
        {
            if (previous.HasValue) farms.Remove(previous.Value);
            Vector2I target = iteration % 2 == 0 ? new Vector2I(301, 277) : new Vector2I(27, 39);
            int index = (target.Y - 1) * FarmGame.MapSize + target.X - 1;
            farms.Place(index, CropKind.Radish);
            LandPosition before = workers.GetSnapshots()[0].ExactPosition;
            workers.AdvanceOneSecond(farms, calendar.Snapshot);
            calendar.TryAdvanceSeconds(1);
            WorkerSnapshot after = workers.GetSnapshots()[0];
            if (after.Path is not LandPath path || path.Points[0] != before || after.ExactPosition != path.GetPositionAtDistance(3) ||
                !double.IsFinite(after.ExactPosition.X) || !double.IsFinite(after.ExactPosition.Y))
                return Fail("连续100次取消没有保留原double位置或累计移动采样");
            previous = index;
        }
        return true;
    }

    public static async Task<bool> RunWaterChecksAsync(SceneTree tree)
    {
        using TerrainMap terrain = await TerrainMap.CreateAsync(12345, FarmGame.LoadTerrainRules(TerrainRules.ResourcePath), tree);
        Vector2I start = default;
        Vector2I target = default;
        Vector2I blocked = default;
        bool found = false;
        for (int y = 2; y < FarmGame.MapSize - 2 && !found; y++)
            for (int x = 3; x < FarmGame.MapSize - 4; x++)
            {
                if (terrain.IsFullLand(new Vector2I(x, y))) continue;
                int end = x;
                while (end < FarmGame.MapSize - 3 && !terrain.IsFullLand(new Vector2I(end, y))) end++;
                if (end - x < 8) { x = end; continue; }
                start = new Vector2I(x - 2, y);
                target = new Vector2I(end + 2, y);
                blocked = new Vector2I((x + end) / 2, y);
                found = true;
                break;
            }
        if (!found) return Fail("水域路线夹具未找到穿过池塘的起止点");
        int targetIndex = (target.Y - 1) * FarmGame.MapSize + target.X - 1;
        int blockedIndex = (blocked.Y - 1) * FarmGame.MapSize + blocked.X - 1;
        var firstFarms = new FarmingSystem(FarmGame.MapSize * FarmGame.MapSize);
        var batchFarms = new FarmingSystem(FarmGame.MapSize * FarmGame.MapSize);
        foreach (FarmingSystem farms in new[] { firstFarms, batchFarms })
        {
            farms.Place(blockedIndex, CropKind.Radish);
            farms.Place(targetIndex, CropKind.Radish);
        }
        var first = new WorkerScheduler(terrain.ReadOnly, start);
        var batch = new WorkerScheduler(terrain.ReadOnly, start);
        var firstCalendar = new GameCalendar();
        var batchCalendar = new GameCalendar();
        first.AdvanceOneSecond(firstFarms, firstCalendar.Snapshot);
        batch.AdvanceOneSecond(batchFarms, batchCalendar.Snapshot);
        firstCalendar.TryAdvanceSeconds(1);
        batchCalendar.TryAdvanceSeconds(1);
        WorkerSnapshot begun = first.GetSnapshots()[0];
        if (begun.TargetCell != target || begun.Path is not LandPath route || route.Points.Count <= 2 ||
            firstFarms.Get(blockedIndex).Stage != CropStage.None || begun.ExactPosition != route.GetPositionAtDistance(3))
            return Fail("工人没有先完成绕水求路再认领，或无效水中目标阻塞了后续可达任务");
        // 水中目标随后移除，批量可在真实行程的平静段推进。
        firstFarms.Remove(blockedIndex);
        batchFarms.Remove(blockedIndex);
        int travelTicks = route.GetTravelTicks(3);
        while (batchCalendar.Snapshot.ElapsedSeconds < travelTicks)
        {
            uint next = batch.GetNextEventSeconds(batchFarms, batchCalendar.Snapshot);
            uint remaining = (uint)travelTicks - batchCalendar.Snapshot.ElapsedSeconds;
            uint span = Math.Min(remaining, Math.Min(7u, next > 1 ? next - 1 : 1));
            if (next > 1)
                batch.AdvanceQuietSeconds(span, batchCalendar.Snapshot.ElapsedSeconds);
            else
                batch.AdvanceOneSecond(batchFarms, batchCalendar.Snapshot);
            batchCalendar.TryAdvanceSeconds(span);
            for (uint second = 0; second < span; second++)
            {
                first.AdvanceOneSecond(firstFarms, firstCalendar.Snapshot);
                firstCalendar.TryAdvanceSeconds(1);
            }
            if (!first.GetSnapshots().SequenceEqual(batch.GetSnapshots()))
                return Fail("绕水路径按单秒和不等长批量切分后精确位置、累计进度或最近秒凭据不同");
        }
        if (first.GetSnapshots()[0].ExactPosition != LandPosition.FromCell(target) ||
            firstFarms.Get(targetIndex).Stage != CropStage.None || batchFarms.Get(targetIndex).Stage != CropStage.None)
            return Fail("绕水整趟到达秒发生提前工作或未到精确终点");
        if (!first.AdvanceOneSecond(firstFarms, firstCalendar.Snapshot) ||
            firstFarms.Get(targetIndex).Stage != CropStage.Seeded)
            return Fail("绕水到达后没有以独立一秒播种");

        var cancelFarms = new FarmingSystem(FarmGame.MapSize * FarmGame.MapSize);
        cancelFarms.Place(targetIndex, CropKind.Radish);
        var cancel = new WorkerScheduler(terrain.ReadOnly, start);
        cancel.AdvanceOneSecond(cancelFarms, new GameCalendar().Snapshot);
        WorkerSnapshot moving = cancel.GetSnapshots()[0];
        cancelFarms.Remove(targetIndex);
        cancel.AdvanceOneSecond(cancelFarms, new GameCalendar(1).Snapshot);
        WorkerSnapshot cancelled = cancel.GetSnapshots()[0];
        if (cancelled.ExactPosition != moving.ExactPosition || cancelled.Activity != WorkerActivity.Idle ||
            cancelled.Movement != null || cancelled.Path != null)
            return Fail("绕水途中取消导致位置取整、瞬移或保留过期移动区间");
        int returnIndex = (start.Y - 1) * FarmGame.MapSize + start.X - 1;
        cancelFarms.Place(returnIndex, CropKind.Radish);
        cancel.AdvanceOneSecond(cancelFarms, new GameCalendar(2).Snapshot);
        WorkerSnapshot resumed = cancel.GetSnapshots()[0];
        if (resumed.Path?.Points[0] != moving.ExactPosition || moving.Path?.Points[0] != LandPosition.FromCell(start))
            return Fail("取消后的新路线没有从权威连续点接续，或旧快照被修改");
        return true;
    }

    private static bool CheckStableAnchorCursor()
    {
        var farms = new FarmingSystem(FarmGame.MapSize * FarmGame.MapSize);
        CalendarSnapshot spring = new GameCalendar().Snapshot;
        int first = 5 * FarmGame.MapSize + 5;
        int second = first + 6;
        int insertedBefore = first - 4;
        farms.Place(first, CropKind.Radish);
        farms.Place(second, CropKind.Radish);
        var originalIndices = farms.Indices;
        var workers = new WorkerScheduler(TerrainTestFixtures.AllLandTerrain, new Vector2I(6, 6));
        workers.AdvanceOneSecond(farms, spring);
        workers.AdvanceOneSecond(farms, spring);
        farms.Place(insertedBefore, CropKind.Radish);
        farms.Remove(first);
        if (!originalIndices.SequenceEqual(new[] { first, second }) ||
            !farms.Indices.SequenceEqual(new[] { insertedBefore, second }) ||
            workers.AdvanceOneSecond(farms, spring) ||
            workers.GetSnapshots()[0].TargetCell != new Vector2I(12, 6) ||
            workers.GetSnapshots()[0].GridPosition != new Vector2(9, 6))
            return Fail("农田集合增删改变了锚点游标语义，或旧只读索引缓存改变");
        workers.AdvanceOneSecond(farms, spring);
        workers.AdvanceOneSecond(farms, spring);
        workers.AdvanceOneSecond(farms, spring);
        if (workers.AdvanceOneSecond(farms, spring) ||
            workers.GetSnapshots()[0].TargetCell != new Vector2I(2, 6) ||
            workers.GetSnapshots()[0].GridPosition != new Vector2(9, 6))
            return Fail("锚点轮转没有在末尾回到新插入的前方农田");
        for (int secondIndex = 0; secondIndex < 3; secondIndex++)
            if (workers.AdvanceOneSecond(farms, spring))
                return Fail("不足三小格的最后移动秒提前执行了播种");
        if (workers.GetSnapshots()[0].GridPosition != new Vector2(2, 6) ||
            farms.Get(insertedBefore).Stage != CropStage.None || !workers.AdvanceOneSecond(farms, spring))
            return Fail("任意锚点的工作中心或向上取整移动时间错误");
        return true;
    }

    private static bool CheckMovementAndWorkSeconds()
    {
        Vector2I cell = new(2, 2);
        FarmingSystem farms = CreateFarms(new[] { cell });
        var workers = CreateWorkers(Vector2I.Zero);
        CalendarSnapshot spring = new GameCalendar().Snapshot;
        IReadOnlyList<WorkerSnapshot> initial = workers.GetSnapshots();
        if (initial.Count != 1 || !Matches(initial[0], Vector2.One, null, WorkerActivity.Idle))
            return Fail("初始工人快照不是独立的空闲状态");
        if (workers.AdvanceOneSecond(farms, spring) ||
            !Matches(workers.GetSnapshots()[0], new Vector2(4, 4), WorkCell(cell), WorkerActivity.Moving))
            return Fail("斜向移动速度不是每秒三小格，或移动时提前执行工作");
        if (workers.AdvanceOneSecond(farms, spring) ||
            !Matches(workers.GetSnapshots()[0], new Vector2(7, 7), WorkCell(cell), WorkerActivity.Sowing) ||
            farms.Get(IndexOf(cell)).Stage != CropStage.None)
            return Fail("到达的同一秒额外完成了播种");
        if (!workers.AdvanceOneSecond(farms, spring) ||
            farms.Get(IndexOf(cell)).Stage != CropStage.Seeded || workers.GetSnapshots()[0].Activity != WorkerActivity.Watering)
            return Fail("播种没有独占一秒，或播种后没有保留本田浇水任务");
        if (!workers.AdvanceOneSecond(farms, spring) || farms.Get(IndexOf(cell)).Stage != CropStage.Growing ||
            workers.GetSnapshots()[0].Activity != WorkerActivity.Idle || workers.GetSnapshots()[0].TargetCell != null ||
            initial[0].GridPosition != Vector2.One)
            return Fail("浇水完成没有释放任务，或旧快照随经营状态改变");
        return true;
    }

    private static bool CheckThreeWorkersAndClaims()
    {
        Vector2I[] cells = { new(63, 63), new(64, 63), new(65, 63) };
        FarmingSystem farms = CreateFarms(cells);
        var workers = CreateWorkers(cells);
        CalendarSnapshot spring = new GameCalendar().Snapshot;
        if (!workers.AdvanceOneSecond(farms, spring) || cells.Any(cell => farms.Get(IndexOf(cell)).Stage != CropStage.Seeded))
            return Fail("开局三人没有在各自格上真实参与播种");
        IReadOnlyList<WorkerSnapshot> sowed = workers.GetSnapshots();
        for (int i = 0; i < cells.Length; i++)
            if (sowed[i].WorkerNumber != i + 1 || sowed[i].TargetCell != WorkCell(cells[i]) || sowed[i].Activity != WorkerActivity.Watering)
                return Fail("固定工人顺序或独占农田认领不一致");
        if (!workers.AdvanceOneSecond(farms, spring) || cells.Any(cell => farms.Get(IndexOf(cell)).Stage != CropStage.Growing) ||
            workers.GetSnapshots().Any(worker => worker.Activity != WorkerActivity.Idle))
            return Fail("三人的一秒被分摊，或浇水完成没有全部释放认领");

        Vector2I oneCell = cells[0];
        farms = CreateFarms(new[] { oneCell });
        workers = CreateWorkers(oneCell, oneCell, oneCell);
        workers.AdvanceOneSecond(farms, spring);
        if (farms.Get(IndexOf(oneCell)).Stage != CropStage.Seeded ||
            workers.GetSnapshots().Count(worker => worker.TargetCell == WorkCell(oneCell)) != 1)
            return Fail("多人面对同一田发生重复播种或同秒供水");
        workers.AdvanceOneSecond(farms, spring);
        return farms.Get(IndexOf(oneCell)).Stage == CropStage.Growing || Fail("单田认领者没有完成连续供水");
    }

    private static bool CheckFarmWorkCredentials()
    {
        var farms = new FarmingSystem(1);
        CalendarSnapshot spring = new GameCalendar().Snapshot;
        if (farms.GetWorkNeed(0, spring) != null)
            return Fail("空地产生了农田工作需求");
        farms.Place(0, CropKind.Radish);
        FarmWorkRequest sow = farms.GetWorkNeed(0, spring)!.Value;
        farms.SetCrop(0, CropKind.Radish);
        if (farms.GetWorkNeed(0, spring) != sow || !farms.TryCompleteWork(sow, spring) || farms.TryCompleteWork(sow, spring))
            return Fail("同品种无变化语义失效，或旧播种请求被重复执行");
        FarmWorkRequest water = farms.GetWorkNeed(0, spring)!.Value;
        farms.SupplyWater(0);
        FarmSnapshot growing = farms.Get(0);
        if (farms.TryCompleteWork(water, spring) || farms.Get(0) != growing)
            return Fail("雨水满足后旧浇水请求重复操作或重置进度");
        for (int second = 0; second < 206; second++)
            farms.AdvanceGrowth(0, out _, WeatherKind.Sunny);
        FarmSnapshot harvested = farms.Get(0);
        if (farms.TryCompleteWork(sow, spring) || farms.IsCompletedWorkCurrent(sow) || farms.Get(0) != harvested)
            return Fail("收获新轮被同格旧播种请求启动");

        sow = farms.GetWorkNeed(0, spring)!.Value;
        farms.Remove(0);
        farms.Place(0, CropKind.Radish);
        if (farms.TryCompleteWork(sow, spring) || farms.Get(0).Stage != CropStage.None)
            return Fail("同格同品种重建误接受旧对象任务");
        sow = farms.GetWorkNeed(0, spring)!.Value;
        farms.SetCrop(0, CropKind.Wheat);
        if (farms.TryCompleteWork(sow, spring) || farms.Get(0).Stage != CropStage.None)
            return Fail("改种后旧任务改变了新作物阶段");
        sow = farms.GetWorkNeed(0, spring)!.Value;
        farms.Clear();
        farms.Place(0, CropKind.Wheat);
        if (farms.TryCompleteWork(sow, spring))
            return Fail("夹具清空重建后旧凭据重新生效");

        sow = farms.GetWorkNeed(0, spring)!.Value;
        farms.RestartCrop(0, CropKind.Wheat);
        if (farms.TryCompleteWork(sow, spring) || farms.Get(0).Stage != CropStage.None)
            return Fail("同种强制重启复活了旧播种请求");
        sow = farms.GetWorkNeed(0, spring)!.Value;
        farms.TryCompleteWork(sow, spring);
        water = farms.GetWorkNeed(0, spring)!.Value;
        farms.RestartCrop(0, CropKind.Wheat);
        if (farms.TryCompleteWork(water, spring) || farms.IsCompletedWorkCurrent(sow) ||
            farms.Get(0).Stage != CropStage.None)
            return Fail("同种强制重启接受旧供水请求或保留了旧成功播种");

        farms.TryWork(0, spring);
        water = farms.GetWorkNeed(0, spring)!.Value;
        farms.SetCrop(0, CropKind.Corn);
        if (farms.TryCompleteWork(water, spring) || farms.Get(0).Stage != CropStage.None)
            return Fail("异种切换接受了旧供水请求");

        farms.TryWork(0, spring);
        water = farms.GetWorkNeed(0, spring)!.Value;
        farms.Remove(0);
        farms.Place(0, CropKind.Corn);
        if (farms.TryCompleteWork(water, spring) || farms.Get(0).Stage != CropStage.None)
            return Fail("同格同种重建接受了旧供水请求");
        sow = farms.GetWorkNeed(0, spring)!.Value;
        farms.TryCompleteWork(sow, spring);
        water = farms.GetWorkNeed(0, spring)!.Value;
        farms.ClearDisallowedCrops(Season.Autumn);
        FarmSnapshot cleared = farms.Get(0);
        if (farms.TryCompleteWork(water, spring) || farms.IsCompletedWorkCurrent(sow) || farms.Get(0) != cleared)
            return Fail("越季清理后旧供水动作改变了空田");
        return true;
    }

    private static bool CheckInvalidClaimsReleasedFirst()
    {
        Vector2I first = new(1, 0);
        Vector2I second = new(2, 0);
        FarmingSystem farms = CreateFarms(new[] { first, second });
        CalendarSnapshot spring = new GameCalendar().Snapshot;
        farms.TryWork(IndexOf(first), spring);
        var workers = CreateWorkers(Vector2I.Zero, new Vector2I(3, 0));
        workers.AdvanceOneSecond(farms, spring);
        farms.Remove(IndexOf(first));
        farms.SetCrop(IndexOf(second), CropKind.Corn);
        if (workers.AdvanceOneSecond(farms, spring) || workers.GetSnapshots()[0].TargetCell != WorkCell(second) ||
            workers.GetSnapshots()[1].TargetCell != null || farms.Get(IndexOf(second)).Stage != CropStage.None)
            return Fail("没有先释放全体失效认领，或旧任务给改种田执行工作");
        return true;
    }

    private static bool CheckRainAndFractionalPosition()
    {
        CalendarSnapshot spring = new GameCalendar().Snapshot;
        Vector2I cell = new(4, 0);
        FarmingSystem farms = CreateFarms(new[] { cell });
        farms.TryWork(IndexOf(cell), spring);
        var workers = CreateWorkers(Vector2I.Zero);
        workers.AdvanceOneSecond(farms, spring);
        farms.SupplyWater(IndexOf(cell));
        FarmSnapshot rainStarted = farms.Get(IndexOf(cell));
        if (workers.AdvanceOneSecond(farms, spring) || workers.GetSnapshots()[0].GridPosition != new Vector2(4, 1) ||
            workers.GetSnapshots()[0].Activity != WorkerActivity.Idle || farms.Get(IndexOf(cell)) != rainStarted)
            return Fail("雨水没有取消移动中的过期浇水任务，或取消发生瞬移");

        cell = new Vector2I(2, 0);
        farms = CreateFarms(new[] { cell });
        workers = CreateWorkers(Vector2I.Zero);
        workers.AdvanceOneSecond(farms, spring);
        farms.SupplyWater(IndexOf(cell));
        workers.AdvanceOneSecond(farms, spring);
        if (!workers.AdvanceOneSecond(farms, spring) || farms.Get(IndexOf(cell)).Stage != CropStage.Growing ||
            workers.GetSnapshots()[0].Activity != WorkerActivity.Idle)
            return Fail("移动期间湿润的空田没有省去人工浇水");

        cell = Vector2I.Zero;
        farms = CreateFarms(new[] { cell });
        workers = CreateWorkers(cell);
        workers.AdvanceOneSecond(farms, spring);
        farms.SupplyWater(IndexOf(cell));
        rainStarted = farms.Get(IndexOf(cell));
        if (workers.AdvanceOneSecond(farms, spring) || workers.GetSnapshots()[0].Activity != WorkerActivity.Idle ||
            farms.Get(IndexOf(cell)) != rainStarted)
            return Fail("浇水完成的最后一秒降雨仍执行了过期人工供水");

        int removed = 3 * FarmGame.MapSize + 8;
        int replacement = 9 * FarmGame.MapSize + 3;
        farms = new FarmingSystem(FarmGame.MapSize * FarmGame.MapSize);
        farms.Place(removed, CropKind.Radish);
        workers = new WorkerScheduler(TerrainTestFixtures.AllLandTerrain, Vector2I.One);
        workers.AdvanceOneSecond(farms, spring);
        if (!workers.GetSnapshots()[0].GridPosition.IsEqualApprox(new Vector2(4, 2.125f)))
            return Fail("非45度直线路线没有保留分数格位置");
        farms.Remove(removed);
        farms.Place(replacement, CropKind.Radish);
        if (workers.AdvanceOneSecond(farms, spring) ||
            !workers.GetSnapshots()[0].GridPosition.IsEqualApprox(new Vector2(4, 5.125f)))
            return Fail("取消后新路线没有从实际分数位置以三小格每秒继续");
        workers.AdvanceOneSecond(farms, spring);
        if (workers.AdvanceOneSecond(farms, spring) || workers.GetSnapshots()[0].GridPosition != new Vector2(4, 10) ||
            farms.Get(replacement).Stage != CropStage.None)
            return Fail("最后不足三小格的移动没有独占一秒，或抵达时抢先播种");
        return true;
    }

    private static bool CheckPlantingRevalidation()
    {
        Vector2I cell = new(2, 0);
        FarmingSystem farms = CreateFarms(new[] { cell }, CropKind.Corn);
        var workers = CreateWorkers(Vector2I.Zero);
        var calendar = new GameCalendar(7608);
        FarmWorkRequest sow = farms.GetWorkNeed(IndexOf(cell), calendar.Snapshot)!.Value;
        for (int second = 0; second < 2; second++)
        {
            if (workers.AdvanceOneSecond(farms, calendar.Snapshot))
                return Fail("季末路线途中提前播种");
            calendar.TryAdvanceSeconds(1);
        }
        if (workers.GetSnapshots()[0].Activity != WorkerActivity.Sowing ||
            !workers.AdvanceOneSecond(farms, calendar.Snapshot) || farms.TryCompleteWork(sow, calendar.Snapshot) ||
            farms.Get(IndexOf(cell)).Stage != CropStage.Seeded)
            return Fail("当前适季但预计时间不足仍拒绝播种，或旧播种请求重复执行");
        farms = CreateFarms(new[] { cell }, CropKind.Corn);
        workers = CreateWorkers(Vector2I.Zero);
        calendar = new GameCalendar(8638);
        sow = farms.GetWorkNeed(IndexOf(cell), calendar.Snapshot)!.Value;
        for (int second = 0; second < 2; second++)
        {
            workers.AdvanceOneSecond(farms, calendar.Snapshot);
            calendar.TryAdvanceSeconds(1);
        }
        if (workers.AdvanceOneSecond(farms, calendar.Snapshot) || farms.TryCompleteWork(sow, calendar.Snapshot) ||
            farms.Get(IndexOf(cell)).Stage != CropStage.None || workers.GetSnapshots()[0].Activity != WorkerActivity.Idle)
            return Fail("分配时适季但到达时进入禁生季节的播种未被执行重验拒绝");
        calendar = new GameCalendar(8640);
        if (workers.AdvanceOneSecond(farms, calendar.Snapshot) || farms.GetWorkNeed(IndexOf(cell), calendar.Snapshot) != null)
            return Fail("不适季田仍被选成工作需求");
        return true;
    }

    private static bool CheckPauseAndDeterminism()
    {
        Vector2I[] cells = { new(2, 0), new(1, 3), new(6, 1), new(0, 6) };
        Vector2I[] starts = { Vector2I.Zero, new(1, 0), new(2, 0) };
        FarmingSystem firstFarms = CreateFarms(cells);
        FarmingSystem secondFarms = CreateFarms(cells);
        var first = CreateWorkers(starts);
        var second = CreateWorkers(starts);
        var firstCalendar = new GameCalendar();
        var secondCalendar = new GameCalendar();
        first.AdvanceOneSecond(firstFarms, firstCalendar.Snapshot);
        second.AdvanceOneSecond(secondFarms, secondCalendar.Snapshot);
        firstCalendar.TryAdvanceSeconds(1);
        secondCalendar.TryAdvanceSeconds(1);
        IReadOnlyList<WorkerSnapshot> paused = first.GetSnapshots();
        firstCalendar.SetPaused(true);
        for (int repeat = 0; repeat < 5; repeat++)
            if (first.AdvanceOneSecond(firstFarms, firstCalendar.Snapshot) || !first.GetSnapshots().SequenceEqual(paused))
                return Fail("暂停期间移动、动作或认领发生变化");
        firstCalendar.SetPaused(false);
        for (int secondIndex = 0; secondIndex < 30; secondIndex++)
        {
            bool rain = secondIndex == 3 || secondIndex == 13;
            AdvanceSimulation(firstFarms, first, firstCalendar, cells, rain);
            AdvanceSimulation(secondFarms, second, secondCalendar, cells, rain);
            if (!first.GetSnapshots().SequenceEqual(second.GetSnapshots()) ||
                cells.Any(cell => firstFarms.Get(IndexOf(cell)) != secondFarms.Get(IndexOf(cell))))
                return Fail("同一模拟秒与输入序列出现不确定结果，或恢复补算现实时间");
        }
        return true;
    }

    private static bool CheckUnequalAssignments()
    {
        Vector2I[] cells = Enumerable.Range(0, 6).Select(x => new Vector2I(x, 0)).ToArray();
        FarmingSystem farms = CreateFarms(cells);
        var workers = CreateWorkers(Vector2I.Zero, new Vector2I(1, 0), new Vector2I(7, 7));
        var calendar = new GameCalendar();
        int[] completions = new int[3];
        int threeWorkersFinishedSeconds = 0;
        for (int second = 0; second < 20; second++)
        {
            IReadOnlyList<WorkerSnapshot> before = workers.GetSnapshots();
            workers.AdvanceOneSecond(farms, calendar.Snapshot);
            IReadOnlyList<WorkerSnapshot> after = workers.GetSnapshots();
            for (int worker = 0; worker < completions.Length; worker++)
                if (before[worker].Activity == WorkerActivity.Watering && after[worker].Activity == WorkerActivity.Idle)
                    completions[worker]++;
            calendar.TryAdvanceSeconds(1);
            if (threeWorkersFinishedSeconds == 0 && cells.All(cell => farms.Get(IndexOf(cell)).Stage == CropStage.Growing))
                threeWorkersFinishedSeconds = second + 1;
        }
        if (completions.Any(count => count == 0) || completions.Distinct().Count() == 1 ||
            cells.Any(cell => farms.Get(IndexOf(cell)).Stage != CropStage.Growing))
            return Fail("异长路线没有让三人都参与，或测试未覆盖不均匀分配");
        farms = CreateFarms(cells);
        workers = CreateWorkers(Vector2I.Zero);
        for (int second = 1; second <= threeWorkersFinishedSeconds; second++)
            workers.AdvanceOneSecond(farms, calendar.Snapshot);
        if (cells.All(cell => farms.Get(IndexOf(cell)).Stage == CropStage.Growing))
            return Fail("同样初值下三人没有缩短异长路线布局的整体等工时间");
        return true;
    }

    private static bool CheckBudgetsAndContinuousProduction()
    {
        Vector2I[] compact = Enumerable.Range(0, 4).SelectMany(y =>
            new[] { new Vector2I(0, y), new Vector2I(3, y) }).ToArray();
        Vector2I[] wide = Enumerable.Range(0, 4).SelectMany(y =>
            new[] { new Vector2I(0, y * 2), new Vector2I(7, y * 2) }).ToArray();
        Vector2I[] twentyFour = new[] { 63, 66, 70 }.SelectMany(y =>
            Enumerable.Range(63, 8).Select(x => new Vector2I(x, y))).ToArray();
        Vector2I[] starts = { new(63, 63), new(64, 63), new(65, 63) };
        if (!CheckProductionBudget(compact, new[] { Vector2I.Zero }, 40, 45) ||
            !CheckProductionBudget(wide, new[] { new Vector2I(7, 7) }, 72, 81) ||
            !CheckProductionBudget(twentyFour, starts, 72, 81) ||
            !CheckExistingWorkBudget(compact, new[] { Vector2I.Zero }, 45) ||
            !CheckExistingWorkBudget(wide, new[] { new Vector2I(7, 7) }, 81) ||
            !CheckExistingWorkBudget(twentyFour, starts, 81))
            return false;

        Vector2I[] moreThanEight = Enumerable.Range(0, 9).Select(x => new Vector2I(x, 0)).ToArray();
        FarmingSystem farms = CreateFarms(moreThanEight);
        var single = CreateWorkers(Vector2I.Zero);
        var calendar = new GameCalendar();
        for (int second = 0; second < 100; second++)
            AdvanceSimulation(farms, single, calendar, moreThanEight);
        return moreThanEight.All(cell => farms.Get(IndexOf(cell)).Stage == CropStage.Growing) ||
            Fail("超出八田验收规模被硬拒绝或长期遗漏");
    }

    private static bool CheckProductionBudget(Vector2I[] cells, Vector2I[] starts, int initialBoundSeconds, int boundSeconds)
    {
        FarmingSystem farms = CreateFarms(cells);
        var workers = CreateWorkers(starts);
        var calendar = new GameCalendar();
        var readySeconds = new uint[cells.Length];
        var waiting = Enumerable.Repeat(true, cells.Length).ToArray();
        var suppliedRounds = new int[cells.Length];
        for (int second = 0; second < 1000; second++)
        {
            for (int field = 0; field < cells.Length; field++)
                if (farms.AdvanceGrowth(IndexOf(cells[field]), out _, WeatherKind.Sunny))
                {
                    waiting[field] = true;
                    readySeconds[field] = calendar.Snapshot.ElapsedSeconds;
                }
            workers.AdvanceOneSecond(farms, calendar.Snapshot);
            calendar.TryAdvanceSeconds(1);
            IReadOnlyList<WorkerSnapshot> snapshots = workers.GetSnapshots();
            if (snapshots.Where(worker => worker.TargetCell != null).Select(worker => worker.TargetCell).Distinct().Count() !=
                snapshots.Count(worker => worker.TargetCell != null))
                return Fail("持续生产中出现重复认领");
            for (int field = 0; field < cells.Length; field++)
            {
                if (!waiting[field] || farms.Get(IndexOf(cells[field])).Stage != CropStage.Growing)
                    continue;
                uint elapsed = calendar.Snapshot.ElapsedSeconds - readySeconds[field];
                int limit = suppliedRounds[field] == 0 ? initialBoundSeconds : boundSeconds;
                if (elapsed > limit)
                    return Fail($"{starts.Length} 人、{cells.Length} 田完整等工至供水用时 {elapsed} 秒，超过 {limit} 秒");
                waiting[field] = false;
                suppliedRounds[field]++;
            }
            if (suppliedRounds.All(rounds => rounds >= 3))
                return true;
        }
        return Fail("明确规模内没有连续完成至少三轮供水");
    }

    private static bool CheckExistingWorkBudget(Vector2I[] cells, Vector2I[] starts, int boundSeconds)
    {
        FarmingSystem farms = CreateFarms(cells.Take(starts.Length));
        var workers = CreateWorkers(starts);
        var calendar = new GameCalendar();
        workers.AdvanceOneSecond(farms, calendar.Snapshot);
        calendar.TryAdvanceSeconds(1);
        IReadOnlyList<WorkerSnapshot> existing = workers.GetSnapshots();
        if (existing.Any(worker => worker.TargetCell == null || worker.Activity == WorkerActivity.Idle))
            return Fail("既有工作预算夹具没有让全部工人先真实认领并开始工作");

        var readySeconds = new uint[cells.Length];
        for (int field = starts.Length; field < cells.Length; field++)
        {
            farms.Place(IndexOf(cells[field]), CropKind.Radish);
            readySeconds[field] = calendar.Snapshot.ElapsedSeconds;
            if (farms.GetWorkNeed(IndexOf(cells[field]), calendar.Snapshot) == null)
                return Fail("其他田没有在既有工作进行中真实具备播种条件");
        }
        var waiting = Enumerable.Repeat(true, cells.Length).ToArray();
        for (int second = 0; second < boundSeconds; second++)
        {
            AdvanceSimulation(farms, workers, calendar, cells);
            for (int field = 0; field < cells.Length; field++)
            {
                if (!waiting[field] || farms.Get(IndexOf(cells[field])).Stage != CropStage.Growing)
                    continue;
                uint elapsed = calendar.Snapshot.ElapsedSeconds - readySeconds[field];
                if (elapsed > boundSeconds)
                    return Fail($"{starts.Length} 项既有工作下，{cells.Length} 田等工至供水 {elapsed} 秒，超过 {boundSeconds} 秒");
                waiting[field] = false;
            }
            if (waiting.All(field => !field))
                return true;
        }
        return Fail($"{starts.Length} 项既有工作下没有在 {boundSeconds} 秒内完成全部供水");
    }

    private static FarmingSystem CreateFarms(IEnumerable<Vector2I> cells, CropKind crop = CropKind.Radish)
    {
        var farms = new FarmingSystem(FarmGame.MapSize * FarmGame.MapSize);
        foreach (Vector2I cell in cells)
            farms.Place(IndexOf(cell), crop);
        return farms;
    }

    private static void AdvanceSimulation(FarmingSystem farms, WorkerScheduler workers, GameCalendar calendar,
        IEnumerable<Vector2I> cells, bool rain = false)
    {
        if (calendar.IsPaused)
            return;
        if (rain)
            foreach (Vector2I cell in cells)
                farms.SupplyWater(IndexOf(cell));
        foreach (Vector2I cell in cells)
            farms.AdvanceGrowth(IndexOf(cell), out _, WeatherKind.Sunny);
        workers.AdvanceOneSecond(farms, calendar.Snapshot);
        calendar.TryAdvanceSeconds(1);
    }

    // 以下预算夹具以标准田跨度描述布局，再换算为基础格；原秒数上界保持。
    private static int IndexOf(Vector2I cell) => cell.Y * 3 * FarmGame.MapSize + cell.X * 3;

    private static Vector2I WorkCell(Vector2I cell) =>
        BuildingFootprint.WorkCell(cell * 3, BuildingKind.Farm);

    private static WorkerScheduler CreateWorkers(params Vector2I[] cells) =>
        new(TerrainTestFixtures.AllLandTerrain, cells.Select(WorkCell).ToArray());

    private static bool Matches(WorkerSnapshot worker, Vector2 position, Vector2I? target, WorkerActivity activity) =>
        worker.WorkerNumber == 1 && worker.ExactPosition == LandPosition.FromVector2(position) &&
        worker.GridPosition == position && worker.TargetCell == target && worker.Activity == activity;

    private static bool Fail(string message)
    {
        GD.PushError("工人调度测试失败：" + message);
        return false;
    }
}
