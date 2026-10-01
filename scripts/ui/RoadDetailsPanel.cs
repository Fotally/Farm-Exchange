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
        AddChild(MakeInfoCard("道路"));
        AddChild(MakeInfoCard("用途：当前仅用于布局和外观"));
        AddChild(MakeLabel("拆除道路不退还建造费。", 12, Ink));
        Button remove = MakeButton("拆除道路", new Color(0.66f, 0.36f, 0.31f), 0, 43);
        remove.Name = "RemoveRoadButton";
        remove.Pressed += () => RemoveRequested?.Invoke();
        AddChild(remove);
    }
}
