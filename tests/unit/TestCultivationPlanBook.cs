using System;
using Godot;
using FarmExchange.Cultivation;
using FarmExchange.Farming;
using FarmExchange.Gameplay;
using FarmExchange.Time;

public partial class TestCultivationPlanBook : Node
{
    public override void _Ready() => GetTree().Quit(RunChecks() ? 0 : 1);

    public static bool RunChecks() => CheckValidation() && CheckSharedPlans() &&
        CheckModesAndCache() && CheckOneRoundAndYearWrap() && CheckManualAndFacade() &&
        CheckSeasonRescue() && CheckHarvestPlanDateActivation() && CheckRescueWithinPlanBar() &&
        CheckExecutedEntriesAfterEdit() && CheckPreservedRoundDoesNotConsumeEntry() &&
        CheckSowBeforeSeasonClearIsConsumed() && CheckSeasonEndCache() &&
        CheckRestBufferAndConsecutiveRounds() && CheckDifferentCropAfterBuffer();

    private static CultivationPlanRequest Request(CultivationMode mode = CultivationMode.PrepareNext) =>
        new("春季轮作", mode, new[] { new CultivationEntry(1, CropKind.Radish, 0), new CultivationEntry(2, CropKind.Wheat, 5) });

    private static bool CheckValidation()
    {
        var request = Request();
        if (!CultivationPlanBook.Validate(request).Success ||
            CultivationPlanBook.Validate(request with { Name = " " }).Success ||
            CultivationPlanBook.Validate(request with { Mode = (CultivationMode)99 }).Success ||
            CultivationPlanBook.Validate(request with { Entries = new[] { new CultivationEntry(0, CropKind.Wheat, 0) } }).Success ||
            CultivationPlanBook.Validate(request with { Entries = new[] { new CultivationEntry(1, (CropKind)99, 0) } }).Success ||
            CultivationPlanBook.Validate(request with { Entries = new[] { new CultivationEntry(1, CropKind.Wheat, 336) } }).Success ||
            CultivationPlanBook.Validate(request with { Entries = new[] { new CultivationEntry(1, CropKind.Wheat, -1) } }).Success ||
            CultivationPlanBook.Validate(request with { Entries = new[] { new CultivationEntry(1, CropKind.Radish, 0), new CultivationEntry(1, CropKind.Wheat, 5) } }).Success ||
            CultivationPlanBook.Validate(request with { Entries = new[] { new CultivationEntry(1, CropKind.Radish, 0), new CultivationEntry(2, CropKind.Wheat, 3) } }).Success ||
            CultivationPlanBook.Validate(request with { Entries = new[] { new CultivationEntry(1, CropKind.Radish, 0), new CultivationEntry(2, CropKind.Wheat, 4) } }).Success)
            return Fail("年度表无效配置、重叠或异种一天间隔没有拒绝");
        var same = request with { Entries = new[] { new CultivationEntry(1, CropKind.Radish, 0), new CultivationEntry(2, CropKind.Radish, 4) } };
        var wrapConflict = request with { Entries = new[] { new CultivationEntry(1, CropKind.Wheat, 330), new CultivationEntry(2, CropKind.Radish, 0) } };
        var risk = request with { Entries = new[] { new CultivationEntry(1, CropKind.Radish, 82) } };
        var safe = request with { Entries = new[] { new CultivationEntry(1, CropKind.Radish, 80) } };
        if (!CultivationPlanBook.Validate(same).Success || CultivationPlanBook.Validate(wrapConflict).Success ||
            CultivationPlanBook.Validate(risk).RiskEntryIds.Count != 1 ||
            CultivationPlanBook.Validate(safe).RiskEntryIds.Count != 0 ||
            !CultivationPlanBook.Validate(request with { Entries = Array.Empty<CultivationEntry>() }).Success)
            return Fail("同种连续、冬春冲突、禁生风险或全年度休耕错误");
        return true;
    }

