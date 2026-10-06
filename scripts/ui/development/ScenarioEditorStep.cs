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
        var row = new HBoxContainer();
        AddChild(row);
        Profiles = new OptionButton { Name = "ScenarioProfileChoice", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        Profiles.ItemSelected += index => ProfileSelected?.Invoke((int)index);
        row.AddChild(Profiles);
        _delete = ScenarioConfirmationPanel.TrashButton("ScenarioDeleteConfigurationButton", "删除当前配置文件；内置示例只读");
        _delete.Pressed += () => DeleteRequested?.Invoke();
        row.AddChild(_delete);
        _status = MakeLabel("选择配置后编辑。", 12, Muted);
        _status.Name = "ScenarioDraftStatusLabel";
        AddChild(_status);
        Form = new ScenarioConfigurationForm();
        Form.Edited += () => UpdateState(false);
        AddChild(Form);
        var actions = new HBoxContainer();
        AddChild(actions);
        _back = MakeSecondaryButton("← 返回流程", 140, 44);
        _back.Name = "ScenarioEditorBackButton";
        _back.Pressed += () => BackRequested?.Invoke();
        actions.AddChild(_back);
        _saveAs = MakeSecondaryButton("另存为", 120, 44);
        _saveAs.Name = "ScenarioSaveAsButton";
        _saveAs.Pressed += () => SaveAsRequested?.Invoke();
        actions.AddChild(_saveAs);
        _overwrite = MakeSecondaryButton("覆盖保存", 120, 44);
        _overwrite.Name = "ScenarioOverwriteButton";
        _overwrite.Pressed += () => OverwriteRequested?.Invoke();
        actions.AddChild(_overwrite);
        actions.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _continue = MakeButton("继续：运行与结果 →", Mid, 190, 44);
        _continue.Name = "ScenarioEditorContinueButton";
        _continue.Pressed += () => ContinueRequested?.Invoke();
        actions.AddChild(_continue);
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
        _status.Text = _draft == null ? "没有配置。" :
            (_draft.IsDirty ? "未保存修改。" : "已保存。") +
            (_draft.Entry!.IsReadOnly ? "内置示例只读：另存为自己的配置后可覆盖、删除与运行。" : "覆盖会更新此配置，已有报告保留原摘要。");
    }
}
