using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using FarmExchange.Development;
using static FarmExchange.UI.UiElements;

namespace FarmExchange.UI;

/**
 * <summary>由配置字段描述生成编辑控件，不依赖具体流程的JSON键名。</summary>
 * <remarks>输入只修改草稿；标量变更不重建控件，数组结构变更保留滚动和仍存在字段的焦点。</remarks>
 */
public partial class ScenarioConfigurationForm : ScrollContainer
{
    private readonly GridContainer _content = new();
    private readonly List<Control> _groupPanels = new();
    private readonly List<(string Path, Control Row)> _rows = new();
    private readonly List<(string Path, Label Error)> _errors = new();
    private ScenarioConfigurationDraft? _draft;
    private bool _locked;
    private string? _lastFocusedName;
    private int _lastCaret;
    internal event Action? Edited;

    /**
     * <summary>创建仅编辑草稿的滚动表单，动态控件继承宿主界面倍率。</summary>
     */
    public ScenarioConfigurationForm()
    {
        Name = "ScenarioConfigurationForm";
        HorizontalScrollMode = ScrollMode.Disabled;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        _content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _content.Name = "ScenarioConfigurationGroups";
        _content.AddThemeConstantOverride("h_separation", 25);
        _content.AddThemeConstantOverride("v_separation", 25);
        var padding = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        padding.AddThemeConstantOverride("margin_right", 12);
        AddChild(padding);
        padding.AddChild(_content);
        Resized += UpdateGroupLayout;
    }

    /**
     * <summary>显示一份独立草稿；同一草稿在步骤切换时继续复用控件。</summary>
     * <param name="draft">配置库拥有的编辑草稿。</param>
     */
    public void BindDraft(ScenarioConfigurationDraft draft)
    {
        if (ReferenceEquals(_draft, draft)) return;
        _draft = draft;
        _lastFocusedName = null;
        Rebuild();
    }

    internal void SetLocked(bool locked)
    {
        _locked = locked;
        UpdateControls(_content);
    }

    internal void RestoreFocus()
    {
        if (_lastFocusedName == null || _locked) return;
        string name = _lastFocusedName;
        int caret = _lastCaret;
        Callable.From(() =>
        {
            if (FindChild(name, true, false) is not Control control || !control.IsVisibleInTree()) return;
            control.GrabFocus();
            if (control is LineEdit input) input.CaretColumn = caret;
        }).CallDeferred();
    }

