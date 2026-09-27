using System.Globalization;
using Godot;

namespace FarmExchange.UI;

internal static class UiElements
{
    internal static readonly Color Dark = new(0.13f, 0.24f, 0.22f);
    internal static readonly Color Mid = new(0.25f, 0.42f, 0.35f);
    internal static readonly Color Cream = new(1f, 0.98f, 0.91f);
    internal static readonly Color Ink = new(0.16f, 0.29f, 0.25f);
    internal static readonly Color Gold = new(0.74f, 0.55f, 0.32f);
    internal static readonly Color Muted = new(0.70f, 0.75f, 0.69f);

    internal static PanelContainer MakeInfoCard(string text, out Label label)
    {
        var card = new PanelContainer();
        card.AddThemeStyleboxOverride("panel", Style(new Color(0.90f, 0.94f, 0.86f), 9));
        var margin = WrapMargin(card, 11, 9);
        label = MakeLabel(text, 14, Ink);
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
        button.AddThemeStyleboxOverride("normal", Style(background, 9));
        button.AddThemeStyleboxOverride("hover", Style(background.Lightened(0.12f), 9));
        button.AddThemeStyleboxOverride("pressed", Style(background.Darkened(0.12f), 9));
        button.AddThemeStyleboxOverride("disabled", Style(new Color(0.39f, 0.47f, 0.42f), 9));
        button.AddThemeColorOverride("font_color", Cream);
        button.AddThemeColorOverride("font_hover_color", Cream);
        button.AddThemeColorOverride("font_pressed_color", Cream);
        button.AddThemeColorOverride("font_disabled_color", Muted);
        button.AddThemeFontSizeOverride("font_size", 14);
        return button;
    }

    internal static StyleBoxFlat Style(Color color, int radius) => new()
    {
        BgColor = color,
        CornerRadiusTopLeft = radius,
        CornerRadiusTopRight = radius,
        CornerRadiusBottomLeft = radius,
        CornerRadiusBottomRight = radius,
    };

    internal static void ClearChildren(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }

    internal static string FormatCoins(int cents) =>
        (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
}
