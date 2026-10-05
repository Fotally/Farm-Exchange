using Godot;
using FarmExchange.Gameplay;
using static FarmExchange.UI.UiElements;

namespace FarmExchange.UI;

/**
 * <summary>从作物区拖出一轮完整生长周期，不发出播种命令。</summary>
 */
public partial class CultivationCropCard : Button
{
    private readonly CropKind _crop;
    private readonly CultivationTimeline _timeline;

    public CultivationCropCard(CropKind crop, CultivationTimeline timeline)
    {
        _crop = crop;
        _timeline = timeline;
        CropDefinition definition = FarmGame.GetCrop(crop);
        Name = $"CultivationCrop{crop}";
        Text = $"{definition.CropName}\n{definition.GrowthDays}天";
        CustomMinimumSize = new Vector2(64, 48);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_hover_pressed_color", "font_focus_color" })
            AddThemeColorOverride(state, Ink);
        StyleBoxFlat normal = Style(Paper, 0);
        normal.BorderColor = Wood;
        normal.SetBorderWidthAll(1);
        AddThemeStyleboxOverride("normal", normal);
        StyleBoxFlat hover = Style(Paper.Lightened(0.08f), 0);
        hover.BorderColor = Mid;
        hover.SetBorderWidthAll(2);
        AddThemeStyleboxOverride("hover", hover);
        AddThemeStyleboxOverride("pressed", hover);
        AddThemeStyleboxOverride("hover_pressed", hover);
        AddThemeFontSizeOverride("font_size", 13);
        TooltipText = "拖入年度时间图，安排一轮作物";
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        SetDragPreview(CultivationTimeline.MakePreview(_crop, _timeline));
        return CultivationTimeline.DragData(_crop, 0);
    }
}
