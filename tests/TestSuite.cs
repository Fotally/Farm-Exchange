using Godot;

public partial class TestSuite : Node
{
    public override void _Ready()
    {
        GD.Print("规则测试：农田、加工与交易");
        bool farmGamePassed = TestFarmGame.RunChecks();
        GD.Print("规则测试：市场价格与换日");
        bool marketPassed = TestMarketRules.RunChecks();
        GD.Print("场景集成测试：等距地图");
        bool worldMapPassed = TestWorldMap.RunChecks();
        GD.Print("场景集成测试：镜头输入");
        bool cameraPassed = TestCameraInteraction.RunChecks(this);
        GD.Print("核心流程冒烟测试：主场景经营");
        bool mainScenePassed = TestMainScene.RunChecks(this);
        GD.Print("性能压力测试：满地图 headless 逻辑");
        bool stressPassed = TestWorldStress.RunChecks();
        bool passed = farmGamePassed && marketPassed && worldMapPassed && cameraPassed &&
            mainScenePassed && stressPassed;

        if (passed)
            GD.Print("全部自动化场景测试通过");
        GetTree().Quit(passed ? 0 : 1);
    }
}
