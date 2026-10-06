using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using FarmExchange.Development;
using static FarmExchange.UI.UiElements;

namespace FarmExchange.UI;

internal partial class ScenarioCatalogStep : VBoxContainer
{
    private readonly LineEdit _search;
    private readonly HFlowContainer _categories;
    private readonly GridContainer _cards;
    private IReadOnlyList<ScenarioFlowDefinition> _flows = Array.Empty<ScenarioFlowDefinition>();
    private string? _selected;
    private string _category = "";
    private bool _locked;
    internal event Action<string>? Selected;
    internal event Action<string>? DeleteRequested;
    internal event Action? ContinueRequested;
    internal Button ContinueButton { get; }

    internal ScenarioCatalogStep()
    {
        Name = "ScenarioCatalogStep";
        SizeFlagsVertical = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 14);
        var content = new HBoxContainer { Name = "ScenarioCatalogContent", SizeFlagsVertical = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 28);
        AddChild(content);
        var searchRow = new VBoxContainer { Name = "ScenarioCatalogSidebar", CustomMinimumSize = new Vector2(285, 0) };
        searchRow.AddThemeConstantOverride("separation", 18);
        content.AddChild(searchRow);
        _search = new LineEdit
        {
            Name = "ScenarioFlowSearch",
            PlaceholderText = "搜索流程名称或简介",
            CustomMinimumSize = new Vector2(0, 39),
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        _search.AddThemeFontSizeOverride("font_size", 16);
        var inputStyle = Style(new Color("fff7e3"), 0);
        inputStyle.BorderColor = new Color("a58c5a");
        inputStyle.ContentMarginLeft = inputStyle.ContentMarginRight = 9;
        inputStyle.ContentMarginTop = inputStyle.ContentMarginBottom = 6;
        _search.AddThemeStyleboxOverride("normal", inputStyle);
        _search.TextChanged += _ => RenderCards();
        searchRow.AddChild(_search);
        _categories = new HFlowContainer { Name = "ScenarioFlowCategories", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _categories.AddThemeConstantOverride("h_separation", 7);
        _categories.AddThemeConstantOverride("v_separation", 7);
        searchRow.AddChild(_categories);
        var scroll = new ScrollContainer
        {
            Name = "ScenarioFlowScroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        content.AddChild(scroll);
        _cards = new GridContainer { Name = "ScenarioFlowCards", Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _cards.AddThemeConstantOverride("h_separation", 10);
        _cards.AddThemeConstantOverride("v_separation", 10);
        var flowPadding = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        flowPadding.AddThemeConstantOverride("margin_left", 2);
        flowPadding.AddThemeConstantOverride("margin_right", 6);
        flowPadding.AddThemeConstantOverride("margin_top", 2);
        flowPadding.AddThemeConstantOverride("margin_bottom", 5);
        scroll.AddChild(flowPadding);
        flowPadding.AddChild(_cards);
        _cards.Resized += UpdateCardSizes;
        var separator = new HSeparator { Name = "ScenarioFooterSeparator" };
        separator.AddThemeConstantOverride("separation", 1);
        separator.AddThemeStyleboxOverride("separator", new StyleBoxLine { Color = new Color("bba573"), Thickness = 1 });
        AddChild(separator);
        var footer = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        AddChild(footer);
        ContinueButton = MakeButton("继续配置 →", Mid, 120, 43);
        ContinueButton.AddThemeFontSizeOverride("font_size", 16);
        foreach (string state in new[] { "normal", "hover", "pressed", "disabled" })
        {
            StyleBoxFlat style = Style(state == "hover" ? new Color("78864f") : Mid, 0);
            style.BorderColor = new Color("4c5931");
            style.SetBorderWidthAll(2);
            style.ContentMarginLeft = style.ContentMarginRight = 15;
            style.ContentMarginTop = style.ContentMarginBottom = 9;
            ContinueButton.AddThemeStyleboxOverride(state, style);
        }
        ContinueButton.AddThemeColorOverride("font_color", new Color("fff3d8"));
        ContinueButton.Name = "ScenarioCatalogContinueButton";
        ContinueButton.Pressed += () => ContinueRequested?.Invoke();
        footer.AddChild(ContinueButton);
    }

    internal void ShowFlows(IReadOnlyList<ScenarioFlowDefinition> flows, string? selected)
    {
        _flows = flows;
        _selected = selected;
        if (_category.Length > 0 && !flows.Any(flow => flow.Category == _category)) _category = "";
        RenderCategories();
        RenderCards();
    }

    internal void SetLocked(bool locked)
    {
        _locked = locked;
        _search.Editable = !locked;
        foreach (Button button in _categories.GetChildren()) button.Disabled = locked;
        ContinueButton.Disabled = locked || _selected == null;
        foreach (Button button in _cards.FindChildren("*", "Button", true, false)) button.Disabled = locked;
    }

    private void RenderCards()
    {
        ClearChildren(_cards);
        int count = 0;
        foreach (ScenarioFlowDefinition flow in _flows)
        {
            if (_category.Length > 0 && _category != flow.Category) continue;
            string search = _search.Text.Trim();
            if (search.Length > 0 && !flow.Name.Contains(search, StringComparison.OrdinalIgnoreCase) &&
                !flow.Description.Contains(search, StringComparison.OrdinalIgnoreCase)) continue;
            count++;
            Button select = MakeQuietButton("", 0, 112);
            select.Name = "ScenarioSelectFlow_" + flow.Id;
            select.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            bool selected = flow.Id == _selected;
            select.AddThemeStyleboxOverride("normal", CardStyle(selected ? "e1dbb2" : "f0dfb4", selected));
            select.AddThemeStyleboxOverride("hover", CardStyle(selected ? "e9e3bf" : "f8ebc9", selected));
            select.AddThemeStyleboxOverride("pressed", CardStyle(selected ? "e1dbb2" : "f0dfb4", selected));
            select.AddThemeStyleboxOverride("disabled", CardStyle(selected ? "e1dbb2" : "f0dfb4", selected));
            select.Pressed += () => Selected?.Invoke(flow.Id);
            var text = new VBoxContainer { Name = "ScenarioFlowText", MouseFilter = MouseFilterEnum.Ignore };
            text.AddThemeConstantOverride("separation", 6);
            var textMargin = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
            textMargin.AddThemeConstantOverride("margin_left", 13);
            textMargin.AddThemeConstantOverride("margin_right", 13);
            textMargin.AddThemeConstantOverride("margin_top", 11);
            textMargin.AddThemeConstantOverride("margin_bottom", 38);
            select.AddChild(textMargin);
            textMargin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            textMargin.AddChild(text);
            Label title = MakeLabel(flow.Name, 15, Ink);
            title.MouseFilter = MouseFilterEnum.Ignore;
            text.AddChild(title);
            Label description = MakeLabel(flow.Description, 12, Ink);
            description.MouseFilter = MouseFilterEnum.Ignore;
            text.AddChild(description);
            Button trash = ScenarioConfirmationPanel.TrashButton("ScenarioDeleteFlow_" + flow.Id, "移除流程及全部可写配置");
            trash.CustomMinimumSize = new Vector2(29, 29);
            trash.AddThemeConstantOverride("icon_max_width", 17);
            foreach (string state in new[] { "normal", "hover", "pressed", "disabled" })
            {
                StyleBoxFlat style = Style(new Color("f8edcf"), 0);
                style.BorderColor = new Color("b59a67");
                style.SetBorderWidthAll(2);
                style.ContentMarginLeft = style.ContentMarginRight = 5;
                style.ContentMarginTop = style.ContentMarginBottom = 5;
                trash.AddThemeStyleboxOverride(state, style);
            }
            var trashPosition = new MarginContainer { MouseFilter = MouseFilterEnum.Ignore };
            trashPosition.AddThemeConstantOverride("margin_right", 9);
            trashPosition.AddThemeConstantOverride("margin_bottom", 8);
            select.AddChild(trashPosition);
            trashPosition.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            trash.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
            trash.SizeFlagsVertical = SizeFlags.ShrinkEnd;
            trash.Pressed += () => DeleteRequested?.Invoke(flow.Id);
            trashPosition.AddChild(trash);
            _cards.AddChild(select);
        }
        if (count == 0) _cards.AddChild(MakeLabel(_flows.Count == 0 ? "流程目录为空。" : "没有符合分类与搜索条件的流程。", 15, Muted));
        else if (count % 2 == 1)
            _cards.AddChild(new Control
            {
                CustomMinimumSize = Vector2.Zero,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                MouseFilter = MouseFilterEnum.Ignore
            });
        SetLocked(_locked);
        if (IsInsideTree()) Callable.From(UpdateCardSizes).CallDeferred();
    }

    private static StyleBoxFlat CardStyle(string color, bool selected)
    {
        StyleBoxFlat style = Style(new Color(color), 0);
        style.BorderColor = new Color(selected ? "718043" : "b59a67");
        style.SetBorderWidthAll(2);
        style.BorderWidthLeft = selected ? 4 : 2;
        style.ContentMarginLeft = style.ContentMarginRight = 0;
        style.ContentMarginTop = style.ContentMarginBottom = 0;
        return style;
    }

    private void RenderCategories()
    {
        ClearChildren(_categories);
        string[] categories = new[] { "" }.Concat(_flows.Select(flow => flow.Category).Where(value => value.Length > 0).Distinct()).ToArray();
        for (int index = 0; index < categories.Length; index++)
        {
            string category = categories[index];
            Button button = MakeButton(category.Length == 0 ? "全部" : category,
                category == _category ? Mid : new Color("f0dfb4"), 0, 35);
            button.Name = "ScenarioCategory_" + index;
            button.AddThemeFontSizeOverride("font_size", 14);
            foreach (string state in new[] { "normal", "hover", "pressed", "disabled" })
            {
                StyleBoxFlat style = Style(category == _category ? Mid : new Color("f0dfb4"), 0);
                style.BorderColor = category == _category ? Mid : new Color("b59a67");
                style.SetBorderWidthAll(2);
                style.ContentMarginLeft = style.ContentMarginRight = 13;
                style.ContentMarginTop = style.ContentMarginBottom = 8;
                button.AddThemeStyleboxOverride(state, style);
            }
            var inset = new Panel { MouseFilter = MouseFilterEnum.Ignore };
            StyleBoxFlat insetStyle = Style(Colors.Transparent, 0);
            insetStyle.BorderColor = new Color("fff1d0");
            inset.AddThemeStyleboxOverride("panel", insetStyle);
            button.AddChild(inset);
            inset.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            inset.OffsetLeft = inset.OffsetTop = 2;
            inset.OffsetRight = inset.OffsetBottom = -2;
            button.Pressed += () =>
            {
                _category = category;
                RenderCategories();
                RenderCards();
            };
            _categories.AddChild(button);
        }
    }

    private void UpdateCardSizes()
    {
        foreach (Button card in _cards.GetChildren().OfType<Button>())
        {
            var text = (VBoxContainer)card.FindChild("ScenarioFlowText", true, false);
            float scale = UiScaling.OverallScale(card);
            card.CustomMinimumSize = new Vector2(0, Math.Max(112 * scale, text.GetCombinedMinimumSize().Y + 49 * scale));
        }
    }

    public override void _Ready()
    {
        UiScaling.Changed += OnScaleChanged;
        Callable.From(UpdateCardSizes).CallDeferred();
    }

    public override void _ExitTree() => UiScaling.Changed -= OnScaleChanged;

    private void OnScaleChanged(Control control)
    {
        if (control == this || control.IsAncestorOf(this)) Callable.From(UpdateCardSizes).CallDeferred();
    }
}
