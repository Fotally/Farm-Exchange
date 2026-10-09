using System;
using System.IO;
using System.Text.Json;
using FarmExchange.Gameplay;
using FarmExchange.Logging;
using FarmExchange.UI;
using Godot;

public partial class TestLoggingLifecycle : Node
{
    private RuntimeLog _logging = null!;
    private string _directory = "";
    private string _mode = "";
    private double _elapsed;
    private bool _ready;

    public override void _EnterTree()
    {
        string[] args = OS.GetCmdlineUserArgs();
        _directory = Path.GetFullPath(args[Array.IndexOf(args, "--output") + 1]);
        _mode = args[Array.IndexOf(args, "--mode") + 1];
        Directory.CreateDirectory(_directory);
        Main.InitialSeed = 130;
        Main.LoggingFactory = () => _logging = RuntimeLog.OpenFile(Path.Combine(_directory, "logs"), true, new(BuildKind: "Debug"));
    }

    public override void _Ready()
    {
        GetWindow().Size = new(1920, 1080);
        GetWindow().Title = "FarmExchange-Logging-Lifecycle";
        using var independent = new FarmGame(130, _logging, GamePurpose.ScenarioIndependent);
        independent.AdvanceTicks(5);
        independent.Dispose();
        independent.Dispose();
        GetNode<Main>("Main").Game.AdvanceTicks(5);
    }

    public override void _Process(double delta)
    {
        _elapsed += delta;
        if (_elapsed < 2) return;
        if (!_ready)
        {
            _ready = true;
            string temporary = Path.Combine(_directory, "ready.tmp");
            File.WriteAllText(temporary, JsonSerializer.Serialize(new { Mode = _mode, EngineProcessId = System.Environment.ProcessId }));
            File.Move(temporary, Path.Combine(_directory, "ready.json"));
        }
        // 主动退出也先等脚本核实实际引擎 PID，防止准备事件与进程退出竞态。
        if (_mode == "quit" && File.Exists(Path.Combine(_directory, "verified.txt"))) GetTree().Quit();
    }

    public override void _ExitTree()
    {
        // Main 子节点先退出且完成正式关闭，此处重复关闭核验幂等。
        _logging?.Dispose();
        Main.LoggingFactory = null;
        Main.InitialSeed = null;
    }
}
