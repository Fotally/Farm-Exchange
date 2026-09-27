using System;
using Godot;
using FarmExchange.Gameplay;
using static FarmExchange.UI.UiElements;

namespace FarmExchange.UI;

public partial class BuildCatalogWindow : DraggableWindow
{
    private readonly GridContainer _cards;
    private readonly LineEdit _search;
    private bool _showProcessors;

    public event Action<BuildingKind, CropKind>? SelectionRequested;

    public BuildCatalogWindow() : base("BuildWindow", "建造目录", new Vector2(30, 252),
        new Vector2(680, 340), avoidBottomBar: true)
    {
        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 8);
        Body.AddChild(tabs);
        Button farmTab = MakeButton("♧ 农田", Mid, 100, 40);
        farmTab.Name = "FarmTab";
        farmTab.Pressed += () => { _showProcessors = false; RefreshCards(); };
        tabs.AddChild(farmTab);
        Button processorTab = MakeButton("⚙ 加工场地", Mid, 132, 40);
        processorTab.Name = "ProcessorTab";
        processorTab.Pressed += () => { _showProcessors = true; RefreshCards(); };
        tabs.AddChild(processorTab);
        _search = new LineEdit { Name = "BuildSearch", PlaceholderText = "按名称搜索加工场地" };
        _search.TextChanged += _ => RefreshCards();
        Body.AddChild(_search);
        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        Body.AddChild(scroll);
        _cards = new GridContainer { Name = "BuildCards", Columns = 3 };
        _cards.AddThemeConstantOverride("h_separation", 8);
        _cards.AddThemeConstantOverride("v_separation", 8);
        scroll.AddChild(_cards);
        RefreshCards();
    }

    public void RefreshCards()
    {
        ClearChildren(_cards);
        _search.Visible = _showProcessors;
        if (!_showProcessors)
        {
            Button farm = MakeButton($"♧ 农田\n建造费 {FormatCoins(FarmGame.BuildingCostCents)} 金币", Mid, 194, 75);
            farm.Name = "FarmCard";
            farm.Pressed += () => SelectionRequested?.Invoke(BuildingKind.Farm, CropKind.Wheat);
            _cards.AddChild(farm);
            return;
        }
        int count = 0;
        foreach (CropDefinition crop in FarmGame.Crops)
        {
            if (!crop.BuildingName.Contains(_search.Text, StringComparison.OrdinalIgnoreCase))
                continue;
            CropKind kind = crop.Kind;
            Button card = MakeButton($"{crop.BuildingName}\n{crop.CropName} → {crop.ProductName} · {FormatCoins(FarmGame.BuildingCostCents)} 金币",
                Mid, 194, 75);
            card.Name = $"ProcessorCard{crop.Kind}";
            card.Pressed += () => SelectionRequested?.Invoke(BuildingKind.Processor, kind);
            _cards.AddChild(card);
            count++;
        }
        if (count == 0)
            _cards.AddChild(MakeLabel("没有匹配的加工场地", 14, Ink));
    }
}
