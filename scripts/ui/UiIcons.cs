using System.Collections.Generic;
using Godot;

namespace FarmExchange.UI;

internal enum UiIcon
{
    Leaf, Build, Box, Market, Calendar, Coin, People,
    Pause, Play, Close, Arrow, Water, Wheat, Mill, Reset,
}

/**
 * <summary>复用本项目像素田园原型的线条图标，不参与经营或文字缩放。</summary>
 */
internal static class UiIcons
{
    private static readonly Dictionary<UiIcon, Texture2D> Textures = new();

    internal static Texture2D Texture(UiIcon icon)
    {
        if (!Textures.TryGetValue(icon, out Texture2D? texture))
        {
            texture = GD.Load<Texture2D>($"res://assets/ui/icons/{icon.ToString().ToLowerInvariant()}.svg");
            Textures.Add(icon, texture);
        }
        return texture;
    }

    internal static TextureRect Create(UiIcon icon, float size = 21, Color? color = null) => new()
    {
        Texture = Texture(icon),
        Modulate = color ?? UiElements.Ink,
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        CustomMinimumSize = new Vector2(size, size),
        MouseFilter = Control.MouseFilterEnum.Ignore,
    };
}
