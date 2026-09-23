using System;
using System.Globalization;
using System.Text;
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
    private Button _buildProcessorButton = null!;
    private OptionButton _cropOption = null!;
    private OptionButton _processorOption = null!;
    private Button _removeButton = null!;
    private Button _sellButton = null!;
    private Vector2I? _selectedCell;

    public override void _Ready()
    {
        _worldMap = GetNode<WorldMap>("WorldMap");
        _worldMap.SetGame(_game);
        _worldMap.SelectionChanged += OnSelectionChanged;

        _messageLabel = GetNode<Label>("CanvasLayer/StatusPanel/MarginContainer/MessageLabel");
        const string actions = "CanvasLayer/ActionsPanel/MarginContainer/ScrollContainer/Actions/";
        _economyLabel = GetNode<Label>(actions + "EconomyLabel");
        _plotLabel = GetNode<Label>(actions + "PlotLabel");
        _unlockButton = GetNode<Button>(actions + "UnlockButton");
        _buildButton = GetNode<Button>(actions + "BuildButton");
        _buildProcessorButton = GetNode<Button>(actions + "BuildProcessorButton");
        _cropOption = GetNode<OptionButton>(actions + "CropOption");
        _processorOption = GetNode<OptionButton>(actions + "ProcessorOption");
        _removeButton = GetNode<Button>(actions + "RemoveButton");
        _sellButton = GetNode<Button>(actions + "SellButton");

        _unlockButton.Pressed += () => RunSelected(_game.UnlockLand, "土地已解锁");
        _buildButton.Pressed += () => RunSelected(_game.BuildFarm, "农田已建造");
        foreach (CropDefinition crop in FarmGame.Crops)
        {
            _cropOption.AddItem(crop.CropName);
            _processorOption.AddItem(crop.BuildingName);
        }
        _cropOption.ItemSelected += index =>
            RunSelected(cell => _game.SetFarmCrop(cell, (CropKind)index), "农田作物已更换");
        _buildProcessorButton.Pressed += () =>
            RunSelected(cell => _game.BuildProcessor(cell, (CropKind)_processorOption.Selected), "加工场地已建造");
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
        SaleResult sale = _game.SellAll();
        _messageLabel.Text = sale.Quantity == 0
            ? "加工品库存为空"
            : $"卖出 {sale.Quantity} 份加工品，获得 {FormatCoins(sale.RevenueCents)} 金币";
        Refresh();
    }

    private void OnTick()
    {
        TickResult result = _game.AdvanceTick();
        if (result.DayAdvanced)
        {
            _messageLabel.Text = $"进入第 {_game.CurrentDay} 天，面粉售价 {FormatCoins(_game.CurrentFlourPriceCents)} 金币";
        }
        else if (result.Harvested > 0 || result.Produced > 0)
        {
            _messageLabel.Text = $"本 tick：收获作物 {result.Harvested}，产出加工品 {result.Produced}";
        }
        if (result.WorkerActed || result.Harvested > 0 || result.Produced > 0)
            _worldMap.QueueRedraw();
        Refresh();
    }

    private void Refresh()
    {
        string priceChange = _game.CurrentDay == 1
            ? "基准价"
            : $"{FormatPercent(_game.DailyPriceChangePercent)}%（{DescribePriceChange(_game.DailyPriceChangePercent)}）";
        var economy = new StringBuilder();
        economy.AppendLine($"第 {_game.CurrentDay} 天    面粉售价：{FormatCoins(_game.CurrentFlourPriceCents)} 金币");
        economy.AppendLine($"今日涨跌：{priceChange}");
        economy.AppendLine($"金币：{FormatCoins(_game.MoneyCents)}");
        foreach (CropDefinition crop in FarmGame.Crops)
            economy.AppendLine($"{crop.CropName}：{_game.GetRawStock(crop.Kind)} → {crop.ProductName}：{_game.GetProductStock(crop.Kind)}");
        economy.AppendLine("当日加工品售价：");
        foreach (CropDefinition crop in FarmGame.Crops)
            economy.AppendLine($"{crop.ProductName} {FormatCoins(_game.GetProductPriceCents(crop.Kind))}");
        economy.AppendLine($"免费土地：{_game.FreeLandGrants}");
        economy.Append("工人：1（自动播种、浇水）");
        _economyLabel.Text = economy.ToString();
        _unlockButton.Text = _game.FreeLandGrants > 0
            ? "解锁土地（免费）"
            : $"解锁土地（{FormatCoins(FarmGame.LandCostCents)} 金币）";
        bool hasProducts = false;
        foreach (CropDefinition crop in FarmGame.Crops)
            hasProducts |= _game.GetProductStock(crop.Kind) > 0;
        _sellButton.Disabled = !hasProducts;

        if (_selectedCell is not Vector2I cell)
        {
            _plotLabel.Text = "请选择一块土地";
            _unlockButton.Disabled = true;
            _buildButton.Disabled = true;
            _buildProcessorButton.Disabled = true;
            _cropOption.Disabled = true;
            _processorOption.Disabled = true;
            _removeButton.Disabled = true;
            return;
        }

        PlotSnapshot plot = _game.GetPlot(cell);
        CropDefinition selectedCrop = FarmGame.GetCrop(plot.CropKind);
        string state = !plot.IsUnlocked ? "未解锁" : plot.Building switch
        {
            BuildingKind.None => "空地",
            BuildingKind.Processor => plot.RemainingTicks > 0
                ? $"{selectedCrop.BuildingName}：加工中，剩余 {plot.RemainingTicks} tick"
                : $"{selectedCrop.BuildingName}：等待{selectedCrop.CropName}",
            _ => plot.Crop switch
            {
                CropStage.Seeded => $"农田（{selectedCrop.CropName}）：待浇水",
                CropStage.Growing => $"农田（{selectedCrop.CropName}）：生长中，剩余 {plot.RemainingTicks} tick",
                _ => $"农田（{selectedCrop.CropName}）：空闲",
            },
        };
        _plotLabel.Text = $"土地 {cell.X}, {cell.Y}\n{state}";
        _unlockButton.Disabled = plot.IsUnlocked || (_game.FreeLandGrants == 0 && _game.MoneyCents < FarmGame.LandCostCents);
        _buildButton.Disabled = !plot.IsUnlocked || plot.Building != BuildingKind.None;
        _buildProcessorButton.Disabled = !plot.IsUnlocked || plot.Building != BuildingKind.None;
        _cropOption.Disabled = plot.Building != BuildingKind.Farm;
        _processorOption.Disabled = !plot.IsUnlocked || plot.Building != BuildingKind.None;
        if (plot.Building == BuildingKind.Farm)
            _cropOption.Select((int)plot.CropKind);
        _removeButton.Disabled = plot.Building == BuildingKind.None;
    }

    private static string FormatCoins(int cents)
    {
        return (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static string DescribePriceChange(double percentage)
    {
        double absolute = Math.Abs(percentage);
        if (absolute < 5.0)
            return "小幅";
        if (absolute < 15.0)
            return "中幅";
        return "大幅";
    }

    private static string FormatPercent(double percentage)
    {
        return percentage.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);
    }
}