    private void Rebuild()
    {
        string? focusedName = GetViewport()?.GuiGetFocusOwner()?.Name;
        int caret = GetViewport()?.GuiGetFocusOwner() is LineEdit focused ? focused.CaretColumn : 0;
        int scroll = ScrollVertical;
        ClearChildren(_content);
        _rows.Clear();
        _errors.Clear();
        _groupPanels.Clear();
        int groupIndex = 0;
        foreach (var group in _draft!.Schema.Fields.Where(field => !field.IsReadOnly).GroupBy(field => field.Group)
                     .OrderBy(group => group.Any(ContainsArray) ? 1 : 0))
        {
            var pane = new VBoxContainer
            {
                Name = "ScenarioConfigurationGroup_" + groupIndex++,
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            pane.AddThemeConstantOverride("separation", 11);
            _content.AddChild(pane);
            _groupPanels.Add(pane);
            if (group.Key.Length > 0)
            {
                var header = new PanelContainer();
                StyleBoxFlat style = Style(Colors.Transparent, 0);
                style.SetBorderWidthAll(0);
                style.BorderWidthBottom = 1;
                style.BorderColor = new Color("c6b381");
                style.ContentMarginLeft = style.ContentMarginRight = style.ContentMarginTop = 0;
                style.ContentMarginBottom = 8;
                header.AddThemeStyleboxOverride("panel", style);
                header.AddChild(MakeLabel(group.Key, 16, Ink));
                pane.AddChild(header);
            }
            var fields = new VBoxContainer();
            fields.AddThemeConstantOverride("separation", 12);
            pane.AddChild(fields);
            foreach (ConfigFieldDescriptor field in group)
                fields.AddChild(BuildField(field, field.Name, group.Key, topLevel: true));
        }
        UpdateGroupLayout();
        UpdateVisibility();
        UpdateControls(_content);
        if (IsInsideTree()) Callable.From(() =>
        {
            ScrollVertical = scroll;
            if (focusedName != null && FindChild(focusedName, true, false) is Control next)
            {
                next.GrabFocus();
                if (next is LineEdit text) text.CaretColumn = caret;
            }
        }).CallDeferred();
    }

    private Control BuildField(ConfigFieldDescriptor field, string path, string enclosingHeading = "", bool topLevel = false)
    {
        var card = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        card.AddThemeConstantOverride("separation", 7);
        _rows.Add((path, card));
        card.TooltipText = field.Description;
        if (field.Label != enclosingHeading || field.Unit.Length != 0)
        {
            Label label = MakeLabel(field.Label + (field.Unit.Length == 0 ? "" : " · " + field.Unit), 14, Ink);
            label.TooltipText = field.Description;
            card.AddChild(label);
        }
        if (field.Kind == ConfigFieldKind.Object)
        {
            var grid = new GridContainer { Columns = topLevel ? 1 : 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
            grid.AddThemeConstantOverride("h_separation", 12);
            grid.AddThemeConstantOverride("v_separation", 12);
            card.AddChild(grid);
            foreach (ConfigFieldDescriptor child in field.Children)
                grid.AddChild(BuildField(child, path + "." + child.Name, field.Label));
        }
        else if (field.Kind == ConfigFieldKind.Array)
        {
            int count = (int)_draft!.GetValue(path)!;
            for (int index = 0; index < count; index++)
            {
                int itemIndex = index;
                var item = new PanelContainer();
                item.AddThemeStyleboxOverride("panel", Style(new Color("ede0bf"), 0));
                card.AddChild(item);
                var body = new VBoxContainer();
                WrapMargin(item, 12, 10).AddChild(body);
                var commands = new HBoxContainer();
                body.AddChild(commands);
                Label number = MakeLabel("第" + (index + 1) + "段", 13, Ink);
                number.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                commands.AddChild(number);
                AddArrayButton(commands, "↑", path, index, "Up", () => _draft!.MoveArrayItem(path, itemIndex, itemIndex - 1), index == 0);
                AddArrayButton(commands, "↓", path, index, "Down", () => _draft!.MoveArrayItem(path, itemIndex, itemIndex + 1), index == count - 1);
                AddArrayButton(commands, "删除", path, index, "Remove", () => _draft!.RemoveArrayItem(path, itemIndex), false);
                var fields = new GridContainer { Columns = 1, SizeFlagsHorizontal = SizeFlags.ExpandFill };
                body.AddChild(fields);
                foreach (ConfigFieldDescriptor child in field.Children)
                    fields.AddChild(BuildField(child, $"{path}[{index}].{child.Name}"));
            }
            Button add = MakeSecondaryButton("添加一段", 128, 38);
            add.Name = "ScenarioArrayAdd_" + ControlName(path);
            add.Pressed += () => ChangeArray(() => _draft!.AddArrayItem(path));
            card.AddChild(add);
        }
        else if (field.Kind == ConfigFieldKind.Choice)
        {
            var choice = new OptionButton { Name = FieldName(path), CustomMinimumSize = new Vector2(0, 39), SizeFlagsHorizontal = SizeFlags.ExpandFill };
            ApplyInputStyle(choice);
            string actual = Convert.ToString(_draft!.GetValue(path), CultureInfo.InvariantCulture) ?? "";
            for (int index = 0; index < field.Options.Count; index++)
            {
                choice.AddItem(field.Options[index].Label);
                if (field.Options[index].Value == actual) choice.Selected = index;
            }
            choice.ItemSelected += index => Change(path, field.Options[(int)index].Value);
            RememberFocus(choice);
            card.AddChild(choice);
        }
        else if (field.Kind == ConfigFieldKind.Boolean)
        {
            var check = new CheckBox { Name = FieldName(path), Text = "启用", ButtonPressed = (bool)_draft!.GetValue(path)! };
            check.Toggled += value => Change(path, value ? "true" : "false");
            RememberFocus(check);
            card.AddChild(check);
        }
        else if (field.Kind == ConfigFieldKind.GameDate)
        {
            string[] date = ((string)_draft!.GetValue(path)!).Split('-');
            var row = new HBoxContainer();
            card.AddChild(row);
            var parts = new LineEdit[3];
            for (int index = 0; index < 3; index++)
            {
                parts[index] = new LineEdit
                {
                    Name = FieldName(path) + "_" + index,
                    Text = index < date.Length ? date[index] : "",
                    ExpandToTextLength = false,
                    SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
                    SizeFlagsVertical = SizeFlags.ShrinkCenter,
                    CustomMinimumSize = new Vector2(62, 39)
                };
                ApplyInputStyle(parts[index]);
                parts[index].AddThemeConstantOverride("minimum_character_width", 2);
                row.AddChild(parts[index]);
                RememberFocus(parts[index]);
                Label unit = MakeLabel(new[] { "年", "月", "日" }[index], 14, Muted);
                unit.AutowrapMode = TextServer.AutowrapMode.Off;
                unit.VerticalAlignment = VerticalAlignment.Center;
                row.AddChild(unit);
            }
            foreach (LineEdit part in parts) part.TextChanged += _ => Change(path,
                parts[0].Text + "-" + parts[1].Text + "-" + parts[2].Text);
        }
        else
        {
            var input = new LineEdit
            {
                Name = FieldName(path),
                Text = Convert.ToString(_draft!.GetValue(path), CultureInfo.InvariantCulture) ?? "",
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(120, 39),
                TooltipText = field.Description
            };
            ApplyInputStyle(input);
            input.TextChanged += value => Change(path, value);
            RememberFocus(input);
            card.AddChild(input);
        }
        if (field.Description.Length != 0)
            card.AddChild(MakeLabel(field.Description, 12, Muted));
        Label error = MakeLabel("", 11, new Color("963f2c"));
        error.Visible = false;
        card.AddChild(error);
        _errors.Add((path, error));
        foreach (Node child in card.GetChildren())
            if (child is LineEdit or BaseButton) child.SetMeta("field_read_only", field.IsReadOnly);
        return card;
    }

    private void RememberFocus(Control control)
    {
        control.FocusEntered += () => _lastFocusedName = control.Name;
        control.FocusExited += () =>
        {
            if (control is LineEdit input) _lastCaret = input.CaretColumn;
        };
    }

    private void AddArrayButton(Node parent, string text, string path, int index, string action, Action change, bool disabled)
    {
        Button button = MakeQuietButton(text, 42, 32);
        button.Name = $"ScenarioArray{action}_{ControlName(path)}_{index}";
        button.Disabled = disabled;
        button.SetMeta("array_edge", disabled);
        button.Pressed += () => ChangeArray(change);
        parent.AddChild(button);
    }

    private void Change(string path, string value)
    {
        if (_locked) return;
        _draft!.SetValue(path, value);
        UpdateVisibility();
        Edited?.Invoke();
    }

    private void ChangeArray(Action change)
    {
        if (_locked) return;
        change();
        Rebuild();
        Edited?.Invoke();
    }

    private void UpdateVisibility()
    {
        foreach ((string path, Control row) in _rows)
        {
            bool visible = _draft!.IsVisible(path);
            bool newlyVisible = visible && !row.Visible;
            row.Visible = visible;
            if (newlyVisible && row.FindChild(FieldName(path), true, false) is LineEdit input)
                input.Text = Convert.ToString(_draft.GetValue(path), CultureInfo.InvariantCulture) ?? "";
        }
        IReadOnlyDictionary<string, string> errors = _draft!.Errors;
        foreach ((string path, Label label) in _errors)
        {
            label.Visible = errors.TryGetValue(path, out string? error);
            label.Text = error ?? "";
        }
    }

    private void UpdateControls(Node parent)
    {
        foreach (Node child in parent.GetChildren())
        {
            bool readOnly = child.HasMeta("field_read_only") && (bool)child.GetMeta("field_read_only");
            if (child is LineEdit text) text.Editable = !_locked && !readOnly;
            if (child is BaseButton button)
                button.Disabled = _locked || readOnly || (button.HasMeta("array_edge") && (bool)button.GetMeta("array_edge"));
            UpdateControls(child);
        }
    }

    internal static string ControlName(string path) => path.Replace('.', '_').Replace('[', '_').Replace("]", "");
    internal static string FieldName(string path) => "ScenarioField_" + ControlName(path);

    internal static void ApplyInputStyle(Control input)
    {
        input.AddThemeFontSizeOverride("font_size", 16);
        foreach (string state in new[] { "normal", "hover", "pressed", "focus", "read_only", "disabled" })
        {
            bool disabled = state is "read_only" or "disabled";
            StyleBoxFlat style = Style(new Color(disabled ? "e5d8bb" : "fff7e3"), 0);
            style.BorderColor = state == "focus" ? Mid : new Color("a58c5a");
            style.ContentMarginLeft = style.ContentMarginRight = 10;
            style.ContentMarginTop = style.ContentMarginBottom = 7;
            input.AddThemeStyleboxOverride(state, style);
        }
        foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "font_hover_pressed_color",
                     "font_selected_color", "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_focus_color", "icon_hover_pressed_color" })
            input.AddThemeColorOverride(state, Ink);
        input.AddThemeColorOverride("font_disabled_color", Muted);
        input.AddThemeColorOverride("font_uneditable_color", Muted);
        input.AddThemeColorOverride("icon_disabled_color", Muted);
    }

