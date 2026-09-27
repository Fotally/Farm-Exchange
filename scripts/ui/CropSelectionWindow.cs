using System;
using Godot;
using FarmExchange.Gameplay;
using static FarmExchange.UI.UiElements;

namespace FarmExchange.UI;

public partial class CropSelectionWindow : DraggableWindow
{
    private readonly Button[] _options = new Button[FarmGame.Crops.Count];

    public event Action<CropKind>? CropRequested;

    public CropSelectionWindow() : base("CropWindow", "选择作物", new Vector2(455, 140),
        new Vector2(430, 390), avoidBottomBar: true)
    {
        var scroll = new ScrollContainer
        {
            Name = "CropScroll",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        Body.AddChild(scroll);
        var cards = new VBoxContainer { Name = "CropCards", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        cards.AddThemeConstantOverride("separation", 7);
        scroll.AddChild(cards);
        foreach (CropDefinition crop in FarmGame.Crops)
        {
            CropKind kind = crop.Kind;
            Button option = MakeButton("", Mid, 390, 57);
            option.Name = $"CropCard{kind}";
            option.Pressed += () => CropRequested?.Invoke(kind);
            cards.AddChild(option);
            _options[(int)kind] = option;
        }
    }

    public void Refresh(FarmGame game)
    {
        foreach (CropDefinition crop in FarmGame.Crops)
            _options[(int)crop.Kind].Text =
                $"{crop.CropName} · 原料售价：{FormatCoins(game.GetRawPriceCents(crop.Kind))} 金币\n" +
                $"原料库存 {game.GetRawStock(crop.Kind)}";
    }
}