    private static bool CheckSharedPlans()
    {
        var farming = new FarmingSystem(4);
        farming.Place(0, CropKind.Wheat);
        farming.Place(1, CropKind.Corn);
        var book = new CultivationPlanBook(farming);
        if (book.Create(Request() with { Name = "" }).Success || book.Contains(1))
            return Fail("创建失败仍保存共享表");
        var id = book.Create(Request()).Id;
        book.Apply(id, new[] { 0, 1 }, 0);
        var old = book.GetSnapshots();
        if (!book.Contains(id) || old[0].ReferencingFarms != 2 || farming.Get(0).CropKind != CropKind.Radish ||
            farming.Get(1).CropKind != CropKind.Radish || book.GetFarm(0).PlanId != id)
            return Fail("多田没有引用同一配置");
        var changed = Request() with { Name = "改名", Entries = new[] { new CultivationEntry(3, CropKind.Wheat, 10) } };
        if (book.Update(999, changed, 0).Success || book.Update(id, changed with { Name = "" }, 0).Success ||
            !book.Update(id, changed, 0).Success || old[0].Name != "春季轮作" || old[0].Entries.Count != 2 ||
            book.GetFarm(0).PlanName != "改名" || !book.GetFarm(1).IsResting ||
            book.GetFarm(0).PreparedTimeUnits != 3600)
            return Fail("共享编辑、失败零修改或独立快照错误");
        book.Synchronize(3600);
        if (!farming.Get(0).SowingEnabled || farming.Get(0).CropKind != CropKind.Wheat)
            return Fail("休耕结束没有按缓存事件启动");
        book.RemoveFarm(0);
        book.Clear();
        if (book.GetFarm(0) != default || book.GetSnapshots()[0].ReferencingFarms != 0)
            return Fail("拆田或清理遗留引用");
        return true;
    }

    private static bool CheckModesAndCache()
    {
        foreach (CultivationMode mode in Enum.GetValues<CultivationMode>())
        {
            var farming = new FarmingSystem(2);
            farming.Place(0, CropKind.Wheat);
            farming.TryWork(0, new GameCalendar().Snapshot); // 待水，完成日期尚未确定。
            var book = new CultivationPlanBook(farming);
            int id = book.Create(Request(mode)).Id;
            book.Apply(id, new[] { 0 }, 0);
            if (farming.Get(0).Stage != CropStage.Seeded || !book.GetFarm(0).WaitingForGrowthStart ||
                book.GetFarm(0).PreparedTimeUnits != null)
                return Fail("初次应用中断当前轮或虚构待水完成日期");
            farming.SupplyWater(0);
            book.Synchronize(7);
            FarmCultivationSnapshot prepared = book.GetFarm(0);
            if (prepared.PreparedCrop != CropKind.Wheat || prepared.PreparedTimeUnits != 1800 ||
                prepared.WaitingForGrowthStart)
                return Fail("没有按当前精确完成日期定位后续年度条");
            if (book.Update(id, Request(mode) with { Name = "共享修改" }, 7).Success != true ||
                farming.Get(0).Stage != CropStage.Growing)
                return Fail("编辑共享表中断当前轮");
            book.Synchronize(1440);
            if (farming.Get(0).Stage != CropStage.Growing || farming.Get(0).SowingEnabled)
                return Fail("空白休耕未保留当前轮并停止复种");
        }
        return true;
    }

