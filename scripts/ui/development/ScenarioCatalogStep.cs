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
    private readonly OptionButton _category;
    private readonly GridContainer _cards;
    private IReadOnlyList<ScenarioFlowDefinition> _flows = Array.Empty<ScenarioFlowDefinition>();
    private string? _selected;
    private bool _locked;
    internal event Action<string>? Selected;
    internal event Action<string>? DeleteRequested;
    internal event Action? ContinueRequested;
    internal Button ContinueButton { get; }

    internal ScenarioCatalogStep()
    {
        Name = "ScenarioCatalogStep";
        SizeFlagsVertical = SizeFlags.ExpandFill;
        var content = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        AddChild(content);
        var searchRow = new VBoxContainer { CustomMinimumSize = new Vector2(250, 0) };
        content.AddChild(searchRow);
        searchRow.AddChild(MakeLabel("流程目录", 15, Ink));
        _search = new LineEdit { Name = "ScenarioFlowSearch", PlaceholderText = "搜索流程名称或简介", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _search.TextChanged += _ => RenderCards();
        searchRow.AddChild(_search);
        _category = new OptionButton { Name = "ScenarioFlowCategory", CustomMinimumSize = new Vector2(220, 38) };
        _category.ItemSelected += _ => RenderCards();
        searchRow.AddChild(_category);
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        content.AddChild(scroll);
        _cards = new GridContainer { Name = "ScenarioFlowCards", Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(_cards);
        var footer = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        AddChild(footer);
        ContinueButton = MakeButton("继续：编辑配置 →", Mid, 190, 44);
        ContinueButton.Name = "ScenarioCatalogContinueButton";
        ContinueButton.Pressed += () => ContinueRequested?.Invoke();
        footer.AddChild(ContinueButton);
    }

    internal void ShowFlows(IReadOnlyList<ScenarioFlowDefinition> flows, string? selected)
    {
        _flows = flows;
        _selected = selected;
        string category = _category.Selected > 0 ? _category.GetItemText(_category.Selected) : "全部分类";
        _category.Clear();
        _category.AddItem("全部分类");
        foreach (string value in flows.Select(flow => flow.Category).Distinct()) _category.AddItem(value);
        for (int index = 0; index < _category.ItemCount; index++)
            if (_category.GetItemText(index) == category) _category.Select(index);
        RenderCards();
    }

    internal void SetLocked(bool locked)
    {
        _locked = locked;
        _search.Editable = !locked;
        _category.Disabled = locked;
        ContinueButton.Disabled = locked || _selected == null;
        foreach (Button button in _cards.FindChildren("*", "Button", true, false)) button.Disabled = locked;
    }

    private void RenderCards()
    {
        ClearChildren(_cards);
        string category = _category.Selected > 0 ? _category.GetItemText(_category.Selected) : "";
        int count = 0;
        foreach (ScenarioFlowDefinition flow in _flows)
        {
            if (category.Length > 0 && category != flow.Category) continue;
            string search = _search.Text.Trim();
            if (search.Length > 0 && !flow.Name.Contains(search, StringComparison.OrdinalIgnoreCase) &&
                !flow.Description.Contains(search, StringComparison.OrdinalIgnoreCase)) continue;
            count++;
            var card = new PanelContainer { CustomMinimumSize = new Vector2(350, 158), SizeFlagsHorizontal = SizeFlags.ExpandFill };
            card.AddThemeStyleboxOverride("panel", Style(new Color(flow.Id == _selected ? "d6d9ad" : "f1e3c0"), 0));
            _cards.AddChild(card);
            var body = new VBoxContainer();
            WrapMargin(card, 15, 13).AddChild(body);
            Button select = MakeQuietButton("", 0, 84);
            select.Name = "ScenarioSelectFlow_" + flow.Id;
            select.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            select.Pressed += () => Selected?.Invoke(flow.Id);
            body.AddChild(select);
            var text = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            var textMargin = WrapMargin(select, 9, 6);
            textMargin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            textMargin.MouseFilter = MouseFilterEnum.Ignore;
            textMargin.AddChild(text);
            Label title = MakeLabel(flow.Name, 15, Ink);
            title.MouseFilter = MouseFilterEnum.Ignore;
            text.AddChild(title);
            Label description = MakeLabel(flow.Description, 12, Ink);
            description.MouseFilter = MouseFilterEnum.Ignore;
            text.AddChild(description);
            var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
            body.AddChild(row);
            Button trash = ScenarioConfirmationPanel.TrashButton("ScenarioDeleteFlow_" + flow.Id, "移除流程及全部可写配置");
            trash.Pressed += () => DeleteRequested?.Invoke(flow.Id);
            row.AddChild(trash);
        }
        if (count == 0) _cards.AddChild(MakeLabel(_flows.Count == 0 ? "流程目录为空。" : "没有符合分类与搜索条件的流程。", 15, Muted));
        else if (count % 2 == 1)
            _cards.AddChild(new Control
            {
                CustomMinimumSize = new Vector2(350, 0),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                MouseFilter = MouseFilterEnum.Ignore
            });
        SetLocked(_locked);
    }
}
