using Godot;
using FarmExchange.Gameplay;
using static FarmExchange.UI.UiElements;

namespace FarmExchange.UI;

public partial class InventoryWindow : DraggableWindow
{
    private readonly Label[] _rows = new Label[FarmGame.Crops.Count];

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
            rows.AddChild(MakeInfoCard("", out Label label));
            _rows[(int)crop.Kind] = label;
        }
    }

    public void Refresh(FarmGame game)
    {
        foreach (CropDefinition crop in FarmGame.Crops)
            _rows[(int)crop.Kind].Text =
                $"{crop.CropName}原料  {game.GetRawStock(crop.Kind)}   →   {crop.ProductName}  {game.GetProductStock(crop.Kind)}";
    }
}
