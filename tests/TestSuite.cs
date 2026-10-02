using Godot;

public partial class TestSuite : Node
{
    public override async void _Ready()
    {
        GD.Print("单元测试：农田、加工与交易");
        bool farmGamePassed = TestFarmGame.RunChecks();
        GD.Print("单元测试：资源与作物定义");
        bool resourcesPassed = TestResources.RunChecks();
        GD.Print("单元测试：土地、农田与加工状态边界");
        bool productionStatePassed = TestProductionState.RunChecks();
        GD.Print("单元测试：完整设施空间、子格映射与放置几何");
        bool landPassed = TestLandOccupancy.RunChecks();
        GD.Print("单元测试：工人移动、完整动作与多人调度");
        bool workersPassed = TestWorkerScheduler.RunChecks();
        GD.Print("单元测试：市场价格与换日");
        bool marketPassed = TestMarketRules.RunChecks();
        GD.Print("单元测试：独立商品报价、节日排期与消息");
        bool marketQuotesPassed = TestMarketQuotes.RunChecks();
        GD.Print("单元测试：即时交易、容量与公共库存守恒");
        bool tradingPassed = TestTradingService.RunChecks();
        GD.Print("单元测试：独立日历换算");
        bool calendarPassed = TestGameCalendar.RunChecks();
        GD.Print("单元测试：等距地图坐标");
        bool worldMapPassed = TestWorldMap.RunChecks();
        GD.Print("集成测试：镜头输入与地图选择");
        bool cameraPassed = TestCameraInteraction.RunChecks(this);
        cameraPassed = await TestCameraInteraction.RunResizeChecksAsync(this) && cameraPassed;
        GD.Print("集成测试：NPC 动画与角色切换");
        bool npcPassed = TestNpcPreview.RunChecks(this);
        GD.Print("端到端测试：主场景经营流程");
        bool coreLoopPassed = TestCoreLoop.RunChecks(this);
        GD.Print("性能测试：满地图 headless 负载");
        bool loadPassed = TestFullWorldLoad.RunChecks();
        GD.Print("端到端测试：库存编辑真实布局与滚动");
        bool inventoryLayoutPassed = await TestCoreLoop.RunLayoutChecks(this);
        GD.Print("集成测试：工人经营快照与主地图表现");
        bool workerPresentationPassed = await TestWorkerPresentation.RunChecksAsync(this);
        GD.Print("集成测试：道路灰色地图、分块缓存与选择");
        bool roadMapPassed = await TestRoadMap.RunChecksAsync(this);
        bool passed = farmGamePassed && resourcesPassed && productionStatePassed && landPassed && marketPassed && marketQuotesPassed && tradingPassed && calendarPassed && worldMapPassed && cameraPassed && npcPassed &&
            coreLoopPassed && loadPassed && inventoryLayoutPassed && workersPassed && workerPresentationPassed && roadMapPassed;

        if (passed)
            GD.Print("全部自动化测试通过");
        GetTree().Quit(passed ? 0 : 1);
    }
}
