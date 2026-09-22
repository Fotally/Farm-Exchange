using Godot;

public partial class TestSuite : Node
{
    public override void _Ready()
    {
        bool farmGamePassed = TestFarmGame.RunChecks();
        bool worldMapPassed = TestWorldMap.RunChecks();
        bool mainScenePassed = TestMainScene.RunChecks(this);
        bool passed = farmGamePassed && worldMapPassed && mainScenePassed;

        if (passed)
            GD.Print("全部自动化场景测试通过");
        GetTree().Quit(passed ? 0 : 1);
    }
}