    private static bool CheckOneRoundAndYearWrap()
    {
        var farming = new FarmingSystem(1);
        farming.Place(0, CropKind.Radish);
        var book = new CultivationPlanBook(farming);
        int id = book.Create(new("冬春", CultivationMode.PrepareNext,
            new[] { new CultivationEntry(1, CropKind.Radish, 334) })).Id;
        book.Apply(id, new[] { 0 }, 0); // 上年冬季条在春初仍有效。
        farming.TryWork(0, new GameCalendar().Snapshot);
        farming.SupplyWater(0);
        book.Synchronize(7);
        if (book.GetFarm(0).PreparedTimeUnits != 334 * 360 || farming.Get(0).SowingEnabled)
            return Fail("冬春同一轮未定位到本年冬季下一次安排");
        for (int tick = 0; tick < 206; tick++)
            farming.AdvanceGrowth(0, out _);
        book.Harvested(0, 1449);
        if (farming.Get(0).SowingEnabled || !book.GetFarm(0).IsResting)
            return Fail("同一作物条收获后重复播种");
        book.Synchronize(1449);
        if (farming.Get(0).SowingEnabled || book.GetFarm(0).PreparedTimeUnits != 334 * 360)
            return Fail("收获后日期同步未保持休耕和未来目标缓存");
        book.Apply(id, new[] { 0 }, 1449);
        if (farming.Get(0).SowingEnabled)
            return Fail("重复应用使同条执行第二轮");
        book.Synchronize(334 * 360);
        if (!farming.Get(0).SowingEnabled)
            return Fail("冬春环绕排程未按年重复");
        return true;
    }

    private static bool CheckManualAndFacade()
    {
        var game = new FarmGame(12345);
        Vector2I farm = new(189, 189);
        Vector2I other = new(192, 189);
        game.SetFarmCrop(farm, CropKind.Radish);
        game.AdvanceTick();
        game.AdvanceTick();
        int id = game.CreateCultivationPlan(Request()).Id;
        PlotSnapshot current = game.GetPlot(farm);
        if (game.ApplyCultivationPlan(id, new[] { farm, new Vector2I(0, 0) }) == null ||
            game.GetFarmCultivation(farm).PlanId != null ||
            game.ApplyCultivationPlan(999, new[] { farm }) == null ||
            game.ApplyCultivationPlan(id, Array.Empty<Vector2I>()) == null ||
            game.ApplyCultivationPlan(id, new[] { new Vector2I(-1, 0) }) == null ||
            game.ApplyCultivationPlan(id, new[] { farm, farm + Vector2I.One, other }) != null ||
            game.GetCultivationPlans()[0].ReferencingFarms != 2 || game.GetPlot(farm) != current)
            return Fail("批量应用未完整预检、去重或保留当前轮");
        game.SetPaused(true);
        var before = game.GetFarmCultivation(farm);
        game.GetCultivationPlans();
        game.CheckCultivationPlan(Request());
        game.AdvanceTick();
        if (before != game.GetFarmCultivation(farm) || game.GetPlot(farm) != current)
            return Fail("查询或暂停推进耕作计划");
        if (game.PrepareFarmCrop(new Vector2I(-1, 0), CropKind.Corn) == null ||
            game.PrepareFarmCrop(farm, (CropKind)99) == null ||
            game.PrepareFarmCrop(new Vector2I(0, 0), CropKind.Corn) == null ||
            game.PrepareFarmCrop(farm, CropKind.Corn) != null || game.GetPlot(farm) != current ||
            game.GetFarmCultivation(farm).PlanId != null ||
            game.GetFarmCultivation(farm).PreparedCrop != CropKind.Corn ||
            game.GetFarmCultivation(other).PlanId != id)
            return Fail("手动预备没有只接管本田并保留当前轮");
        game.SetPaused(false);
        for (int second = 0; second < 206; second++)
            game.AdvanceTick();
        if (game.GetPlot(farm).CropKind != CropKind.Corn ||
            game.GetPlot(farm).Crop == CropStage.None || game.GetFarmCultivation(farm).PreparedCrop != null)
            return Fail("手动预备收获后未无额外等待接续并恢复自动复种");
        if (game.SetFarmCrop(other, CropKind.Wheat) != null || game.GetFarmCultivation(other).PlanId != null ||
            game.GetCultivationPlans()[0].ReferencingFarms != 0)
            return Fail("手动立即改种未解除共享引用");
        return true;
    }

