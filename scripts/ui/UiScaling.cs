using System;
using System.Collections.Generic;
using Godot;

namespace FarmExchange.UI;

/**
 * <summary>按控件原始排版分别设置整体与字体倍率。</summary>
 * <remarks>倍率属于界面节点，父子相乘；更新真实布局，不缩放画布或累计修改原值。</remarks>
 */
public static class UiScaling
{
    private sealed class Settings
    {
        internal float Overall = 1f;
        internal float Font = 1f;
        internal readonly Vector2 Minimum;
        internal readonly Vector4 Offsets;
        internal readonly bool FreeLayout;
        internal readonly Dictionary<string, int> FontSizes = new();
        internal readonly Dictionary<string, int> Constants = new();
        internal readonly Dictionary<string, StyleBoxFlat> Styles = new();
        internal readonly Dictionary<string, Texture2D> Icons = new();
        internal readonly HashSet<string> FontOverrides = new();
        internal readonly HashSet<string> ConstantOverrides = new();
        internal readonly HashSet<string> StyleOverrides = new();
        internal readonly HashSet<string> IconOverrides = new();
        internal readonly Theme? OriginalTheme;
        internal readonly Theme? OriginalPopupTheme;
        internal readonly Theme SourceTheme;
        internal readonly Theme TooltipTheme;
        internal readonly Theme? PopupTheme;
        internal readonly int TooltipFontSize;
        internal readonly Dictionary<string, int> PopupFontSizes = new();
        internal readonly Dictionary<string, int> PopupConstants = new();
        internal readonly Dictionary<string, StyleBoxFlat> PopupStyles = new();
        internal readonly Dictionary<string, Texture2D> PopupIcons = new();
        internal readonly Node.ChildEnteredTreeEventHandler ChildEntered;
        internal readonly Action Leaving;

        internal Settings(Control control)
        {
            Minimum = control.CustomMinimumSize;
            Offsets = new Vector4(control.OffsetLeft, control.OffsetTop, control.OffsetRight, control.OffsetBottom);
            FreeLayout = control.GetParent() is not Container && control is not DraggableWindow;
            OriginalTheme = control.Theme;
            OriginalPopupTheme = control is OptionButton option ? option.GetPopup().Theme : null;
            string type = control.GetClass();
            Theme defaults = ThemeDB.GetDefaultTheme();
            SourceTheme = EffectiveTheme(control);
            TooltipTheme = (Theme)SourceTheme.Duplicate();
            // 新增控件可能尚未进入场景树，先接入有效主题再读取原始排版，避免捕获引擎默认样式。
            control.Theme = TooltipTheme;
            TooltipFontSize = TooltipTheme.GetFontSize("font_size", "TooltipLabel");
            if (control is OptionButton menuButton)
            {
                PopupTheme = (Theme)(OriginalPopupTheme ?? SourceTheme).Duplicate();
                PopupMenu popup = menuButton.GetPopup();
                popup.Theme = PopupTheme;
                var popupFonts = new HashSet<string>(defaults.GetFontSizeList("PopupMenu"));
                popupFonts.UnionWith(PopupTheme.GetFontSizeList("PopupMenu"));
                foreach (string name in popupFonts)
                    PopupFontSizes[name] = popup.GetThemeFontSize(name);
                var popupConstants = new HashSet<string>(defaults.GetConstantList("PopupMenu"));
                popupConstants.UnionWith(PopupTheme.GetConstantList("PopupMenu"));
                foreach (string name in popupConstants)
                    // gutter_compact 是布尔选项，不是几何尺寸。
                    if (name != "gutter_compact") PopupConstants[name] = popup.GetThemeConstant(name);
                var popupStyles = new HashSet<string>(defaults.GetStyleboxList("PopupMenu"));
                popupStyles.UnionWith(PopupTheme.GetStyleboxList("PopupMenu"));
                foreach (string name in popupStyles)
                    if (popup.GetThemeStylebox(name) is StyleBoxFlat flat) PopupStyles[name] = flat;
                var popupIcons = new HashSet<string>(defaults.GetIconList("PopupMenu"));
                popupIcons.UnionWith(PopupTheme.GetIconList("PopupMenu"));
                foreach (string name in popupIcons)
                    if (popup.GetThemeIcon(name) is Texture2D icon) PopupIcons[name] = icon;
            }
            var fonts = new HashSet<string>(defaults.GetFontSizeList(type)) { "font_size" };
            foreach (string name in fonts)
                if (control.HasThemeFontSize(name))
                {
                    FontSizes[name] = control.GetThemeFontSize(name);
                    if (control.HasThemeFontSizeOverride(name)) FontOverrides.Add(name);
                }
            foreach (string name in new[] { "separation", "h_separation", "v_separation", "hseparation", "vseparation",
                         "margin_left", "margin_right", "margin_top", "margin_bottom", "line_spacing", "outline_size", "icon_max_width" })
                if (control.HasThemeConstant(name))
                {
                    Constants[name] = control.GetThemeConstant(name);
                    if (control.HasThemeConstantOverride(name)) ConstantOverrides.Add(name);
                }
            var styles = new HashSet<string>(defaults.GetStyleboxList(type));
            styles.UnionWith(UiElements.SharedTheme.GetStyleboxList(type));
            foreach (string name in styles)
                if (control.GetThemeStylebox(name) is StyleBoxFlat flat)
                {
                    Styles[name] = flat;
                    if (control.HasThemeStyleboxOverride(name)) StyleOverrides.Add(name);
                }
            foreach (string name in defaults.GetIconList(type))
                if (control.GetThemeIcon(name) is Texture2D icon)
                {
                    Icons[name] = icon;
                    if (control.HasThemeIconOverride(name)) IconOverrides.Add(name);
                }
            ChildEntered = child =>
            {
                if (child is Control nested) Bind(nested);
            };
            Leaving = () =>
            {
                control.ChildEnteredTree -= ChildEntered;
                control.TreeExiting -= Leaving;
                Restore(control, this);
                _settings.Remove(control);
            };
        }
    }

