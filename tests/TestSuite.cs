using Godot;

public partial class TestSuite : Node
{
    public override void _Ready()
    {
        GD.Print("单元测试：农田、加工与交易");
        bool farmGamePassed = TestFarmGame.RunChecks();
        GD.Print("单元测试：资源与作物定义");
        bool resourcesPassed = TestResources.RunChecks();
        GD.Print("单元测试：土地、农田与加工状态边界");
        bool productionStatePassed = TestProductionState.RunChecks();
        GD.Print("单元测试：市场价格与换日");
        bool marketPassed = TestMarketRules.RunChecks();
        GD.Print("单元测试：独立日历换算");
        bool calendarPassed = TestGameCalendar.RunChecks();
        GD.Print("单元测试：等距地图坐标");
        bool worldMapPassed = TestWorldMap.RunChecks();
        GD.Print("集成测试：镜头输入与地图选择");
        bool cameraPassed = TestCameraInteraction.RunChecks(this);
        GD.Print("集成测试：NPC 动画与角色切换");
        bool npcPassed = TestNpcPreview.RunChecks(this);
        GD.Print("端到端测试：主场景经营流程");
        bool coreLoopPassed = TestCoreLoop.RunChecks(this);
        GD.Print("性能测试：满地图 headless 负载");
        bool loadPassed = TestFullWorldLoad.RunChecks();
        bool passed = farmGamePassed && resourcesPassed && productionStatePassed && marketPassed && calendarPassed && worldMapPassed && cameraPassed && npcPassed &&
            coreLoopPassed && loadPassed;

        if (passed)
            GD.Print("全部自动化测试通过");
        GetTree().Quit(passed ? 0 : 1);
    }
}