    private static bool CheckSeasonRescue()
    {
        // 春末供水后恰剩约5%的生长周期，换季补救沿用经营收获入库且只收一次。
        var game = new FarmGame(12345, 4123);
        foreach (var space in game.GetBuildingSpaces())
            game.RemoveBuilding(space.AnchorCell);
        Vector2I farm = new(189, 189);
        game.BuildFarm(farm);
        game.SetFarmCrop(farm, CropKind.Radish);
        int harvests = 0;
        while (game.Calendar.ElapsedSeconds < 4320)
            harvests += game.AdvanceTick().Harvested;
        if (harvests != 6 || game.GetRawStock(CropKind.Radish) != 6 ||
            game.GetPlot(farm).Crop != CropStage.None)
            return Fail("换季促熟未走原收获入库流程");
        for (int tick = 0; tick < 10; tick++)
            if (game.AdvanceTick().Harvested != 0 || game.GetRawStock(CropKind.Radish) != 6)
                return Fail("换季补救重复入库");
        return true;
    }

    private static bool CheckHarvestPlanDateActivation()
    {
        foreach (CultivationMode mode in Enum.GetValues<CultivationMode>())
        {
            foreach (CropKind target in new[] { CropKind.Wheat, CropKind.Corn })
            {
                // 旧轮干田萝卜恰在春夏边界正常成熟；新计划条从夏季起点开始。
                var game = new FarmGame(12345, 4112);
                foreach (var space in game.GetBuildingSpaces())
                    game.RemoveBuilding(space.AnchorCell);
                Vector2I farm = new(189, 189);
                game.BuildFarm(farm);
                game.SetFarmCrop(farm, CropKind.Radish);
                game.AdvanceTick();
                game.AdvanceTick();
                var request = new CultivationPlanRequest("夏季目标", mode,
                    new[] { new CultivationEntry(1, target, 84) });
                CultivationValidation validation = game.CheckCultivationPlan(request);
                if (!validation.Success || validation.RiskEntryIds.Count != (target == CropKind.Wheat ? 1 : 0))
                    return Fail("夏季禁生目标未允许以风险条保存");
                int id = game.CreateCultivationPlan(request).Id;
                if (game.ApplyCultivationPlan(id, new[] { farm }) != null ||
                    game.GetFarmCultivation(farm).PreparedTimeUnits != 84 * 360)
                    return Fail("春末当前轮未缓存夏季生效目标");
                int totalHarvested = 0;
                while (game.Calendar.ElapsedSeconds < 4319)
                    totalHarvested += game.AdvanceTick().Harvested;
                TickResult boundary = game.AdvanceTick();
                totalHarvested += boundary.Harvested;
                if (game.Calendar.Season != Season.Summer || !boundary.DayAdvanced || boundary.Harvested != 6 ||
                    boundary.WorkerActed || game.GetPlot(farm).Crop != CropStage.None ||
                    game.GetPlot(farm).CropKind != target || totalHarvested != 6 ||
                    game.GetRawStock(CropKind.Radish) != 6 || game.GetRawStock(target) != 0)
                    return Fail("新计划条借旧季工人相位提前播种，或原收成重复入库");
                TickResult next = game.AdvanceTick();
                if (target == CropKind.Wheat)
                {
                    if (next.WorkerActed || game.GetPlot(farm).Crop != CropStage.None ||
                        game.GetFarmDetails(farm).Status != FarmStatus.WrongSeason)
                        return Fail("夏季禁生风险目标仍消耗播种动作");
                }
                else if (!next.WorkerActed || game.GetPlot(farm).Crop != CropStage.Seeded)
                    return Fail("夏季适宜目标未在日期生效后的原工人相位播种");
                game.AdvanceTick();
                if (game.GetRawStock(CropKind.Radish) != 6 || game.GetRawStock(target) != 0 ||
                    (target == CropKind.Corn && game.GetPlot(farm).Crop != CropStage.Growing))
                    return Fail("日期生效目标改变原供水耗时或重复发放旧轮库存");
            }
        }
        return true;
    }

