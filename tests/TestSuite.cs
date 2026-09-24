using Godot;

public partial class TestSuite : Node
{
    public override void _Ready()
    {
        GD.Print("单元测试：农田、加工与交易");
        bool farmGamePassed = TestFarmGame.RunChecks();
        GD.Print("单元测试：市场价格与换日");
        bool marketPassed = TestMarketRules.RunChecks();
        GD.Print("单元测试：等距地图坐标");
        bool worldMapPassed = TestWorldMap.RunChecks();
        GD.Print("集成测试：镜头输入与地图选择");
        bool cameraPassed = TestCameraInteraction.RunChecks(this);
        GD.Print("端到端测试：主场景经营流程");
        bool coreLoopPassed = TestCoreLoop.RunChecks(this);
        GD.Print("性能测试：满地图 headless 负载");
        bool loadPassed = TestFullWorldLoad.RunChecks();
        bool passed = farmGamePassed && marketPassed && worldMapPassed && cameraPassed &&
            coreLoopPassed && loadPassed;

        if (passed)
            GD.Print("全部自动化测试通过");
        GetTree().Quit(passed ? 0 : 1);
    }
}
