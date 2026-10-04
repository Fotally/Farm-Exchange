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
    private readonly Label _hint;
    private readonly Dictionary<BuildingKind, Button> _tabs = new();
    private BuildingKind _category = BuildingKind.None;

    public event Action<BuildingKind, CropKind>? SelectionRequested;

    public BuildCatalogWindow() : base("BuildWindow", "建造目录", new Vector2(255, 108),
        new Vector2(730, 510))
    {
        Body.AddChild(MakeLabel("选择设施，在地图空位连续建造", 17, Ink));
        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 8);
        Body.AddChild(tabs);
        AddTab(tabs, "全部", "AllBuildingsTab", BuildingKind.None, 78);
        AddTab(tabs, "农田", "FarmTab", BuildingKind.Farm, 85);
        AddTab(tabs, "加工场地", "ProcessorTab", BuildingKind.Processor, 116);
        AddTab(tabs, "道路", "RoadTab", BuildingKind.Road, 85);
        _search = new LineEdit { Name = "BuildSearch", PlaceholderText = "搜索设施名称", CustomMinimumSize = new Vector2(0, 35) };
        _search.TextChanged += _ => RefreshCards();
        Body.AddChild(_search);
        var scroll = new ScrollContainer
        {
            Name = "BuildScroll",
            CustomMinimumSize = new Vector2(0, 235),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        Body.AddChild(scroll);
        _cards = new GridContainer { Name = "BuildCards", Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _cards.AddThemeConstantOverride("h_separation", 8);
        _cards.AddThemeConstantOverride("v_separation", 8);
        scroll.AddChild(_cards);
        _farmCard = MakeSecondaryButton("", 194, 110);
        _farmCard.Name = "FarmCard";
        _farmCard.Pressed += () => SelectionRequested?.Invoke(BuildingKind.Farm, CropKind.Wheat);
        _cards.AddChild(_farmCard);
        _roadCard = MakeSecondaryButton("", 194, 110);
        _roadCard.Name = "RoadCard";
        _roadCard.Pressed += () => SelectionRequested?.Invoke(BuildingKind.Road, default);
        _cards.AddChild(_roadCard);
        foreach (CropDefinition crop in FarmGame.Crops)
        {
            CropKind kind = crop.Kind;
            Button card = MakeSecondaryButton("", 194, 110);
            card.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            card.Name = $"ProcessorCard{kind}";
            card.Pressed += () => SelectionRequested?.Invoke(BuildingKind.Processor, kind);
            _processorCards.Add(kind, card);
            _cards.AddChild(card);
        }
        _noMatches = MakeLabel("没有匹配的设施，试试其他名称", 14, Ink);
        _cards.AddChild(_noMatches);
        _hint = MakeLabel("", 12, Ink);
        _hint.Name = "BuildCatalogHint";
        Body.AddChild(_hint);
        RefreshCards();
    }

    private void AddTab(HBoxContainer tabs, string title, string name, BuildingKind kind, float width)
    {
        Button tab = MakeSecondaryButton(title, width, 36);
        tab.Name = name;
        tab.Pressed += () => { _category = kind; RefreshCards(); };
        tabs.AddChild(tab);
        _tabs.Add(kind, tab);
    }

    public void RefreshCards()
    {
        _hint.Text = $"农田与加工场地 {FormatCoins(FarmGame.GetBuildingCostCents(BuildingKind.Farm))} / 座 · " +
            $"道路 {FormatCoins(FarmGame.GetBuildingCostCents(BuildingKind.Road))} / 格\n" +
            "选择后点击空位连续建造；右键、Esc 或取消退出。";
        foreach (var tab in _tabs)
            tab.Value.AddThemeStyleboxOverride("normal", Style(tab.Key == _category ? Gold : Paper, 0));
        _farmCard.Visible = (_category == BuildingKind.None || _category == BuildingKind.Farm) &&
            "农田".Contains(_search.Text, StringComparison.OrdinalIgnoreCase);
        _roadCard.Visible = (_category == BuildingKind.None || _category == BuildingKind.Road) &&
            "道路".Contains(_search.Text, StringComparison.OrdinalIgnoreCase);
        _farmCard.Text = $"农田 · 3×3\n工人自动播种、浇水\n建造费 {FormatCoins(FarmGame.GetBuildingCostCents(BuildingKind.Farm))} 金币";
        _roadCard.Text = $"道路 · 1×1\n用于布局与外观\n建造费 {FormatCoins(FarmGame.GetBuildingCostCents(BuildingKind.Road))} 金币/格";
        int count = (_farmCard.Visible ? 1 : 0) + (_roadCard.Visible ? 1 : 0);
        foreach (CropDefinition crop in FarmGame.Crops)
        {
            Button card = _processorCards[crop.Kind];
            card.Text = $"{crop.BuildingName} · 3×3\n{crop.CropName} → {crop.ProductName}\n建造费 {FormatCoins(FarmGame.GetBuildingCostCents(BuildingKind.Processor))} 金币";
            card.Visible = (_category == BuildingKind.None || _category == BuildingKind.Processor) &&
                crop.BuildingName.Contains(_search.Text, StringComparison.OrdinalIgnoreCase);
            if (card.Visible)
                count++;
        }
        _noMatches.Visible = count == 0;
    }
}