    private static bool CheckRescueWithinPlanBar()
    {
        foreach (CultivationMode mode in Enum.GetValues<CultivationMode>())
        {
            // 第69天的小麦条延伸至第85天；第84天换季促熟时仍在原条内。
            var game = new FarmGame(12345, 3550);
            foreach (var space in game.GetBuildingSpaces())
                game.RemoveBuilding(space.AnchorCell);
            Vector2I farm = new(189, 189);
            game.BuildFarm(farm);
            int id = game.CreateCultivationPlan(new("条内促熟", mode,
                new[] { new CultivationEntry(1, CropKind.Wheat, 69) })).Id;
            if (game.ApplyCultivationPlan(id, new[] { farm }) != null)
                return Fail("条内促熟回归无法应用共享表");
            while (game.Calendar.ElapsedSeconds < 4319)
                if (game.AdvanceTick().Harvested != 0)
                    return Fail("条内促熟回归在季节边界前意外收获");
            TickResult boundary = game.AdvanceTick();
            FarmCultivationSnapshot arrangement = game.GetFarmCultivation(farm);
            if (boundary.Harvested != 2 || boundary.WorkerActed || game.GetRawStock(CropKind.Wheat) != 2 ||
                game.GetPlot(farm).Crop != CropStage.None || !arrangement.IsResting ||
                arrangement.PreparedTimeUnits != (336 + 69) * 360)
                return Fail("条内换季收获后同条被重新启用，或未缓存下一年度目标");
            for (int tick = 0; tick < 10; tick++)
                if (game.AdvanceTick().Harvested != 0 || game.GetRawStock(CropKind.Wheat) != 2 ||
                    !game.GetFarmCultivation(farm).IsResting)
                    return Fail("条内收获后的休耕或未来缓存未保持");
        }
        return true;
    }

    private static bool CheckExecutedEntriesAfterEdit()
    {
        var game = new FarmGame(12345);
        foreach (var space in game.GetBuildingSpaces())
            game.RemoveBuilding(space.AnchorCell);
        Vector2I farm = new(189, 189);
        game.BuildFarm(farm);
        var request = new CultivationPlanRequest("两轮同种", CultivationMode.PrepareNext,
            new[] { new CultivationEntry(1, CropKind.Radish, 0), new CultivationEntry(2, CropKind.Radish, 4) });
        int id = game.CreateCultivationPlan(request).Id;
        game.ApplyCultivationPlan(id, new[] { farm });
        while (game.GetRawStock(CropKind.Radish) < 12 && game.Calendar.ElapsedSeconds < 500)
            game.AdvanceTick();
        if (game.GetRawStock(CropKind.Radish) != 12 || game.GetPlot(farm).Crop != CropStage.None)
            return Fail("编辑执行凭据回归没有真实完成两条各一轮");
        var edited = request with { Entries = new[] { new CultivationEntry(1, CropKind.Radish, 10), new CultivationEntry(2, CropKind.Radish, 4) } };
        if (!game.UpdateCultivationPlan(id, edited).Success || game.ApplyCultivationPlan(id, new[] { farm }) != null)
            return Fail("编辑或同表重应用失败");
        uint nextYearSecondEntry = (uint)GameTimeUnits.RemainingSeconds((336 + 4) * GameTimeUnits.PerDay);
        while (game.Calendar.ElapsedSeconds < nextYearSecondEntry)
        {
            TickResult tick = game.AdvanceTick();
            if (tick.WorkerActed || tick.Harvested != 0 || game.GetPlot(farm).Crop != CropStage.None ||
                game.GetRawStock(CropKind.Radish) != 12)
                return Fail("较早已执行条拖至本年未来日期后重复播种");
        }
        TickResult yearStart = game.AdvanceTick();
        if (!yearStart.WorkerActed || game.GetPlot(farm).Crop != CropStage.Seeded)
            return Fail("保留执行凭据阻止下一年度正常重复");
        while (game.GetRawStock(CropKind.Radish) < 18 && game.Calendar.ElapsedSeconds < nextYearSecondEntry + 210)
            game.AdvanceTick();
        uint nextYearMovedEntry = (uint)GameTimeUnits.RemainingSeconds((336 + 10) * GameTimeUnits.PerDay);
        if (game.GetRawStock(CropKind.Radish) != 18 ||
            game.GetFarmCultivation(farm).PreparedTimeUnits != (336 + 10) * GameTimeUnits.PerDay)
            return Fail("下一年度未预备移动过的早期条");
        while (game.Calendar.ElapsedSeconds < nextYearMovedEntry)
            game.AdvanceTick();
        if (!game.AdvanceTick().WorkerActed || game.GetPlot(farm).Crop != CropStage.Seeded)
            return Fail("早期条在新年度仍被旧执行凭据禁止");
        return true;
    }

