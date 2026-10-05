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
        new Vector2(790, 620))
    {
        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 8);
        Body.AddChild(tabs);
        AddTab(tabs, "全部", "AllBuildingsTab", BuildingKind.None, 78);
        AddTab(tabs, "农田", "FarmTab", BuildingKind.Farm, 85);
        AddTab(tabs, "加工场地", "ProcessorTab", BuildingKind.Processor, 116);
        AddTab(tabs, "道路", "RoadTab", BuildingKind.Road, 85);
        _search = new LineEdit { Name = "BuildSearch", PlaceholderText = "搜索设施名称", CustomMinimumSize = new Vector2(180, 34), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _search.AddThemeFontSizeOverride("font_size", 11);
        _search.TextChanged += _ => RefreshCards();
        tabs.AddChild(_search);
        var scroll = new ScrollContainer
        {
            Name = "BuildScroll",
            CustomMinimumSize = new Vector2(0, 260),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        Body.AddChild(scroll);
        _cards = new GridContainer { Name = "BuildCards", Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _cards.AddThemeConstantOverride("h_separation", 12);
        _cards.AddThemeConstantOverride("v_separation", 12);
        scroll.AddChild(_cards);
        scroll.Resized += () => RefreshColumns(scroll);
        _cards.MinimumSizeChanged += () => RefreshColumns(scroll);
        _farmCard = MakeFacilityCard(BuildingKind.Farm, CropKind.Wheat);
        _farmCard.Name = "FarmCard";
        _farmCard.Pressed += () => SelectionRequested?.Invoke(BuildingKind.Farm, CropKind.Wheat);
        _cards.AddChild(_farmCard);
        foreach (CropDefinition crop in FarmGame.Crops)
        {
            CropKind kind = crop.Kind;
            Button card = MakeFacilityCard(BuildingKind.Processor, kind);
            card.Name = $"ProcessorCard{kind}";
            card.Pressed += () => SelectionRequested?.Invoke(BuildingKind.Processor, kind);
            _processorCards.Add(kind, card);
            _cards.AddChild(card);
        }
        _roadCard = MakeFacilityCard(BuildingKind.Road, default);
        _roadCard.Name = "RoadCard";
        _roadCard.Pressed += () => SelectionRequested?.Invoke(BuildingKind.Road, default);
        _cards.AddChild(_roadCard);
        _noMatches = MakeLabel("没有匹配的设施，试试其他名称", 14, Ink);
        _cards.AddChild(_noMatches);
        _hint = MakeLabel("", 10, Muted);
        _hint.Name = "BuildCatalogHint";
        Body.AddChild(_hint);
        RefreshCards();
    }

    private void AddTab(HBoxContainer tabs, string title, string name, BuildingKind kind, float width)
    {
        Button tab = MakeQuietButton(title, width, 34);
        tab.AddThemeFontSizeOverride("font_size", 11);
        tab.Name = name;
        tab.Pressed += () => { _category = kind; RefreshCards(); };
        tabs.AddChild(tab);
        _tabs.Add(kind, tab);
    }

    private static Button MakeFacilityCard(BuildingKind building, CropKind crop)
    {
        Button card = MakeSecondaryButton("", 220, 200);
        card.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        card.AddThemeFontSizeOverride("font_size", 12);
        card.Icon = FacilityPreview.Texture(building, crop);
        card.ExpandIcon = true;
        card.AddThemeConstantOverride("icon_max_width", 145);
        card.IconAlignment = HorizontalAlignment.Center;
        card.VerticalIconAlignment = VerticalAlignment.Top;
        return card;
    }

    private void RefreshColumns(ScrollContainer scroll)
    {
        if (scroll.Size.X <= 0) return;
        float cardWidth = 0;
        foreach (Node child in _cards.GetChildren())
            if (child is Button card && card.Visible)
                cardWidth = Math.Max(cardWidth, card.GetCombinedMinimumSize().X);
        if (cardWidth <= 0) return;
        int gap = _cards.GetThemeConstant("h_separation");
        float available = scroll.Size.X - scroll.GetVScrollBar().GetCombinedMinimumSize().X;
        int columns = Math.Clamp((int)((available + gap) / (cardWidth + gap)), 1, 3);
        if (_cards.Columns != columns) _cards.Columns = columns;
    }

    public void RefreshCards()
    {
        _hint.Text = $"农田与加工场地 {FormatCoins(FarmGame.GetBuildingCostCents(BuildingKind.Farm))} / 座 · " +
            $"道路 {FormatCoins(FarmGame.GetBuildingCostCents(BuildingKind.Road))} / 格\n" +
            "选择后点击空位连续建造；右键、Esc 或取消退出。";
        foreach (var tab in _tabs)
            tab.Value.AddThemeStyleboxOverride("normal", Style(tab.Key == _category ? new Color("dad5a5") : Paper, 0));
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
