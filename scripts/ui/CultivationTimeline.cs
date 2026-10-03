using System;
using System.Collections.Generic;
using Godot;
using FarmExchange.Cultivation;
using FarmExchange.Gameplay;
using static FarmExchange.UI.UiElements;

namespace FarmExchange.UI;

/**
 * <summary>把一份年度草稿画成四季折行时间轴，拖动只报告整条的新日期。</summary>
 */
public partial class CultivationTimeline : Control
{
    private const float LabelWidth = 42;
    private const float RowHeight = 50;
    private IReadOnlyList<CultivationEntry> _entries = Array.Empty<CultivationEntry>();
    private IReadOnlyList<int> _risks = Array.Empty<int>();
    private readonly List<(Rect2 Rect, CultivationEntry Entry)> _segments = new();
    private int? _selectedId;
    private float _landing;
    private int _currentDay;

    /**
     * <summary>报告选中的原始作物条编号，所有显示片段共用编号。</summary>
     */
    public event Action<int>? EntrySelected;
    /**
     * <summary>报告整条落位；新条编号为零，日期为年度整数日。</summary>
     */
    public event Action<int, CropKind, int>? EntryDropped;

    /**
     * <summary>当前图中一个游戏日的画面宽度。</summary>
     */
    public float PixelsPerDay => (Size.X - LabelWidth - 8) / 84;

    public CultivationTimeline()
    {
        Name = "CultivationTimeline";
        CustomMinimumSize = new Vector2(540, 4 * RowHeight + 22);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Stop;
    }

    /**
     * <summary>只显示草稿、风险和当前日期，不提交经营命令。</summary>
     * <param name="entries">本地草稿的完整作物条。</param>
     * <param name="risks">经营验证返回的风险条编号。</param>
     * <param name="selectedId">当前选中的原条编号。</param>
     * <param name="elapsedDays">已过去的游戏日。</param>
     */
    public void Refresh(IReadOnlyList<CultivationEntry> entries, IReadOnlyList<int> risks,
        int? selectedId, uint elapsedDays)
    {
        _entries = entries;
        _risks = risks;
        _selectedId = selectedId;
        _currentDay = (int)(elapsedDays % 336);
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_landing <= 0) return;
        _landing = Math.Max(0, _landing - (float)delta);
        QueueRedraw();
    }

    public override void _Draw()
    {
        _segments.Clear();
        Font font = ThemeDB.FallbackFont;
        float width = Size.X - LabelWidth - 8;
        string[] seasons = { "春", "夏", "秋", "冬" };
        for (int row = 0; row < 4; row++)
        {
            float y = 22 + row * RowHeight;
            DrawString(font, new Vector2(4, y + 24), seasons[row], fontSize: 15, modulate: Ink);
            DrawRect(new Rect2(LabelWidth, y, width, 40), new Color(0.86f, 0.91f, 0.83f));
            for (int month = 0; month <= 3; month++)
            {
                float x = LabelWidth + width * month / 3f;
                DrawLine(new Vector2(x, y), new Vector2(x, y + 40), Muted);
                if (month < 3)
                    DrawString(font, new Vector2(x + 3, y - 4), $"{row * 3 + month + 1}月", fontSize: 11, modulate: Ink);
            }
        }
        foreach (CultivationEntry entry in _entries)
        {
            int day = entry.StartDay;
            int remaining = entry.LengthDays;
            while (remaining > 0)
            {
                int row = day / 84;
                int localDay = day % 84;
                int count = Math.Min(remaining, 84 - localDay);
                float extra = _selectedId == entry.Id ? _landing * 8 : 0;
                var rect = new Rect2(LabelWidth + width * localDay / 84f,
                    24 + row * RowHeight - extra, width * count / 84f, 36);
                Color color = new Color(0.24f + (int)entry.Crop * 0.055f, 0.48f, 0.32f);
                DrawRect(rect, color);
                bool risk = Contains(_risks, entry.Id);
                if (risk || _selectedId == entry.Id)
                    DrawRect(rect, risk ? new Color(0.82f, 0.25f, 0.18f) : Gold, false, 2);
                string text = FarmGame.GetCrop(entry.Crop).CropName;
                if (rect.Size.X > 24)
                    DrawString(font, rect.Position + new Vector2(3, 24), text,
                        width: Math.Max(0, rect.Size.X - 6), fontSize: 12, modulate: Cream);
                _segments.Add((rect, entry));
                remaining -= count;
                day = (day + count) % 336;
            }
        }
        int currentRow = _currentDay / 84;
        float currentX = LabelWidth + width * (_currentDay % 84) / 84f;
        DrawLine(new Vector2(currentX, 22 + currentRow * RowHeight),
            new Vector2(currentX, 62 + currentRow * RowHeight), Gold, 2);
    }

    public override void _GuiInput(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mouse)
            return;
        foreach ((Rect2 rect, CultivationEntry entry) in _segments)
            if (rect.HasPoint(mouse.Position))
            {
                EntrySelected?.Invoke(entry.Id);
                AcceptEvent();
                return;
            }
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        foreach ((Rect2 rect, CultivationEntry entry) in _segments)
            if (rect.HasPoint(atPosition))
            {
                EntrySelected?.Invoke(entry.Id);
                SetDragPreview(MakePreview(entry.Crop, PixelsPerDay));
                return DragData(entry.Crop, entry.Id);
            }
        return default;
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        bool canDrop = atPosition.X >= LabelWidth && atPosition.X <= Size.X - 8 &&
            atPosition.Y >= 22 && atPosition.Y < 22 + 4 * RowHeight && IsCropDrag(data);
        return canDrop;
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        var values = data.AsGodotDictionary();
        int row = Math.Clamp((int)((atPosition.Y - 22) / RowHeight), 0, 3);
        int day = row * 84 + Math.Clamp((int)((atPosition.X - LabelWidth) /
            (Size.X - LabelWidth - 8) * 84), 0, 83);
        _landing = 0.18f;
        EntryDropped?.Invoke(values["id"].AsInt32(), (CropKind)values["crop"].AsInt32(), day);
    }

    internal static Godot.Collections.Dictionary DragData(CropKind crop, int id) =>
        new() { ["cultivation"] = true, ["crop"] = (int)crop, ["id"] = id };

    internal static bool IsCropDrag(Variant data) => data.VariantType == Variant.Type.Dictionary &&
        data.AsGodotDictionary().ContainsKey("cultivation");

    internal static Control MakePreview(CropKind kind, float pixelsPerDay)
    {
        CropDefinition crop = FarmGame.GetCrop(kind);
        var preview = new PanelContainer
        {
            CustomMinimumSize = new Vector2(crop.GrowthDays * pixelsPerDay, 40),
            Modulate = new Color(1, 1, 1, 0.86f),
        };
        preview.AddThemeStyleboxOverride("panel", Style(Mid.Lightened(0.15f), 5));
        Label text = MakeLabel($"{crop.CropName} · {crop.GrowthDays}天", 13, Cream);
        text.AutowrapMode = TextServer.AutowrapMode.Off;
        text.ClipText = true;
        text.VerticalAlignment = VerticalAlignment.Center;
        preview.AddChild(text);
        preview.TreeEntered += () => preview.CreateTween().TweenProperty(preview, "modulate:a", 1f, 0.12);
        return preview;
    }

    private static bool Contains(IReadOnlyList<int> values, int id)
    {
        foreach (int value in values) if (value == id) return true;
        return false;
    }
}