    private static bool CheckPreservedRoundDoesNotConsumeEntry()
    {
        var game = new FarmGame(12345);
        foreach (var space in game.GetBuildingSpaces())
            game.RemoveBuilding(space.AnchorCell);
        Vector2I farm = new(189, 189);
        game.BuildFarm(farm);
        game.SetFarmCrop(farm, CropKind.Radish);
        game.AdvanceTick();
        game.AdvanceTick();
        int id = game.CreateCultivationPlan(new("保留外来当前轮", CultivationMode.PrepareNext,
            new[] { new CultivationEntry(1, CropKind.Wheat, 0) })).Id;
        game.ApplyCultivationPlan(id, new[] { farm });
        for (int tick = 0; tick < 206; tick++)
            game.AdvanceTick();
        if (game.GetRawStock(CropKind.Radish) != 6 || game.GetPlot(farm).CropKind != CropKind.Wheat ||
            game.GetPlot(farm).Crop != CropStage.None || !game.AdvanceTick().WorkerActed ||
            game.GetPlot(farm).Crop != CropStage.Seeded)
            return Fail("初次应用保留的手动当前轮被虚认为已播计划条");
        return true;
    }

    private static bool CheckSowBeforeSeasonClearIsConsumed()
    {
        foreach (CultivationMode mode in Enum.GetValues<CultivationMode>())
        {
            var game = new FarmGame(12345, 4319);
            foreach (var space in game.GetBuildingSpaces())
                game.RemoveBuilding(space.AnchorCell);
            Vector2I farm = new(189, 189);
            game.BuildFarm(farm);
            var request = new CultivationPlanRequest("季末实际播种", mode,
                new[] { new CultivationEntry(1, CropKind.Radish, 83) });
            if (!game.CheckCultivationPlan(request).Success || game.CheckCultivationPlan(request).RiskEntryIds.Count != 1)
                return Fail("季末实际播种回归未建立可保存风险条");
            int id = game.CreateCultivationPlan(request).Id;
            game.ApplyCultivationPlan(id, new[] { farm });
            TickResult crossing = game.AdvanceTick();
            if (!crossing.WorkerActed || !crossing.DayAdvanced || game.Calendar.Season != Season.Summer ||
                game.GetPlot(farm).Crop != CropStage.None || game.GetRawStock(CropKind.Radish) != 0 ||
                !game.GetFarmCultivation(farm).IsResting ||
                game.GetFarmCultivation(farm).PreparedTimeUnits != (336 + 83) * GameTimeUnits.PerDay)
                return Fail("季末 Sow 后清除未记录已消费条或未更新中断安排");
            var edited = request with { Entries = new[] { new CultivationEntry(1, CropKind.Radish, 168) } };
            if (!game.UpdateCultivationPlan(id, edited).Success || game.ApplyCultivationPlan(id, new[] { farm }) != null ||
                game.GetFarmCultivation(farm).PreparedTimeUnits != (336 + 168) * GameTimeUnits.PerDay)
                return Fail("季末已播条编辑或同表重应用丢失执行凭据");
            uint nextYearAutumn = (uint)GameTimeUnits.RemainingSeconds((336 + 168) * GameTimeUnits.PerDay);
            while (game.Calendar.ElapsedSeconds < nextYearAutumn)
            {
                TickResult tick = game.AdvanceTick();
                if (tick.WorkerActed || tick.Harvested != 0 || game.GetPlot(farm).Crop != CropStage.None ||
                    game.GetRawStock(CropKind.Radish) != 0)
                    return Fail("季末已播条移动到本年秋季后再次播种");
            }
            if (game.Calendar.Year != 2 || game.Calendar.Season != Season.Autumn ||
                !game.AdvanceTick().WorkerActed || game.GetPlot(farm).Crop != CropStage.Seeded)
                return Fail("季末已播条凭据错误阻止翌年正常执行");
        }
        return true;
    }