    private static readonly Dictionary<Control, Settings> _settings = new();
    internal static event Action<Control>? Changed;

    /**
     * <summary>设置一个界面子树的整体倍率，几何、图标和文字同时重新排版。</summary>
     * <param name="ui">窗口、面板或控件根节点。</param>
     * <param name="multiplier">有限正数，1 为原始排版。</param>
     */
    public static void SetOverallScale(Control ui, float multiplier)
    {
        CheckMultiplier(multiplier);
        Bind(ui);
        _settings[ui].Overall = multiplier;
        ApplyTree(ui);
        Changed?.Invoke(ui);
    }

    /**
     * <summary>设置一个界面子树相对于整体倍率的字体倍率。</summary>
     * <param name="ui">窗口、面板或控件根节点。</param>
     * <param name="multiplier">有限正数，1 为原始字号；与父级字体倍率相乘。</param>
     */
    public static void SetFontScale(Control ui, float multiplier)
    {
        CheckMultiplier(multiplier);
        Bind(ui);
        _settings[ui].Font = multiplier;
        ApplyTree(ui);
        Changed?.Invoke(ui);
    }

    internal static float OverallScale(Control control) => Product(control, font: false);
    internal static float FontScale(Control control) => Product(control, font: true);
    internal static int FontSize(Control control, int originalSize) =>
        Math.Max(1, (int)MathF.Round(originalSize * OverallScale(control) * FontScale(control)));

    internal static void Bind(Control control)
    {
        if (_settings.ContainsKey(control)) return;
        var settings = new Settings(control);
        _settings.Add(control, settings);
        control.ChildEnteredTree += settings.ChildEntered;
        control.TreeExiting += settings.Leaving;
        foreach (Node child in control.GetChildren(includeInternal: true))
            if (child is Control nested) Bind(nested);
        Apply(control, settings);
    }

    private static void CheckMultiplier(float value)
    {
        if (!float.IsFinite(value) || value <= 0f)
            throw new ArgumentOutOfRangeException(nameof(value), "界面倍率必须是有限正数。");
    }

    private static Theme EffectiveTheme(Control control)
    {
        for (Node? node = control; node != null; node = node.GetParent())
            if (node is Control ancestor)
            {
                if (_settings.TryGetValue(ancestor, out Settings? settings)) return settings.SourceTheme;
                if (ancestor.Theme is Theme theme) return theme;
            }
        return UiElements.SharedTheme;
    }

    private static float Product(Control control, bool font)
    {
        float value = 1f;
        for (Node? node = control; node != null; node = node.GetParent())
            if (node is Control ancestor && _settings.TryGetValue(ancestor, out Settings? settings))
                value *= font ? settings.Font : settings.Overall;
        return value;
    }

    private static void ApplyTree(Control control)
    {
        if (_settings.TryGetValue(control, out Settings? settings)) Apply(control, settings);
        foreach (Node child in control.GetChildren(includeInternal: true))
            if (child is Control nested) ApplyTree(nested);
    }

