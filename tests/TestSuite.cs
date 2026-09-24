using Godot;

public partial class TestSuite : Node
{
    public override void _Ready()
    {
        GD.Print("规则单元测试：生产、交易、价格及满地图压测状态");
        bool farmGamePassed = TestFarmGame.RunChecks();
        GD.Print("场景组件测试：等距地图与镜头");
        bool worldMapPassed = TestWorldMap.RunChecks();
        GD.Print("场景集成与核心流程测试：主场景交互");
        bool mainScenePassed = TestMainScene.RunChecks(this);
        bool passed = farmGamePassed && worldMapPassed && mainScenePassed;

        if (passed)
            GD.Print("全部自动化场景测试通过");
        GetTree().Quit(passed ? 0 : 1);
    }
}
