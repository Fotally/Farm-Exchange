using System;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Land;
using static FarmExchange.UI.UiElements;

namespace FarmExchange.UI;

public partial class RoadDetailsPanel : VBoxContainer
{
    private readonly FacilityPreview _preview;
    public event Action? RemoveRequested;

    public RoadDetailsPanel()
    {
        Name = "RoadDetailsPanel";
        AddThemeConstantOverride("separation", 11);
        _preview = new FacilityPreview { Name = "RoadFacilityPreview" };
        AddChild(_preview);
        var heading = new HBoxContainer();
        var title = MakeLabel("乡间道路", 27, Ink);
        title.Name = "RoadTitle";
        title.AutowrapMode = TextServer.AutowrapMode.Off;
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        heading.AddChild(title);
        var status = MakeLabel("已铺设", 14, Mid);
        status.Name = "RoadCurrentStatus";
        status.AutowrapMode = TextServer.AutowrapMode.Off;
        status.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        heading.AddChild(status);
        AddChild(heading);
        AddChild(MakeLabel("用途：当前仅用于布局和外观", 14, Muted));
        var next = new HBoxContainer();
        next.AddChild(UiIcons.Create(UiIcon.Build, 25, Muted));
        var nextStep = MakeLabel("建造目录 → 选择道路\n连续点击空格铺设。", 14, Muted);
        nextStep.Name = "RoadNextStep";
        nextStep.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        next.AddChild(nextStep);
        AddChild(next);
        AddChild(MakeLabel("拆除道路释放占地，不退还建造费。", 12, Muted));
        Button remove = MakeQuietButton("拆除道路 · 不退款", 0, 35);
        remove.AddThemeFontSizeOverride("font_size", 13);
        remove.Name = "RemoveRoadButton";
        remove.Pressed += () => RemoveRequested?.Invoke();
        AddChild(remove);
    }

    /**
     * <summary>显示已选道路的实际占地与锚点。</summary>
     * <param name="space">已选中道路的只读空间快照。</param>
     */
    public void RefreshPlacement(BuildingSpaceSnapshot space) =>
        _preview.Refresh(space, default, CropStage.None);
}
