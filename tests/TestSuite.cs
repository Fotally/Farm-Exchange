using Godot;

public partial class TestSuite : Node
{
    public override async void _Ready()
    {
        GetWindow().Size = new Vector2I(1920, 1080);
        for (int frame = 0; frame < 2; frame++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GD.Print("单元测试：农田、加工与交易");
        bool loggingPassed = TestLogging.RunChecks();
        loggingPassed = TestLoggingFiles.RunChecks() && loggingPassed;
        loggingPassed = TestTradeLogging.RunChecks() && loggingPassed;
        loggingPassed = TestOrderLogging.RunChecks() && loggingPassed;
        loggingPassed = TestMarketLogging.RunChecks() && loggingPassed;
        bool farmGamePassed = TestFarmGame.RunChecks();
        GD.Print("单元测试：共享年度耕作表、日期事件与手动接管");
        bool cultivationPassed = TestCultivationPlanBook.RunChecks();
        GD.Print("单元测试：共享年度表删除、引用解除与当前作物自动复种");
        bool cultivationDeletionPassed = TestCultivationDeletion.RunChecks();
        GD.Print("单元测试：资源与作物定义");
        bool resourcesPassed = TestResources.RunChecks();
        GD.Print("单元测试：土地、农田与加工状态边界");
        bool productionStatePassed = TestProductionState.RunChecks();
        GD.Print("单元测试：完整设施空间、子格映射与放置几何");
        bool landPassed = TestLandOccupancy.RunChecks();
        GD.Print("单元测试：工人移动、完整动作与多人调度");
        bool workersPassed = TestWorkerScheduler.RunChecks();
        GD.Print("单元测试：逐实例真实作业产出与过期失效");
        bool productionResultsPassed = TestProductionResults.RunChecks();
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
        GD.Print("单元测试：经营倍率、暂停半tick与稳定检查点换速");
        bool driverPassed = TestSimulationDriver.RunChecks();
        GD.Print("单元测试：等价批量经营推进");
        bool batchPassed = TestBatchedSimulation.RunChecks();
        GD.Print("单元测试：严格参数化买入加工卖出流程");
        bool scenarioPassed = TestParameterizedScenario.RunChecks();
        GD.Print("单元测试：可扫描配置字段与严格结构校验");
        bool scenarioSchemaPassed = TestScenarioConfigurationSchema.RunChecks();
        GD.Print("单元测试：流程配置库保存、目录移除与文件删除");
        bool scenarioLibraryPassed = TestScenarioConfigurationLibrary.RunChecks();
        GD.Print("端到端测试：点击倍率、暂停半tick与日期卡布局");
        bool rateButtonPassed = await TestSimulationRateButton.RunChecksAsync(this);
        GD.Print("端到端测试：开发窗口持久配置、独立报告及现场暂停改速");
        bool developerWindowPassed = await TestDeveloperToolsWindow.RunChecksAsync(this);
        GD.Print("端到端测试：分类流程分步界面、自动表单与保存删除确认");
        bool developerEditorPassed = await TestDeveloperFlowEditor.RunChecksAsync(this);
        GD.Print("单元测试：等距地图坐标");
        bool worldMapPassed = TestWorldMap.RunChecks();
        GD.Print("集成测试：镜头输入与地图选择");
        bool cameraPassed = TestCameraInteraction.RunChecks(this);
        cameraPassed = await TestCameraInteraction.RunResizeChecksAsync(this) && cameraPassed;
        GD.Print("集成测试：NPC 动画与角色切换");
        bool npcPassed = TestNpcPreview.RunChecks(this);
        GD.Print("集成测试：清亮静态纹理与人物建筑深度遮挡");
        worldMapPassed = await TestWorldArt.RunChecksAsync(this) && worldMapPassed;
        GD.Print("集成测试：设施真实生产动效与产出反馈");
        worldMapPassed = await TestFacilityMotion.RunChecksAsync(this) && worldMapPassed;
        GD.Print("集成测试：纯环境装饰与完整占地清除");
        worldMapPassed = await TestEnvironmentDecorations.RunChecksAsync(this) && worldMapPassed;
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
        GD.Print("端到端测试：共享年度表列表可读性、整表删除与引用解除");
        bool cultivationManagementPassed = await TestCultivationManagement.RunChecksAsync(this);
        bool passed = loggingPassed && productionResultsPassed && driverPassed && batchPassed && scenarioPassed && scenarioSchemaPassed && scenarioLibraryPassed && rateButtonPassed && developerWindowPassed && developerEditorPassed && farmGamePassed && resourcesPassed && productionStatePassed && landPassed && marketPassed && marketQuotesPassed && tradingPassed && calendarPassed && worldMapPassed && cameraPassed && npcPassed &&
            coreLoopPassed && facilitiesPassed && loadPassed && inventoryLayoutPassed && workersPassed && workerPresentationPassed && roadMapPassed && tradeOrdersPassed && tradeOrdersUiPassed && cultivationPassed && cultivationDeletionPassed && cultivationUiPassed && placementPreviewPassed && buildPlacementPassed && visualLayoutPassed && uiScalingPassed && cultivationManagementPassed;

        if (passed)
            GD.Print("全部自动化测试通过");
        GetTree().Quit(passed ? 0 : 1);
    }
}
