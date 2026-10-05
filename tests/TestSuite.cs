using Godot;

public partial class TestSuite : Node
{
    public override async void _Ready()
    {
        GetWindow().Size = new Vector2I(1920, 1080);
        for (int frame = 0; frame < 2; frame++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GD.Print("单元测试：农田、加工与交易");
        bool farmGamePassed = TestFarmGame.RunChecks();
        GD.Print("单元测试：共享年度耕作表、日期事件与手动接管");
        bool cultivationPassed = TestCultivationPlanBook.RunChecks();
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
        GD.Print("单元测试：多因素委托、费用、冻结与经营顺序");
        bool tradeOrdersPassed = TestTradeOrders.RunChecks();
        GD.Print("端到端测试：委托窗口实际命令、草稿与焦点");
        bool tradeOrdersUiPassed = TestTradeOrdersWindow.RunChecks(this);
        GD.Print("端到端测试：年度耕作表拖动、批量应用与手动指令");
        bool cultivationUiPassed = TestCultivationWindow.RunChecks(this);
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
        GD.Print("端到端测试：建造检索、选种信息与设施真实进度");
        bool facilitiesPassed = TestUiFacilities.RunChecks(this);
        GD.Print("性能测试：满地图 headless 负载");
        bool loadPassed = TestFullWorldLoad.RunChecks();
        GD.Print("端到端测试：库存编辑真实布局与滚动");
        bool inventoryLayoutPassed = await TestCoreLoop.RunLayoutChecks(this);
        GD.Print("端到端测试：委托窗口长列表、条件滚动与布局");
        tradeOrdersUiPassed = await TestTradeOrdersWindow.RunLayoutChecks(this) && tradeOrdersUiPassed;
        GD.Print("端到端测试：耕作表时间轴、草稿与窗口布局");
        cultivationUiPassed = await TestCultivationWindow.RunLayoutChecks(this) && cultivationUiPassed;
        GD.Print("集成测试：工人经营快照与主地图表现");
        bool workerPresentationPassed = await TestWorkerPresentation.RunChecksAsync(this);
        GD.Print("集成测试：道路灰色地图、分块缓存与选择");
        bool roadMapPassed = await TestRoadMap.RunChecksAsync(this);
        GD.Print("集成测试：动态占地预览、局部冲突与独立覆盖层");
        bool placementPreviewPassed = await TestPlacementPreview.RunChecksAsync(this);
        GD.Print("端到端测试：连续摆放、统一取消、界面与镜头输入");
        bool buildPlacementPassed = await TestBuildPlacement.RunChecksAsync(this);
        GD.Print("端到端测试：像素田园主界面、主题与窗口边界");
        bool visualLayoutPassed = await TestUiVisualLayout.RunChecksAsync(this);
        GD.Print("端到端测试：整体与字体倍率、动态控件、原生拖放与分辨率像素稳定");
        bool uiScalingPassed = await TestUiScaling.RunChecksAsync(this);
        bool passed = farmGamePassed && resourcesPassed && productionStatePassed && landPassed && marketPassed && marketQuotesPassed && tradingPassed && calendarPassed && worldMapPassed && cameraPassed && npcPassed &&
            coreLoopPassed && facilitiesPassed && loadPassed && inventoryLayoutPassed && workersPassed && workerPresentationPassed && roadMapPassed && tradeOrdersPassed && tradeOrdersUiPassed && cultivationPassed && cultivationUiPassed && placementPreviewPassed && buildPlacementPassed && visualLayoutPassed && uiScalingPassed;

        if (passed)
            GD.Print("全部自动化测试通过");
        GetTree().Quit(passed ? 0 : 1);
    }
}