    private static bool CheckSeasonEndCache()
    {
        Vector2I farm = new(189, 189);
        foreach (uint start in new[] { 11006u, 6686u, 12000u })
        {
            var game = new FarmGame(12345, start);
            foreach (var space in game.GetBuildingSpaces())
                game.RemoveBuilding(space.AnchorCell);
            game.BuildFarm(farm);
            game.SetFarmCrop(farm, CropKind.Sugarcane);
            while (game.GetPlot(farm).Crop != CropStage.Growing)
                game.AdvanceTick();
            bool crossesAllowedSeason = start == 6686;
            int applyDay = crossesAllowedSeason ? 166 : 250;
            while ((long)game.Calendar.ElapsedSeconds * GameTimeUnits.PerSecond < applyDay * GameTimeUnits.PerDay)
                game.AdvanceTick();
            int radishDay = crossesAllowedSeason ? 166 : 249;
            int wheatDay = crossesAllowedSeason ? 171 : 260;
            int id = game.CreateCultivationPlan(new("季节结局缓存", CultivationMode.PrepareNext,
                new[] { new CultivationEntry(1, CropKind.Radish, radishDay),
                    new CultivationEntry(2, CropKind.Wheat, wheatDay) })).Id;
            game.ApplyCultivationPlan(id, new[] { farm });
            CropKind expected = crossesAllowedSeason ? CropKind.Wheat : CropKind.Radish;
            int expectedDay = crossesAllowedSeason ? wheatDay : radishDay;
            FarmCultivationSnapshot prepared = game.GetFarmCultivation(farm);
            if (prepared.PreparedCrop != expected || prepared.PreparedTimeUnits != expectedDay * GameTimeUnits.PerDay)
                return Fail($"甘蔗 {start} 秒开局的预计结束未区分适季跨季与禁生结局");
            uint boundary = crossesAllowedSeason ? 8640u : 12960u;
            int harvested = 0;
            while (game.Calendar.ElapsedSeconds < boundary)
                harvested += game.AdvanceTick().Harvested;
            if (crossesAllowedSeason)
            {
                if (harvested != 0 || game.GetPlot(farm).Crop != CropStage.Growing ||
                    game.GetFarmCultivation(farm).PreparedCrop != CropKind.Wheat)
                    return Fail("甘蔗夏秋适季边界被预测成补救或清理");
            }
            else
            {
                int expectedHarvest = start == 11006 ? 12 : 0;
                if (harvested != expectedHarvest || game.GetRawStock(CropKind.Sugarcane) != expectedHarvest ||
                    game.GetPlot(farm).CropKind != CropKind.Radish || game.GetPlot(farm).Crop != CropStage.None ||
                    !game.AdvanceTick().WorkerActed || game.GetPlot(farm).Crop != CropStage.Seeded)
                    return Fail("禁生促熟或清理后未按实际结束位置启用仍有效萝卜条，或失败被当成收成");
            }
        }
        return true;
    }

