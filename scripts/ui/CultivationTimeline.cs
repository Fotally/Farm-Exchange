using System;
using System.Collections.Generic;
using Godot;
using FarmExchange.Cultivation;
using FarmExchange.Gameplay;
using FarmExchange.Time;
using static FarmExchange.UI.UiElements;

namespace FarmExchange.UI;

/**
 * <summary>把一份年度草稿画成四季折行时间轴，拖动只报告整条的新日期。</summary>
 */
public partial class CultivationTimeline : Control
{
    private const float LabelWidth = 42;
    private const float RowHeight = 50;
    private readonly Func<int, CropKind, int, bool> _checkDrop;
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

    /**
     * <summary>构造时间图，候选排程由拥有草稿的窗口检查。</summary>
     * <param name="checkDrop">收到原条编号、作物与年度起点，返回是否允许落位。</param>
     */
    public CultivationTimeline(Func<int, CropKind, int, bool> checkDrop)
    {
        _checkDrop = checkDrop;
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
            DrawStyleBox(Style(new Color("e7e8d4"), 0), new Rect2(LabelWidth, y, width, 32));
            for (int week = 1; week < 12; week++)
            {
                float x = LabelWidth + PixelsPerDay * week * 7;
                DrawLine(new Vector2(x, y + 32), new Vector2(x, y + 37), Muted);
            }
            for (int month = 0; month <= 3; month++)
            {
                float x = LabelWidth + width * month / 3f;
                DrawLine(new Vector2(x, y), new Vector2(x, y + 32), Muted);
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
                    24 + row * RowHeight - extra, width * count / 84f, 28);
                Color color = CropColor(entry.Crop);
                bool risk = Contains(_risks, entry.Id);
                StyleBoxFlat style = Style(color, 0);
                style.BorderColor = Wood;
                style.SetBorderWidthAll(1);
                if (risk || _selectedId == entry.Id)
                {
                    style.BorderColor = risk ? new Color("a53d2b") : Mid;
                    style.SetBorderWidthAll(2);
                }
                DrawStyleBox(style, rect);
                string[] lines = EntryLines(entry);
                if (Fits(rect, lines))
                {
                    DrawCentered(font, rect, lines[0], 12);
                    DrawCentered(font, rect, lines[1], 25);
                }
                else
                {
                    string name = FarmGame.GetCrop(entry.Crop).CropName;
                    if (font.GetStringSize(name, fontSize: 12).X + 4 <= rect.Size.X)
                        DrawCentered(font, rect, name, 19);
                }
                _segments.Add((rect, entry));
                remaining -= count;
                day = (day + count) % 336;
            }
        }
        int currentRow = _currentDay / 84;
        float currentX = LabelWidth + width * (_currentDay % 84) / 84f;
        DrawLine(new Vector2(currentX, 22 + currentRow * RowHeight),
            new Vector2(currentX, 54 + currentRow * RowHeight), Gold, 2);
    }

    public override string _GetTooltip(Vector2 atPosition)
    {
        foreach ((Rect2 rect, CultivationEntry entry) in _segments)
            if (rect.HasPoint(atPosition) && !Fits(rect, EntryLines(entry)))
            {
                int end = entry.StartDay + entry.LengthDays;
                return $"{FarmGame.GetCrop(entry.Crop).CropName} · 完整 {entry.LengthDays} 天\n" +
                    $"开始：{FullDate(entry.StartDay)}\n结束：{(end >= 336 ? "次年 " : "")}{FullDate(end % 336)}" +
                    (end > 336 ? "\n冬春跨年，同一轮" : entry.StartDay / 84 != (end - 1) / 84 ? "\n跨季，同一轮" : "");
            }
        return "";
    }

    private static string[] EntryLines(CultivationEntry entry)
    {
        int end = entry.StartDay + entry.LengthDays;
        return new[] { $"{FarmGame.GetCrop(entry.Crop).CropName} · {entry.LengthDays}天",
            $"{ShortDate(entry.StartDay)}→{(end >= 336 ? "次年" : "")}{ShortDate(end % 336)}" };
    }

    private static string ShortDate(int day)
    {
        GameDate date = GameCalendar.GetDate((uint)day);
        return $"{date.Month}/{date.Day}";
    }

    private static string FullDate(int day)
    {
        GameDate date = GameCalendar.GetDate((uint)day);
        return $"{date.Month}月{date.Day}日";
    }
    private static bool Fits(Rect2 rect, string[] lines) =>
        ThemeDB.FallbackFont.GetStringSize(lines[0], fontSize: 12).X + 6 <= rect.Size.X &&
        ThemeDB.FallbackFont.GetStringSize(lines[1], fontSize: 12).X + 6 <= rect.Size.X;

    private void DrawCentered(Font font, Rect2 rect, string text, float baseline) =>
        DrawString(font, rect.Position + new Vector2((rect.Size.X - font.GetStringSize(text, fontSize: 12).X) / 2,
            baseline), text, fontSize: 12, modulate: Ink);

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
        bool canDrop = atPosition.X >= LabelWidth && atPosition.X < Size.X - 8 &&
            atPosition.Y >= 22 && atPosition.Y < 22 + 4 * RowHeight && IsCropDrag(data);
        if (!canDrop) return false;
        var values = data.AsGodotDictionary();
        CropKind crop = (CropKind)values["crop"].AsInt32();
        return _checkDrop(values["id"].AsInt32(), crop, DropDay(atPosition, crop));
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (!_CanDropData(atPosition, data)) return;
        var values = data.AsGodotDictionary();
        CropKind crop = (CropKind)values["crop"].AsInt32();
        int day = DropDay(atPosition, crop);
        _landing = 0.18f;
        EntryDropped?.Invoke(values["id"].AsInt32(), (CropKind)values["crop"].AsInt32(), day);
    }

    private int DropDay(Vector2 atPosition, CropKind crop)
    {
        int row = (int)((atPosition.Y - 22) / RowHeight);
        int day = row * 84 + (int)Math.Floor((atPosition.X - LabelWidth) / PixelsPerDay -
            FarmGame.GetCrop(crop).GrowthDays / 2f);
        return (day + 336) % 336;
    }

    internal static Godot.Collections.Dictionary DragData(CropKind crop, int id) =>
        new() { ["cultivation"] = true, ["crop"] = (int)crop, ["id"] = id };

    internal static bool IsCropDrag(Variant data) => data.VariantType == Variant.Type.Dictionary &&
        data.AsGodotDictionary().ContainsKey("cultivation");

    internal static Control MakePreview(CropKind kind, float pixelsPerDay)
    {
        CropDefinition crop = FarmGame.GetCrop(kind);
        var preview = new Control { MouseFilter = MouseFilterEnum.Ignore };
        var bar = new PanelContainer
        {
            Size = new Vector2(crop.GrowthDays * pixelsPerDay, 40),
            Position = new Vector2(-crop.GrowthDays * pixelsPerDay / 2, -20),
            Modulate = new Color(1, 1, 1, 0.86f),
        };
        preview.AddChild(bar);
        StyleBoxFlat previewStyle = Style(CropColor(kind), 0);
        previewStyle.BorderColor = Wood;
        previewStyle.SetBorderWidthAll(1);
        bar.AddThemeStyleboxOverride("panel", previewStyle);
        string caption = $"{crop.CropName} · {crop.GrowthDays}天";
        float width = crop.GrowthDays * pixelsPerDay;
        if (ThemeDB.FallbackFont.GetStringSize(caption, fontSize: 13).X + 4 > width)
            caption = ThemeDB.FallbackFont.GetStringSize(crop.CropName, fontSize: 13).X + 4 <= width
                ? crop.CropName : "";
        Label text = MakeLabel(caption, 13, Ink);
        text.AutowrapMode = TextServer.AutowrapMode.Off;
        text.VerticalAlignment = VerticalAlignment.Center;
        text.HorizontalAlignment = HorizontalAlignment.Center;
        bar.AddChild(text);
        bar.TreeEntered += () => bar.CreateTween().TweenProperty(bar, "modulate:a", 1f, 0.12);
        return preview;
    }

    private static bool Contains(IReadOnlyList<int> values, int id)
    {
        foreach (int value in values) if (value == id) return true;
        return false;
    }

    private static Color CropColor(CropKind crop) => crop switch
    {
        CropKind.Wheat => new Color("d8c17c"),
        CropKind.Corn => new Color("c3d18d"),
        CropKind.Rice => new Color("d1d5ad"),
        CropKind.Potato => new Color("d8c0a0"),
        CropKind.Sunflower => new Color("e4ce85"),
        CropKind.Sugarcane => new Color("b9cd90"),
        CropKind.Radish => new Color("e2bca3"),
        _ => throw new ArgumentOutOfRangeException(nameof(crop)),
    };
}