    private static void Apply(Control control, Settings settings)
    {
        float overall = OverallScale(control);
        settings.TooltipTheme.SetFontSize("font_size", "TooltipLabel", FontSize(control, settings.TooltipFontSize));
        control.Theme = settings.TooltipTheme;
        if (control is OptionButton option)
        {
            Theme popupTheme = settings.PopupTheme!;
            foreach ((string name, int original) in settings.PopupFontSizes)
                popupTheme.SetFontSize(name, "PopupMenu", FontSize(control, original));
            foreach ((string name, int original) in settings.PopupConstants)
                popupTheme.SetConstant(name, "PopupMenu", (int)MathF.Round(original * overall));
            foreach ((string name, StyleBoxFlat original) in settings.PopupStyles)
                popupTheme.SetStylebox(name, "PopupMenu", ScaleStyle(original, overall));
            foreach ((string name, Texture2D original) in settings.PopupIcons)
                popupTheme.SetIcon(name, "PopupMenu", ScaleIcon(original, overall));
            option.GetPopup().Theme = popupTheme;
        }
        control.CustomMinimumSize = settings.Minimum * overall;
        if (settings.FreeLayout)
        {
            control.OffsetLeft = settings.Offsets.X * overall;
            control.OffsetTop = settings.Offsets.Y * overall;
            control.OffsetRight = settings.Offsets.Z * overall;
            control.OffsetBottom = settings.Offsets.W * overall;
        }
        control.BeginBulkThemeOverride();
        foreach ((string name, int original) in settings.FontSizes)
            control.AddThemeFontSizeOverride(name, FontSize(control, original));
        foreach ((string name, int original) in settings.Constants)
            control.AddThemeConstantOverride(name, (int)MathF.Round(original * overall));
        foreach ((string name, Texture2D original) in settings.Icons)
            control.AddThemeIconOverride(name, ScaleIcon(original, overall));
        foreach ((string name, StyleBoxFlat original) in settings.Styles)
        {
            StyleBoxFlat scaled = ScaleStyle(original, overall);
            if (control.GetThemeStylebox(name) is StyleBoxFlat current)
            {
                scaled.BgColor = current.BgColor;
                scaled.BorderColor = current.BorderColor;
            }
            control.AddThemeStyleboxOverride(name, scaled);
        }
        control.EndBulkThemeOverride();
        control.UpdateMinimumSize();
        control.QueueRedraw();
    }

    private static float ScaleMargin(float value, float multiplier) => value < 0 ? value : value * multiplier;

    private static StyleBoxFlat ScaleStyle(StyleBoxFlat original, float overall)
    {
        var scaled = (StyleBoxFlat)original.Duplicate();
        scaled.BorderWidthLeft = (int)MathF.Round(original.BorderWidthLeft * overall);
        scaled.BorderWidthTop = (int)MathF.Round(original.BorderWidthTop * overall);
        scaled.BorderWidthRight = (int)MathF.Round(original.BorderWidthRight * overall);
        scaled.BorderWidthBottom = (int)MathF.Round(original.BorderWidthBottom * overall);
        scaled.ContentMarginLeft = ScaleMargin(original.ContentMarginLeft, overall);
        scaled.ContentMarginRight = ScaleMargin(original.ContentMarginRight, overall);
        scaled.ContentMarginTop = ScaleMargin(original.ContentMarginTop, overall);
        scaled.ContentMarginBottom = ScaleMargin(original.ContentMarginBottom, overall);
        scaled.ShadowSize = (int)MathF.Round(original.ShadowSize * overall);
        scaled.ShadowOffset = original.ShadowOffset * overall;
        scaled.CornerRadiusTopLeft = (int)MathF.Round(original.CornerRadiusTopLeft * overall);
        scaled.CornerRadiusTopRight = (int)MathF.Round(original.CornerRadiusTopRight * overall);
        scaled.CornerRadiusBottomLeft = (int)MathF.Round(original.CornerRadiusBottomLeft * overall);
        scaled.CornerRadiusBottomRight = (int)MathF.Round(original.CornerRadiusBottomRight * overall);
        return scaled;
    }

    private static Texture2D ScaleIcon(Texture2D original, float overall)
    {
        // 原生滚动条用零尺寸 ImageTexture 表示没有增减按钮，倍率不把空图标变为位图。
        if (overall == 1f || original is ImageTexture && original.GetSize() == Vector2.Zero) return original;
        using Image icon = original.GetImage();
        icon.Resize(Math.Max(1, (int)MathF.Round(original.GetWidth() * overall)),
            Math.Max(1, (int)MathF.Round(original.GetHeight() * overall)), Image.Interpolation.Nearest);
        return ImageTexture.CreateFromImage(icon);
    }

    private static void Restore(Control control, Settings settings)
    {
        control.BeginBulkThemeOverride();
        foreach ((string name, int original) in settings.FontSizes)
            if (settings.FontOverrides.Contains(name)) control.AddThemeFontSizeOverride(name, original);
            else control.RemoveThemeFontSizeOverride(name);
        foreach ((string name, int original) in settings.Constants)
            if (settings.ConstantOverrides.Contains(name)) control.AddThemeConstantOverride(name, original);
            else control.RemoveThemeConstantOverride(name);
        foreach ((string name, StyleBoxFlat original) in settings.Styles)
            if (settings.StyleOverrides.Contains(name)) control.AddThemeStyleboxOverride(name, original);
            else control.RemoveThemeStyleboxOverride(name);
        foreach ((string name, Texture2D original) in settings.Icons)
            if (settings.IconOverrides.Contains(name)) control.AddThemeIconOverride(name, original);
            else control.RemoveThemeIconOverride(name);
        control.EndBulkThemeOverride();
        control.Theme = settings.OriginalTheme;
        if (control is OptionButton option) option.GetPopup().Theme = settings.OriginalPopupTheme;
        control.CustomMinimumSize = settings.Minimum;
        if (settings.FreeLayout)
        {
            control.OffsetLeft = settings.Offsets.X;
            control.OffsetTop = settings.Offsets.Y;
            control.OffsetRight = settings.Offsets.Z;
            control.OffsetBottom = settings.Offsets.W;
        }
    }
}