    private static bool CheckRestBufferAndConsecutiveRounds()
    {
        foreach (CultivationMode mode in Enum.GetValues<CultivationMode>())
            foreach (int rounds in new[] { 1, 3 })
            {
                var game = new FarmGame(12345);
                foreach (var space in game.GetBuildingSpaces())
                    game.RemoveBuilding(space.AnchorCell);
                Vector2I farm = new(189, 189);
                game.BuildFarm(farm);
                var entries = new CultivationEntry[rounds];
                for (int round = 0; round < rounds; round++)
                    entries[round] = new(round + 1, CropKind.Radish, round * 4);
                int id = game.CreateCultivationPlan(new("休耕缓冲逐轮", mode, entries)).Id;
                game.ApplyCultivationPlan(id, new[] { farm });
                while ((long)game.Calendar.ElapsedSeconds * GameTimeUnits.PerSecond < 4 * GameTimeUnits.PerDay)
                    game.AdvanceTick(isRaining: true);
                if (game.GetRawStock(CropKind.Radish) != 0 || game.GetPlot(farm).Crop != CropStage.Growing)
                    return Fail("条尾或同种连续起点丢弃了尚未成熟的第一轮");
                while ((long)game.Calendar.ElapsedSeconds * GameTimeUnits.PerSecond < (rounds * 4 + 2) * GameTimeUnits.PerDay)
                    game.AdvanceTick(isRaining: true);
                if (game.GetRawStock(CropKind.Radish) != rounds * 6 || game.GetPlot(farm).Crop != CropStage.None ||
                    !game.GetFarmCultivation(farm).IsResting ||
                    game.GetFarmCultivation(farm).PreparedTimeUnits != 336 * GameTimeUnits.PerDay)
                    return Fail($"{mode} 的 {rounds} 条同种未各完成一轮，或空白缓冲再次播种");
                for (int tick = 0; tick < 60; tick++)
                    if (game.AdvanceTick(isRaining: true).Harvested != 0 || game.GetRawStock(CropKind.Radish) != rounds * 6 ||
                        game.GetPlot(farm).Crop != CropStage.None)
                        return Fail("空白休耕在成熟后复种或重复收获");
            }
        return true;
    }

    private static bool CheckDifferentCropAfterBuffer()
    {
        foreach (CultivationMode mode in Enum.GetValues<CultivationMode>())
        {
            var game = new FarmGame(12345, 155);
            foreach (var space in game.GetBuildingSpaces())
                game.RemoveBuilding(space.AnchorCell);
            Vector2I farm = new(189, 189);
            game.BuildFarm(farm);
            int id = game.CreateCultivationPlan(new("缓冲后异种起点", mode,
                new[] { new CultivationEntry(1, CropKind.Radish, 0),
                    new CultivationEntry(2, CropKind.Wheat, 6) })).Id;
            game.ApplyCultivationPlan(id, new[] { farm });
            while ((long)game.Calendar.ElapsedSeconds * GameTimeUnits.PerSecond < 6 * GameTimeUnits.PerDay)
                game.AdvanceTick(isRaining: true);
            if (game.GetPlot(farm).CropKind != (mode == CultivationMode.Immediate ? CropKind.Wheat : CropKind.Radish) ||
                game.GetRawStock(CropKind.Radish) != 0)
                return Fail("缓冲改变了下一异种起点的立即中断或预备保留语义");
            while ((long)game.Calendar.ElapsedSeconds * GameTimeUnits.PerSecond < 8 * GameTimeUnits.PerDay)
                game.AdvanceTick(isRaining: true);
            if (game.GetRawStock(CropKind.Radish) != (mode == CultivationMode.Immediate ? 0 : 6) ||
                game.GetPlot(farm).CropKind != CropKind.Wheat || game.GetPlot(farm).Crop != CropStage.Growing)
                return Fail("缓冲后的异种实际切换或当前轮收成不符合执行模式");
        }
        return true;
    }

    private static bool Fail(string message)
    {
        GD.PushError(message);
        return false;
    }
}
