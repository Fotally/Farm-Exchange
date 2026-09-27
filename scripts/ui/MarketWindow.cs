using System;
using Godot;
using FarmExchange.Gameplay;
using static FarmExchange.UI.UiElements;

namespace FarmExchange.UI;

public partial class MarketWindow : DraggableWindow
{
    private readonly Label[] _rows = new Label[FarmGame.Crops.Count];
    private readonly Button[] _rawButtons = new Button[FarmGame.Crops.Count];
    private readonly Button _sellButton;

    public event Action<CropKind>? SellRawRequested;
    public event Action? SellAllRequested;

    public MarketWindow() : base("MarketWindow", "市场", new Vector2(310, 120),
        new Vector2(660, 485))
    {
        var scroll = new ScrollContainer
        {
            Name = "MarketScroll",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        Body.AddChild(scroll);
        var rows = new VBoxContainer { Name = "MarketRows", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        rows.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(rows);
        foreach (CropDefinition crop in FarmGame.Crops)
        {
            CropKind kind = crop.Kind;
            rows.AddChild(MakeInfoCard("", out Label label));
            _rows[(int)kind] = label;
            Button sellRaw = MakeButton($"卖出全部{crop.CropName}原料", Mid, 0, 38);
            sellRaw.Name = $"SellRaw{kind}Button";
            sellRaw.Pressed += () => SellRawRequested?.Invoke(kind);
            rows.AddChild(sellRaw);
            _rawButtons[(int)kind] = sellRaw;
        }
        _sellButton = MakeButton("出售全部加工品", Gold, 0, 43);
        _sellButton.Name = "SellButton";
        _sellButton.Pressed += () => SellAllRequested?.Invoke();
        Body.AddChild(_sellButton);
    }

    public void Refresh(FarmGame game)
    {
        bool hasProducts = false;
        foreach (CropDefinition crop in FarmGame.Crops)
        {
            int raw = game.GetRawStock(crop.Kind);
            int product = game.GetProductStock(crop.Kind);
            hasProducts |= product > 0;
            _rows[(int)crop.Kind].Text =
                $"{crop.CropName}原料  {raw} · 售价 {FormatCoins(game.GetRawPriceCents(crop.Kind))} 金币\n" +
                $"{crop.ProductName}加工品  {product} · 售价 {FormatCoins(game.GetProductPriceCents(crop.Kind))} 金币";
            _rawButtons[(int)crop.Kind].Disabled = raw == 0;
        }
        _sellButton.Disabled = !hasProducts;
    }
}
