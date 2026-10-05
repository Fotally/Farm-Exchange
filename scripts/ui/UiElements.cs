using System.Globalization;
using Godot;

namespace FarmExchange.UI;

internal static class UiElements
{
    internal static readonly Color Wood = new("846848");
    internal static readonly Color Paper = new("f6e8c6");
    internal static readonly Color Dark = new("685239");
    internal static readonly Color Mid = new("657342");
    internal static readonly Color Cream = new("fff8e6");
    internal static readonly Color Ink = new("53452f");
    internal static readonly Color Gold = new("997944");
    internal static readonly Color Muted = new("817257");
    private static Theme? _sharedTheme;

    internal static Theme SharedTheme => _sharedTheme ??= BuildTheme();

    internal static PanelContainer MakeInfoCard(string text, out Label label)
    {
        var card = new PanelContainer();
        card.AddThemeStyleboxOverride("panel", Style(new Color("ebdbb1"), 0));
        var margin = WrapMargin(card, 11, 9);
        label = MakeLabel(text, 12, Ink);
        margin.AddChild(label);
        return card;
    }

    internal static PanelContainer MakeInfoCard(string text) => MakeInfoCard(text, out _);

    internal static MarginContainer WrapMargin(Control parent, int horizontal, int vertical)
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", horizontal);
        margin.AddThemeConstantOverride("margin_right", horizontal);
        margin.AddThemeConstantOverride("margin_top", vertical);
        margin.AddThemeConstantOverride("margin_bottom", vertical);
        parent.AddChild(margin);
        return margin;
    }

    internal static PanelContainer AddFrameLining(PanelContainer frame)
    {
        var lining = new PanelContainer();
        var style = Style(((StyleBoxFlat)frame.GetThemeStylebox("panel")).BgColor, 0);
        style.BorderColor = new Color("c8ad76");
        style.SetBorderWidthAll(2);
        lining.AddThemeStyleboxOverride("panel", style);
        frame.AddChild(lining);
        return lining;
    }

    internal static Label MakeLabel(string text, int size, Color color)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeFontSizeOverride("font_size", size);
        return label;
    }

    internal static Button MakeButton(string text, Color background, float width, float height)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(width, height) };
        button.Theme = SharedTheme;
        button.AddThemeStyleboxOverride("normal", ButtonStyle(background));
        button.AddThemeStyleboxOverride("hover", ButtonStyle(background.Lightened(0.08f)));
        button.AddThemeStyleboxOverride("pressed", ButtonStyle(background.Darkened(0.10f), pressed: true));
        Color textColor = background.Luminance > 0.6f ? Ink : Cream;
        foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
            button.AddThemeColorOverride(state, textColor);
        button.AddThemeFontSizeOverride("font_size", 13);
        return button;
    }

    internal static Button MakeSecondaryButton(string text, float width, float height) =>
        MakeButton(text, new Color("e5d5a7"), width, height);

    internal static Button MakeQuietButton(string text, float width, float height) =>
        MakeButton(text, Paper, width, height);

    internal static StyleBoxFlat Style(Color color, int radius) => new()
    {
        BgColor = color,
        BorderColor = Wood,
        BorderWidthLeft = 1,
        BorderWidthRight = 1,
        BorderWidthTop = 1,
        BorderWidthBottom = 1,
    };

    internal static StyleBoxFlat Frame(Color color, int borderWidth)
    {
        StyleBoxFlat frame = Style(color, 0);
        frame.SetBorderWidthAll(borderWidth);
        frame.ShadowColor = new Color("554b3430");
        frame.ShadowSize = 1;
        frame.ShadowOffset = new Vector2(4, 4);
        return frame;
    }

    private static StyleBoxFlat ButtonStyle(Color color, bool pressed = false)
    {
        StyleBoxFlat style = Style(color, 0);
        style.SetBorderWidthAll(2);
        style.BorderColor = color.Luminance > 0.6f ? new Color("b9a477") : new Color("455234");
        style.BorderWidthBottom = pressed ? 2 : 3;
        style.ContentMarginLeft = 10;
        style.ContentMarginRight = 10;
        style.ContentMarginTop = pressed ? 5 : 3;
        style.ContentMarginBottom = pressed ? 3 : 5;
        return style;
    }

    private static Theme BuildTheme()
    {
        var theme = new Theme
        {
            DefaultFontSize = 13,
            DefaultFont = new SystemFont { FontNames = new[] { "Microsoft YaHei", "PingFang SC" }, FontWeight = 400 },
        };
        theme.SetColor("font_color", "Label", Ink);
        StyleBoxFlat frame = Frame(Paper, 4);
        theme.SetStylebox("panel", "PanelContainer", frame);
        theme.SetConstant("separation", "VBoxContainer", 8);
        theme.SetConstant("separation", "HBoxContainer", 8);
        var focus = Style(Colors.Transparent, 0);
        focus.BorderColor = Gold;
        focus.SetBorderWidthAll(2);
        foreach (string type in new[] { "Button", "OptionButton", "CheckBox", "CheckButton" })
        {
            bool check = type is "CheckBox" or "CheckButton";
            theme.SetStylebox("normal", type, ButtonStyle(check ? Paper : Mid));
            theme.SetStylebox("hover", type, ButtonStyle(check ? Paper.Lightened(0.05f) : Mid.Lightened(0.08f)));
            theme.SetStylebox("pressed", type, ButtonStyle(check ? Paper.Darkened(0.04f) : Mid.Darkened(0.10f), true));
            theme.SetStylebox("hover_pressed", type, ButtonStyle(check ? Paper : Mid.Lightened(0.05f), true));
            theme.SetStylebox("disabled", type, ButtonStyle(new Color("d8cbb0")));
            theme.SetStylebox("focus", type, focus);
            foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "font_hover_pressed_color" })
                theme.SetColor(state, type, check ? Ink : Cream);
            theme.SetColor("font_disabled_color", type, Muted);
        }
        theme.SetConstant("modulate_arrow", "OptionButton", 1);
        foreach (string type in new[] { "LineEdit", "TextEdit" })
        {
            StyleBoxFlat input = Style(Cream, 0);
            input.ContentMarginLeft = 8;
            input.ContentMarginRight = 8;
            input.ContentMarginTop = 5;
            input.ContentMarginBottom = 5;
            theme.SetStylebox("normal", type, input);
            theme.SetStylebox("read_only", type, Style(new Color("e5d8bb"), 0));
            theme.SetStylebox("focus", type, focus);
            theme.SetColor("font_color", type, Ink);
            theme.SetColor("font_uneditable_color", type, Muted);
            theme.SetColor("font_placeholder_color", type, Muted);
            theme.SetColor("caret_color", type, Ink);
            theme.SetColor("selection_color", type, new Color("bdc490"));
            theme.SetColor("font_selected_color", type, Ink);
        }
        theme.SetStylebox("background", "ProgressBar", Style(new Color("ded5b4"), 0));
        theme.SetStylebox("fill", "ProgressBar", Style(Mid, 0));
        theme.SetColor("font_color", "ProgressBar", Ink);
        foreach (string type in new[] { "VScrollBar", "HScrollBar" })
        {
            StyleBoxFlat track = Style(new Color("e4d7b5"), 0);
            if (type == "VScrollBar")
            {
                track.ContentMarginLeft = 6;
                track.ContentMarginRight = 6;
            }
            else
            {
                track.ContentMarginTop = 6;
                track.ContentMarginBottom = 6;
            }
            theme.SetStylebox("scroll", type, track);
            theme.SetStylebox("grabber", type, Style(Wood, 0));
            theme.SetStylebox("grabber_highlight", type, Style(Gold, 0));
            theme.SetStylebox("grabber_pressed", type, Style(Dark, 0));
        }
        theme.SetStylebox("panel", "ItemList", Style(Cream, 0));
        theme.SetStylebox("hovered", "ItemList", Style(Paper, 0));
        theme.SetStylebox("selected", "ItemList", Style(new Color("d6d9ad"), 0));
        theme.SetStylebox("selected_focus", "ItemList", Style(new Color("c6cea0"), 0));
        theme.SetStylebox("hovered_selected", "ItemList", Style(new Color("d6d9ad"), 0));
        theme.SetStylebox("hovered_selected_focus", "ItemList", Style(new Color("c6cea0"), 0));
        theme.SetStylebox("focus", "ItemList", focus);
        theme.SetStylebox("cursor", "ItemList", focus);
        theme.SetStylebox("cursor_unfocused", "ItemList", focus);
        foreach (string state in new[] { "font_color", "font_hovered_color", "font_selected_color", "font_hovered_selected_color" })
            theme.SetColor(state, "ItemList", Ink);
        theme.SetStylebox("panel", "PopupMenu", Style(Paper, 0));
        theme.SetStylebox("hover", "PopupMenu", Style(new Color("d6d9ad"), 0));
        theme.SetColor("font_color", "PopupMenu", Ink);
        theme.SetColor("font_hover_color", "PopupMenu", Ink);
        theme.SetColor("font_disabled_color", "PopupMenu", Muted);
        theme.SetStylebox("panel", "TooltipPanel", Style(Paper, 0));
        theme.SetColor("font_color", "TooltipLabel", Ink);
        return theme;
    }

    internal static void ClearChildren(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }

    internal static string FormatCoins(long cents) =>
        (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
}
