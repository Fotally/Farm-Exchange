using System;
using System.Collections.Generic;
using Godot;
using FarmExchange.Development;
using static FarmExchange.UI.UiElements;

namespace FarmExchange.UI;

internal partial class ScenarioEditorStep : VBoxContainer
{
    internal readonly OptionButton Profiles;
    internal readonly ScenarioConfigurationForm Form;
    private readonly Label _status;
    private readonly Label _flowTitle;
    private readonly Label _flowDescription;
    private readonly Label _saveNote;
    private readonly Button _delete;
    private readonly Button _saveAs;
    private readonly Button _overwrite;
    private readonly Button _back;
    private readonly Button _continue;
    private ScenarioConfigurationDraft? _draft;
    internal event Action<int>? ProfileSelected;
    internal event Action? DeleteRequested;
    internal event Action? SaveAsRequested;
    internal event Action? OverwriteRequested;
    internal event Action? BackRequested;
    internal event Action? ContinueRequested;

    internal ScenarioEditorStep()
    {
        Name = "ScenarioEditorStep";
        SizeFlagsVertical = SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 12);
        var intro = new VBoxContainer();
        intro.AddThemeConstantOverride("separation", 0);
        AddChild(intro);
        _flowTitle = MakeLabel("调整配置", 20, Ink);
        _flowTitle.Name = "ScenarioEditorFlowTitle";
        intro.AddChild(_flowTitle);
        _flowDescription = MakeLabel("", 14, Muted);
        _flowDescription.Name = "ScenarioEditorFlowDescription";
        intro.AddChild(_flowDescription);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        AddChild(row);
        Label profileLabel = MakeLabel("配置", 14, Ink);
        profileLabel.AutowrapMode = TextServer.AutowrapMode.Off;
        profileLabel.VerticalAlignment = VerticalAlignment.Center;
        profileLabel.Name = "ScenarioProfileLabel";
        row.AddChild(profileLabel);
        Profiles = new OptionButton
        {
            Name = "ScenarioProfileChoice",
            ClipText = true,
            FitToLongestItem = false,
            CustomMinimumSize = new Vector2(335, 39)
        };
        Profiles.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        ScenarioConfigurationForm.ApplyInputStyle(Profiles);
        Profiles.ItemSelected += index => ProfileSelected?.Invoke((int)index);
        row.AddChild(Profiles);
        _delete = ScenarioConfirmationPanel.TrashButton("ScenarioDeleteConfigurationButton", "删除当前配置文件；内置示例只读");
        _delete.CustomMinimumSize = new Vector2(35, 35);
        _delete.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _delete.AddThemeConstantOverride("icon_max_width", 17);
        foreach (string state in new[] { "normal", "hover", "pressed", "disabled" })
        {
            StyleBoxFlat style = Style(new Color("f8edcf"), 0);
            style.BorderColor = new Color("b59a67");
            style.SetBorderWidthAll(2);
            style.ContentMarginLeft = style.ContentMarginRight = 6;
            style.ContentMarginTop = style.ContentMarginBottom = 6;
            _delete.AddThemeStyleboxOverride(state, style);
        }
        _delete.Pressed += () => DeleteRequested?.Invoke();
        row.AddChild(_delete);
        _status = MakeLabel("选择配置后编辑。", 13, Muted);
        _status.Name = "ScenarioDraftStatusLabel";
        _status.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _status.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(_status);
        Form = new ScenarioConfigurationForm();
        Form.Edited += () => UpdateState(false);
        AddChild(Form);
        var separator = new HSeparator { Name = "ScenarioEditorFooterSeparator" };
        separator.AddThemeConstantOverride("separation", 1);
        separator.AddThemeStyleboxOverride("separator", new StyleBoxLine { Color = new Color("bba573"), Thickness = 1 });
        AddChild(separator);
        var actions = new HBoxContainer();
        AddChild(actions);
        _back = MakeSecondaryButton("← 返回流程", 140, 43);
        _back.Name = "ScenarioEditorBackButton";
        _back.Pressed += () => BackRequested?.Invoke();
        actions.AddChild(_back);
        _saveAs = MakeSecondaryButton("另存为…", 104, 43);
        _saveAs.Name = "ScenarioSaveAsButton";
        _saveAs.Pressed += () => SaveAsRequested?.Invoke();
        actions.AddChild(_saveAs);
        _overwrite = MakeSecondaryButton("覆盖保存", 104, 43);
        _overwrite.Name = "ScenarioOverwriteButton";
        _overwrite.Pressed += () => OverwriteRequested?.Invoke();
        actions.AddChild(_overwrite);
        actions.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _continue = MakeButton("继续：运行与结果 →", Mid, 190, 43);
        _continue.Name = "ScenarioEditorContinueButton";
        _continue.Pressed += () => ContinueRequested?.Invoke();
        actions.AddChild(_continue);
        foreach (Button button in new[] { _back, _saveAs, _overwrite, _continue }) button.AddThemeFontSizeOverride("font_size", 16);
        _saveNote = MakeLabel("", 12, Muted);
        _saveNote.Name = "ScenarioSaveNoteLabel";
        AddChild(_saveNote);
    }

    internal void ShowFlow(string name, string description)
    {
        _flowTitle.Text = name.Length == 0 ? "调整配置" : name;
        _flowDescription.Text = description;
        _flowDescription.Visible = description.Length != 0;
    }

    internal void ShowProfiles(IReadOnlyList<ScenarioConfigurationEntry> entries, ScenarioConfigurationDraft? draft)
    {
        Profiles.Clear();
        int selected = 0;
        for (int index = 0; index < entries.Count; index++)
        {
            ScenarioConfigurationEntry entry = entries[index];
            Profiles.AddItem(entry.CaseId + " · 修订" + entry.Revision + (entry.IsReadOnly ? " · 内置只读" : ""));
            if (entry.Id == draft?.Entry?.Id) selected = index;
        }
        if (entries.Count > 0) Profiles.Select(selected);
        Profiles.TooltipText = draft?.Entry?.CaseId ?? "";
        _draft = draft;
        Form.Visible = draft != null;
        if (draft != null) Form.BindDraft(draft);
        UpdateState(false);
    }

    internal void UpdateState(bool locked)
    {
        Profiles.Disabled = locked;
        _delete.Disabled = locked || _draft == null || _draft.Entry!.IsReadOnly;
        _saveAs.Disabled = locked || _draft == null;
        _overwrite.Disabled = locked || _draft == null || _draft.Entry!.IsReadOnly;
        _back.Disabled = locked;
        _continue.Disabled = locked || _draft == null;
        Form.SetLocked(locked);
        _status.Text = _draft == null ? "没有配置。" : (_draft.IsDirty ? "未保存修改" : _draft.Entry!.IsReadOnly ? "内置只读" : "已保存") + " · 修订" + _draft.Entry!.Revision;
        _saveNote.Text = _draft == null ? "" : _draft.Entry!.IsReadOnly ?
            "内置示例只读：另存为自己的配置后可覆盖、删除与运行。" : "覆盖会更新此配置，已有报告保留原摘要。";
        _saveNote.Visible = _draft != null;
    }
}
