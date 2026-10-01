using System;
using System.Globalization;
using Godot;
using FarmExchange.Gameplay;
using static FarmExchange.UI.UiElements;

namespace FarmExchange.UI;

public partial class InventoryWindow : DraggableWindow
{
    private readonly Label[] _rows = new Label[FarmGame.Crops.Count];
    private readonly LineEdit[] _reserveInputs = new LineEdit[FarmGame.Crops.Count];
    private readonly Label[] _messages = new Label[FarmGame.Crops.Count];
    private readonly bool[] _editing = new bool[FarmGame.Crops.Count];

    public event Action<CropKind, int>? RawReserveRequested;

    public InventoryWindow() : base("InventoryWindow", "库存", new Vector2(310, 140),
        new Vector2(660, 455))
    {
        var scroll = new ScrollContainer
        {
            Name = "InventoryScroll",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        Body.AddChild(scroll);
        var rows = new VBoxContainer { Name = "InventoryRows", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        rows.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(rows);
        foreach (CropDefinition crop in FarmGame.Crops)
        {
            int index = (int)crop.Kind;
            var row = new VBoxContainer();
            rows.AddChild(row);
            row.AddChild(MakeInfoCard("", out Label label));
            _rows[index] = label;
            var editRow = new HBoxContainer();
            row.AddChild(editRow);
            Label reserveLabel = MakeLabel("原料保留底线", 14, Ink);
            reserveLabel.Name = $"RawReserve{crop.Kind}Label";
            reserveLabel.AutowrapMode = TextServer.AutowrapMode.Off;
            reserveLabel.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            editRow.AddChild(reserveLabel);
            var input = new LineEdit
            {
                Name = $"RawReserve{crop.Kind}Input",
                CustomMinimumSize = new Vector2(140, 36),
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
                Text = "0",
            };
            input.TextChanged += _ => _editing[index] = true;
            input.TextSubmitted += _ => SubmitReserve(crop.Kind);
            editRow.AddChild(input);
            _reserveInputs[index] = input;
            Button apply = MakeButton("设置", Mid, 70, 36);
            apply.Name = $"SetRawReserve{crop.Kind}Button";
            apply.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            apply.Pressed += () => SubmitReserve(crop.Kind);
            editRow.AddChild(apply);
            Label message = MakeLabel("", 13, Ink);
            row.AddChild(message);
            _messages[index] = message;
        }
    }

    public void Refresh(FarmGame game)
    {
        foreach (CropDefinition crop in FarmGame.Crops)
        {
            int index = (int)crop.Kind;
            _rows[(int)crop.Kind].Text =
                $"{crop.CropName}原料  {game.GetRawStock(crop.Kind)}   →   {crop.ProductName}  {game.GetProductStock(crop.Kind)}\n当前保留底线：{game.GetRawReserve(crop.Kind)}";
            if (!_editing[index] && !_reserveInputs[index].HasFocus())
            {
                _reserveInputs[index].Text = game.GetRawReserve(crop.Kind).ToString(CultureInfo.InvariantCulture);
                _editing[index] = false;
            }
        }
    }

    public void ConfirmRawReserve(CropKind crop, int quantity)
    {
        int index = (int)crop;
        _reserveInputs[index].Text = quantity.ToString(CultureInfo.InvariantCulture);
        _editing[index] = false;
        _messages[index].Text = "已设置，将在下次经营领取时检查";
    }

    private void SubmitReserve(CropKind crop)
    {
        int index = (int)crop;
        if (!int.TryParse(_reserveInputs[index].Text, NumberStyles.None,
                CultureInfo.InvariantCulture, out int quantity))
        {
            _messages[index].Text = "请输入 0 到 2147483647 之间的整数";
            return;
        }
        RawReserveRequested?.Invoke(crop, quantity);
    }
}