    private void UpdateGroupLayout()
    {
        if (_groupPanels.Count == 0 || Size.X <= 0) return;
        float scale = UiScaling.OverallScale(this);
        float width = Size.X - ((MarginContainer)_content.GetParent()).GetThemeConstant("margin_right") - GetVScrollBar().GetCombinedMinimumSize().X;
        float gap = _content.GetThemeConstant("h_separation");
        int columns = Math.Min(_groupPanels.Count, Math.Clamp((int)((width + gap) / (320 * scale * UiScaling.FontScale(this) + gap)), 1, 3));
        _content.Columns = columns;
        float totalWeight = columns == 3 ? 3.15f : columns;
        float unitWidth = Math.Max(0, width - gap * (columns - 1)) / totalWeight;
        for (int index = 0; index < _groupPanels.Count; index++)
            _groupPanels[index].CustomMinimumSize = new Vector2(MathF.Floor(unitWidth * (columns == 3 && index % columns == 2 ? 1.15f : 1f)), 0);
    }

    private static bool ContainsArray(ConfigFieldDescriptor field) => field.Kind == ConfigFieldKind.Array || field.Children.Any(ContainsArray);

    public override void _Ready()
    {
        UiScaling.Changed += OnScaleChanged;
        Callable.From(UpdateGroupLayout).CallDeferred();
    }

    public override void _ExitTree() => UiScaling.Changed -= OnScaleChanged;

    private void OnScaleChanged(Control control)
    {
        if (control == this || control.IsAncestorOf(this)) Callable.From(UpdateGroupLayout).CallDeferred();
    }
}
