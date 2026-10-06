using System;
using System.Threading.Tasks;
using Godot;
using FarmExchange.Time;
using FarmExchange.UI;

public partial class TestSimulationRateButton : Node
{
    public override async void _Ready()
    {
        bool passed = await RunChecksAsync(this);
        GetTree().Quit(passed ? 0 : 1);
    }

    public static async Task<bool> RunChecksAsync(Node parent)
    {
        Vector2I originalSize = parent.GetWindow().Size;
        parent.GetWindow().Size = new Vector2I(1920, 1080);
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
        parent.AddChild(main);
        main.SetProcess(false);
        try
        {
            await Frames(parent);
            Button button = Find<Button>(main, "SimulationRateButton");
            Button pause = Find<Button>(main, "PauseButton");
            Control separator = Find<Control>(main, "CalendarActionSeparator");
            Control panel = Find<Control>(main, "CalendarPanel");
            if (separator.GetGlobalRect().End.X > button.GlobalPosition.X ||
                button.GetGlobalRect().End.X > pause.GlobalPosition.X ||
                !panel.GetGlobalRect().Encloses(button.GetGlobalRect()) ||
                Math.Abs(panel.GetGlobalRect().GetCenter().X - 960) > 1)
                return Fail("日期竖线、倍率、暂停顺序或日期卡居中不正确");
            main.AdvanceSimulation(0.25);
            double progress = main.Driver.Progress;
            main.Game.SetPaused(true);
            uint seconds = main.Game.Calendar.ElapsedSeconds;
            SimulationRateSource? source = null;
            main.Driver.RateChanged += (_, changedSource) => source = changedSource;
            foreach (double expected in new[] { 2.0, 0.5, 1.0 })
            {
                button.EmitSignal(Button.SignalName.Pressed);
                main.AdvanceSimulation(100);
                if (main.Driver.Rate != expected || source != SimulationRateSource.Player ||
                    main.Driver.Progress != progress || !main.Game.IsPaused ||
                    main.Game.Calendar.ElapsedSeconds != seconds ||
                    button.Text != expected.ToString(System.Globalization.CultureInfo.InvariantCulture) + "×")
                    return Fail("公共倍率循环、暂停或未完成tick进度不正确");
            }
            foreach (double rate in new[] { 5.0, 20.0 })
            {
                main.Driver.SetDevelopmentRate(rate, SimulationRateSource.Scenario);
                if (button.Text != rate + "×" || !button.TooltipText.Contains("1×"))
                    return Fail("实际开发倍率或下一次点击提示未同步");
                button.EmitSignal(Button.SignalName.Pressed);
                if (main.Driver.Rate != 1 || main.Driver.Progress != progress || source != SimulationRateSource.Player)
                    return Fail("开发高倍率点击没有回到1倍或丢失进度");
            }
            UiScaling.SetOverallScale(panel, 1.25f);
            UiScaling.SetFontScale(panel, 1.2f);
            await Frames(parent);
            if (!panel.GetGlobalRect().Encloses(button.GetGlobalRect()) ||
                !panel.GetGlobalRect().Encloses(pause.GetGlobalRect()) ||
                Math.Abs(panel.GetGlobalRect().GetCenter().X - 960) > 1)
                return Fail("日期整体与字体倍率变化后动作区溢出或卡片失去居中");
            UiScaling.SetOverallScale(panel, 1f);
            UiScaling.SetFontScale(panel, 1f);
            await Frames(parent);
            Vector2 fixedSize = panel.Size;
            foreach (Vector2I size in new[] { new Vector2I(2560, 1440), new Vector2I(3840, 2160) })
            {
                parent.GetWindow().Size = size;
                await Frames(parent);
                if (!panel.Size.IsEqualApprox(fixedSize) || Math.Abs(panel.GetGlobalRect().GetCenter().X - size.X / 2f) > 1)
                    return Fail("扩大窗口改变日期卡像素尺寸或失去居中");
            }
            return true;
        }
        finally
        {
            main.QueueFree();
            await Frames(parent);
            parent.GetWindow().Size = originalSize;
            await Frames(parent);
        }
    }

    private static async Task Frames(Node parent)
    {
        for (int i = 0; i < 3; i++) await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static T Find<T>(Node parent, string name) where T : Node =>
        parent.FindChild(name, true, false) as T ?? throw new InvalidOperationException("控件缺失：" + name);
    private static bool Fail(string message) { GD.PushError(message); return false; }
}
