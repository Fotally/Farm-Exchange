using System;
using System.Collections.Generic;
using Godot;
using FarmExchange.Gameplay;
using static FarmExchange.UI.UiElements;

namespace FarmExchange.UI;

public partial class BuildCatalogWindow : DraggableWindow
{
    private readonly GridContainer _cards;
    private readonly LineEdit _search;
    private readonly Button _farmCard;
    private readonly Button _roadCard;
    private readonly Dictionary<CropKind, Button> _processorCards = new();
    private readonly Label _noMatches;
    private BuildingKind _category = BuildingKind.Farm;

    public event Action<BuildingKind, CropKind>? SelectionRequested;

    public BuildCatalogWindow() : base("BuildWindow", "建造目录", new Vector2(30, 252),
        new Vector2(680, 340), avoidBottomBar: true)
    {
        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 8);
        Body.AddChild(tabs);
        Button farmTab = MakeButton("♧ 农田", Mid, 100, 40);
        farmTab.Name = "FarmTab";
        farmTab.Pressed += () => { _category = BuildingKind.Farm; RefreshCards(); };
        tabs.AddChild(farmTab);
        Button processorTab = MakeButton("⚙ 加工场地", Mid, 132, 40);
        processorTab.Name = "ProcessorTab";
        processorTab.Pressed += () => { _category = BuildingKind.Processor; RefreshCards(); };
        tabs.AddChild(processorTab);
        Button roadTab = MakeButton("道路", Mid, 100, 40);
        roadTab.Name = "RoadTab";
        roadTab.Pressed += () => { _category = BuildingKind.Road; RefreshCards(); };
        tabs.AddChild(roadTab);
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
        _farmCard = MakeButton("", Mid, 194, 75);
        _farmCard.Name = "FarmCard";
        _farmCard.Pressed += () => SelectionRequested?.Invoke(BuildingKind.Farm, CropKind.Wheat);
        _cards.AddChild(_farmCard);
        _roadCard = MakeButton("", new Color(0.44f, 0.44f, 0.44f), 194, 75);
        _roadCard.Name = "RoadCard";
        _roadCard.Pressed += () => SelectionRequested?.Invoke(BuildingKind.Road, default);
        _cards.AddChild(_roadCard);
        foreach (CropDefinition crop in FarmGame.Crops)
        {
            CropKind kind = crop.Kind;
            Button card = MakeButton("", Mid, 194, 75);
            card.Name = $"ProcessorCard{kind}";
            card.Pressed += () => SelectionRequested?.Invoke(BuildingKind.Processor, kind);
            _processorCards.Add(kind, card);
            _cards.AddChild(card);
        }
        _noMatches = MakeLabel("没有匹配的加工场地", 14, Ink);
        _cards.AddChild(_noMatches);
        RefreshCards();
    }

    public void RefreshCards()
    {
        _search.Visible = _category == BuildingKind.Processor;
        _farmCard.Visible = _category == BuildingKind.Farm;
        _roadCard.Visible = _category == BuildingKind.Road;
        _farmCard.Text = $"♧ 农田\n建造费 {FormatCoins(FarmGame.GetBuildingCostCents(BuildingKind.Farm))} 金币";
        _roadCard.Text = $"道路\n建造费 {FormatCoins(FarmGame.GetBuildingCostCents(BuildingKind.Road))} 金币/格";
        int count = 0;
        foreach (CropDefinition crop in FarmGame.Crops)
        {
            Button card = _processorCards[crop.Kind];
            card.Text = $"{crop.BuildingName}\n{crop.CropName} → {crop.ProductName} · {FormatCoins(FarmGame.GetBuildingCostCents(BuildingKind.Processor))} 金币";
            card.Visible = _category == BuildingKind.Processor &&
                crop.BuildingName.Contains(_search.Text, StringComparison.OrdinalIgnoreCase);
            if (card.Visible)
                count++;
        }
        _noMatches.Visible = _category == BuildingKind.Processor && count == 0;
    }
}
