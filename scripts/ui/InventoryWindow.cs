using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Inventory;
using FarmExchange.Market;
using static FarmExchange.UI.UiElements;

namespace FarmExchange.UI;

public partial class InventoryWindow : DraggableWindow
{
    private readonly Dictionary<CommodityId, (Control Root, Label Stock)> _rows = new();
    private readonly LineEdit _search;
    private readonly Label _empty;
    private int _filter;
    private readonly LineEdit[] _reserveInputs = new LineEdit[FarmGame.Crops.Count];
    private readonly Label[] _messages = new Label[FarmGame.Crops.Count];
    private readonly bool[] _editing = new bool[FarmGame.Crops.Count];

    public event Action<CropKind, int>? RawReserveRequested;

    public InventoryWindow() : base("InventoryWindow", "库存 · 原料与加工品", new Vector2(310, 104),
        new Vector2(660, 510))
    {
        Label help = MakeLabel("总量含冻结；加工仅领取超过保留底线的可用原料。", 13, Ink);
        help.AutowrapMode = TextServer.AutowrapMode.Off;
        Body.AddChild(help);
        var filters = new HBoxContainer();
        filters.AddThemeConstantOverride("separation", 8);
        Body.AddChild(filters);
        var group = new ButtonGroup();
        string[] names = { "全部商品", "原料", "加工品" };
        for (int index = 0; index < names.Length; index++)
        {
            int filter = index;
            Button button = MakeSecondaryButton(names[index], 82, 34);
            button.Name = $"InventoryFilter{index}Button";
            button.ToggleMode = true;
            button.ButtonGroup = group;
            button.ButtonPressed = index == 0;
            button.Pressed += () => { _filter = filter; FilterRows(); };
            filters.AddChild(button);
        }
        _search = new LineEdit
        {
            Name = "InventorySearchInput",
            PlaceholderText = "搜索商品名称…",
            CustomMinimumSize = new Vector2(180, 34),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        _search.TextChanged += _ => FilterRows();
        filters.AddChild(_search);
        var scroll = new ScrollContainer
        {
            Name = "InventoryScroll",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            CustomMinimumSize = new Vector2(0, 180),
        };
        Body.AddChild(scroll);
        var content = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("margin_right", 14);
        scroll.AddChild(content);
        var rows = new VBoxContainer { Name = "InventoryRows", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        rows.AddThemeConstantOverride("separation", 8);
        content.AddChild(rows);
        foreach (CommodityDefinition commodity in CommodityCatalog.All)
        {
            CommodityId id = commodity.Id;
            CropDefinition crop = FarmGame.GetCrop(id.Crop);
            int index = (int)crop.Kind;
            var row = new VBoxContainer { Name = $"Inventory{id.Crop}{id.Kind}Row" };
            row.AddThemeConstantOverride("separation", 6);
            rows.AddChild(row);
            var heading = new HBoxContainer();
            row.AddChild(heading);
            Label title = MakeLabel((id.Kind == CommodityKind.Raw ? crop.CropName : crop.ProductName) +
                (id.Kind == CommodityKind.Raw ? " · 原料" : " · 加工品"), 15, Ink);
            title.AutowrapMode = TextServer.AutowrapMode.Off;
            title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            heading.AddChild(title);
            Label stock = MakeLabel("", 13, Ink);
            stock.Name = $"Inventory{id.Crop}{id.Kind}Stock";
            stock.AutowrapMode = TextServer.AutowrapMode.Off;
            heading.AddChild(stock);
            _rows.Add(id, (row, stock));
            if (id.Kind == CommodityKind.Product)
            {
                row.AddChild(new HSeparator());
                continue;
            }
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
            Button apply = MakeSecondaryButton("设置底线", 96, 36);
            apply.Name = $"SetRawReserve{crop.Kind}Button";
            apply.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            apply.Pressed += () => SubmitReserve(crop.Kind);
            editRow.AddChild(apply);
            Label message = MakeLabel("", 13, Ink);
            row.AddChild(message);
            _messages[index] = message;
            row.AddChild(new HSeparator());
        }
        _empty = MakeLabel("没有匹配的商品，请调整名称或类别。", 13, Ink);
        _empty.Name = "InventoryEmptyMessage";
        _empty.AutowrapMode = TextServer.AutowrapMode.Off;
        _empty.Visible = false;
        rows.AddChild(_empty);
    }

    public void Refresh(FarmGame game)
    {
        foreach (CropDefinition crop in FarmGame.Crops)
        {
            int index = (int)crop.Kind;
            if (!_editing[index] && !_reserveInputs[index].HasFocus())
            {
                _reserveInputs[index].Text = game.GetRawReserve(crop.Kind).ToString(CultureInfo.InvariantCulture);
                _editing[index] = false;
            }
        }
        foreach (CommodityDefinition commodity in CommodityCatalog.All)
        {
            CommodityId id = commodity.Id;
            _rows[id].Stock.Text = $"总 {game.GetStock(id)}   可用 {game.GetAvailableStock(id)}   冻结 {game.GetFrozenStock(id)}";
        }
    }

    private void FilterRows()
    {
        string search = _search.Text.Trim();
        bool hasMatch = false;
        foreach (CommodityDefinition commodity in CommodityCatalog.All)
        {
            bool visible = (_filter == 0 ||
                _filter == 1 && commodity.Id.Kind == CommodityKind.Raw ||
                _filter == 2 && commodity.Id.Kind == CommodityKind.Product) &&
                commodity.Name.Contains(search, StringComparison.OrdinalIgnoreCase);
            _rows[commodity.Id].Root.Visible = visible;
            hasMatch |= visible;
        }
        _empty.Visible = !hasMatch;
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
