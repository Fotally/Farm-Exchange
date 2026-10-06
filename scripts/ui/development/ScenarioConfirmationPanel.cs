using System;
using Godot;
using static FarmExchange.UI.UiElements;

namespace FarmExchange.UI;

internal partial class ScenarioConfirmationPanel : PanelContainer
{
    private readonly Label _message;
    private readonly LineEdit _name;
    private readonly Button _confirm;
    private Action<string>? _action;
    internal event Action? Closed;

    internal ScenarioConfirmationPanel()
    {
        Name = "ScenarioConfirmationPanel";
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddThemeStyleboxOverride("panel", Style(new Color("53452fb0"), 0));
        var center = new CenterContainer();
        AddChild(center);
        var frame = new PanelContainer { CustomMinimumSize = new Vector2(520, 220) };
        frame.AddThemeStyleboxOverride("panel", Frame(Paper, 5));
        center.AddChild(frame);
        var content = new VBoxContainer { CustomMinimumSize = new Vector2(440, 0) };
        WrapMargin(frame, 24, 22).AddChild(content);
        var heading = new HBoxContainer();
        content.AddChild(heading);
        var title = MakeLabel("请确认操作", 18, Ink);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        heading.AddChild(title);
        Button close = MakeQuietButton("×", 32, 32);
        close.Name = "ScenarioConfirmationCloseButton";
        close.Pressed += Cancel;
        heading.AddChild(close);
        _message = MakeLabel("", 15, Ink);
        _message.Name = "ScenarioConfirmationMessage";
        content.AddChild(_message);
        _name = new LineEdit { Name = "ScenarioSaveNameInput", PlaceholderText = "新配置名称" };
        content.AddChild(_name);
        var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        content.AddChild(actions);
        Button cancel = MakeSecondaryButton("取消", 104, 42);
        cancel.Name = "ScenarioConfirmationCancelButton";
        cancel.Pressed += Cancel;
        actions.AddChild(cancel);
        _confirm = MakeButton("确认删除", Mid, 120, 42);
        _confirm.Name = "ScenarioConfirmationConfirmButton";
        _confirm.Pressed += Confirm;
        actions.AddChild(_confirm);
        _name.TextSubmitted += _ => Confirm();
    }

    internal void Open(string message, Action<string> action, bool enterName = false)
    {
        _message.Text = message;
        _action = action;
        _name.Visible = enterName;
        _name.Text = "";
        _confirm.Text = enterName ? "确认另存为" : "确认删除";
        _confirm.Name = enterName ? "ScenarioSaveConfirmButton" : "ScenarioConfirmationConfirmButton";
        Show();
        if (enterName) _name.GrabFocus();
        else _confirm.GrabFocus();
    }

    internal void Cancel()
    {
        _action = null;
        Hide();
        Closed?.Invoke();
    }

    private void Confirm()
    {
        Action<string>? action = _action;
        string name = _name.Text;
        Cancel();
        action?.Invoke(name);
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (!Visible || inputEvent is not InputEventKey { Pressed: true, Keycode: Key.Escape }) return;
        Cancel();
        GetViewport().SetInputAsHandled();
    }

    internal static Button TrashButton(string name, string tooltip)
    {
        using var image = new Image();
        image.LoadSvgFromString("<svg xmlns='http://www.w3.org/2000/svg' width='20' height='20' viewBox='0 0 20 20'><g fill='none' stroke='#53452f' stroke-width='1.5'><path d='M4 6h12M8 3h4l1 3M6 6l1 11h6l1-11M9 8v6M11 8v6'/></g></svg>");
        Button button = MakeQuietButton("", 34, 34);
        button.Name = name;
        button.TooltipText = tooltip;
        button.Icon = ImageTexture.CreateFromImage(image);
        button.ExpandIcon = true;
        button.AddThemeConstantOverride("icon_max_width", 20);
        return button;
    }
}
