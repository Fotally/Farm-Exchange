using System;
using Godot;
using static FarmExchange.UI.UiElements;

namespace FarmExchange.UI;

public partial class RoadDetailsPanel : VBoxContainer
{
    public event Action? RemoveRequested;

    public RoadDetailsPanel()
    {
        Name = "RoadDetailsPanel";
        AddThemeConstantOverride("separation", 9);
        AddChild(MakeLabel("道路 · 1×1 基础格", 18, Ink));
        AddChild(MakeInfoCard("用途：当前仅用于布局和外观"));
        AddChild(MakeLabel("下一步：在建造目录选择道路，连续点击空格铺设。", 14, Ink));
        AddChild(MakeLabel("拆除道路释放占地，不退还建造费。", 12, Ink));
        Button remove = MakeQuietButton("拆除道路", 0, 32);
        remove.Name = "RemoveRoadButton";
        remove.Pressed += () => RemoveRequested?.Invoke();
        AddChild(remove);
    }
}
