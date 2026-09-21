using System;
using Godot;

public partial class Main : Node2D
{
    private readonly FarmGame _game = new();
    private WorldMap _worldMap = null!;
    private Label _messageLabel = null!;
    private Label _economyLabel = null!;
    private Label _plotLabel = null!;
    private Button _unlockButton = null!;
    private Button _buildButton = null!;
    private Button _buildMillButton = null!;
    private Button _removeButton = null!;
    private Button _sellButton = null!;
    private Vector2I? _selectedCell;

    public override void _Ready()
    {
        _worldMap = GetNode<WorldMap>("WorldMap");
        _worldMap.SetGame(_game);
        _worldMap.SelectionChanged += OnSelectionChanged;

        _messageLabel = GetNode<Label>("CanvasLayer/StatusPanel/MarginContainer/MessageLabel");
        const string actions = "CanvasLayer/ActionsPanel/MarginContainer/Actions/";
        _economyLabel = GetNode<Label>(actions + "EconomyLabel");
        _plotLabel = GetNode<Label>(actions + "PlotLabel");
        _unlockButton = GetNode<Button>(actions + "UnlockButton");
        _buildButton = GetNode<Button>(actions + "BuildButton");
        _buildMillButton = GetNode<Button>(actions + "BuildMillButton");
        _removeButton = GetNode<Button>(actions + "RemoveButton");
        _sellButton = GetNode<Button>(actions + "SellButton");

        _unlockButton.Pressed += () => RunSelected(_game.UnlockLand, "土地已解锁");
        _buildButton.Pressed += () => RunSelected(_game.BuildFarm, "农田已建造");
        _buildMillButton.Pressed += () => RunSelected(_game.BuildMill, "磨坊已建造");
        _removeButton.Pressed += () => RunSelected(_game.RemoveBuilding, "建筑已移除");
        _sellButton.Pressed += SellAll;
        GetNode<Timer>("TickTimer").Timeout += OnTick;
        Refresh();
    }

    private void OnSelectionChanged(Vector2I cell)
    {
        _selectedCell = cell;
        _messageLabel.Text = $"已选择 {cell.X}, {cell.Y}\n左键拖动 · 滚轮缩放 · WASD/方向键移动";
        Refresh();
    }

    private void RunSelected(Func<Vector2I, string?> action, string success)
    {
        if (_selectedCell is not Vector2I cell)
            return;
        string? error = action(cell);
        _messageLabel.Text = error ?? success;
        Refresh();
        _worldMap.QueueRedraw();
    }

    private void SellAll()
    {
        int sold = _game.SellAll();
        _messageLabel.Text = sold == 0 ? "面粉库存为空" : $"卖出 {sold} 份面粉，获得 {sold * FarmGame.FlourSalePrice} 金币";
        Refresh();
    }

    private void OnTick()
    {
        TickResult result = _game.AdvanceTick();
        if (result.WheatHarvested > 0 || result.FlourProduced > 0)
        {
            _messageLabel.Text = $"本 tick：收获小麦 {result.WheatHarvested}，产出面粉 {result.FlourProduced}";
        }
        if (result.WorkerActed || result.WheatHarvested > 0 || result.FlourProduced > 0)
            _worldMap.QueueRedraw();
        Refresh();
    }

    private void Refresh()
    {
        _economyLabel.Text = $"金币：{_game.Money}\n小麦：{_game.WheatStock}    面粉：{_game.FlourStock}\n免费土地：{_game.FreeLandGrants}\n工人：1（自动播种、浇水）";
        _unlockButton.Text = _game.FreeLandGrants > 0
            ? "解锁土地（免费）"
            : $"解锁土地（{FarmGame.LandCost} 金币）";
        _sellButton.Disabled = _game.FlourStock == 0;

        if (_selectedCell is not Vector2I cell)
        {
            _plotLabel.Text = "请选择一块土地";
            _unlockButton.Disabled = true;
            _buildButton.Disabled = true;
            _buildMillButton.Disabled = true;
            _removeButton.Disabled = true;
            return;
        }

        PlotSnapshot plot = _game.GetPlot(cell);
        string state = !plot.IsUnlocked ? "未解锁" : plot.Building switch
        {
            BuildingKind.None => "空地",
            BuildingKind.Mill => plot.RemainingTicks > 0
                ? $"磨坊：加工中，剩余 {plot.RemainingTicks} tick"
                : "磨坊：等待小麦",
            _ => plot.Crop switch
            {
                CropStage.Seeded => "农田：待浇水",
                CropStage.Growing => $"农田：生长中，剩余 {plot.RemainingTicks} tick",
                _ => "农田：空闲",
            },
        };
        _plotLabel.Text = $"土地 {cell.X}, {cell.Y}\n{state}";
        _unlockButton.Disabled = plot.IsUnlocked || (_game.FreeLandGrants == 0 && _game.Money < FarmGame.LandCost);
        _buildButton.Disabled = !plot.IsUnlocked || plot.Building != BuildingKind.None;
        _buildMillButton.Disabled = !plot.IsUnlocked || plot.Building != BuildingKind.None;
        _removeButton.Disabled = plot.Building == BuildingKind.None;
    }
}
