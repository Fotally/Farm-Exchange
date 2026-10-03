using Godot;
using FarmExchange.Gameplay;
using static FarmExchange.UI.UiElements;

namespace FarmExchange.UI;

/** <summary>从作物区拖出一轮完整生长周期，不发出播种命令。</summary> */
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
        AddThemeColorOverride("font_color", Ink);
        AddThemeFontSizeOverride("font_size", 13);
        TooltipText = "拖入年度时间图，安排一轮作物";
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        SetDragPreview(CultivationTimeline.MakePreview(_crop, _timeline.PixelsPerDay));
        return CultivationTimeline.DragData(_crop, 0);
    }
}
