using System;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Farming;
using static FarmExchange.UI.UiElements;

namespace FarmExchange.UI;

public partial class CropSelectionWindow : DraggableWindow
{
    private const string ImmediateNotice = "立即改种会清空本田耕作安排、解除共享表引用；\n丢弃当前未收获作物，田块水分保留。";
    private const string PrepareNotice = "预备下一轮会清空本田耕作安排、解除共享表引用；\n保留当前作物，本轮结束后接续手动作物。";
    private readonly Button[] _options = new Button[FarmGame.Crops.Count];
    private readonly Label _manualNotice;
    private bool _prepareNext;
    private Vector2I? _farmCell;

    public event Action<CropKind>? CropRequested;
    public event Action<CropKind>? NextCropRequested;

    public CropSelectionWindow() : base("CropWindow", "选择作物", new Vector2(455, 108),
        new Vector2(790, 535))
    {
        _manualNotice = MakeLabel(ImmediateNotice, 10, Muted);
        _manualNotice.Name = "ManualCultivationNotice";
        _manualNotice.AutowrapMode = TextServer.AutowrapMode.Off;
        Body.AddChild(_manualNotice);
        var scroll = new ScrollContainer
        {
            Name = "CropScroll",
            CustomMinimumSize = new Vector2(0, 260),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        Body.AddChild(scroll);
        var cards = new GridContainer { Name = "CropCards", Columns = 4, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        cards.AddThemeConstantOverride("h_separation", 10);
        cards.AddThemeConstantOverride("v_separation", 10);
        scroll.AddChild(cards);
        scroll.Resized += () => RefreshColumns(scroll, cards);
        cards.MinimumSizeChanged += () => RefreshColumns(scroll, cards);
        foreach (CropDefinition crop in FarmGame.Crops)
        {
            CropKind kind = crop.Kind;
            Button option = MakeSecondaryButton("", 160, 238);
            option.AddThemeFontSizeOverride("font_size", 10);
            option.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            option.Icon = FacilityPreview.CropTexture(kind);
            option.ExpandIcon = true;
            option.AddThemeConstantOverride("icon_max_width", 100);
            option.IconAlignment = HorizontalAlignment.Center;
            option.VerticalIconAlignment = VerticalAlignment.Top;
            option.Name = $"CropCard{kind}";
            option.Pressed += () =>
            {
                if (_prepareNext) NextCropRequested?.Invoke(kind);
                else CropRequested?.Invoke(kind);
            };
            cards.AddChild(option);
            _options[(int)kind] = option;
        }
    }

    /**
     * <summary>在选作物前明确手动接管方式和本田损失。</summary>
     * <param name="prepareNext">是否保留当前轮并预备下一轮。</param>
     * <param name="farmCell">用于统一播种提示的农田子格。</param>
     */
    public void SetManualMode(bool prepareNext, Vector2I? farmCell = null)
    {
        _prepareNext = prepareNext;
        _farmCell = farmCell;
        _manualNotice.Text = prepareNext ? PrepareNotice : ImmediateNotice;
    }

    public void Refresh(FarmGame game)
    {
        foreach (CropDefinition crop in FarmGame.Crops)
        {
            string planting = _farmCell is Vector2I cell ? game.GetPlantingCheck(cell, crop.Kind) switch
            {
                PlantingFailure.WrongSeason => "当前禁生季节，选种后等待适季",
                PlantingFailure.InsufficientTime => "当前可播种，预计越过禁生边界，有枯萎风险",
                _ => "当前季节可播种",
            } : "";
            _options[(int)crop.Kind].Text =
                $"{crop.CropName} · 获水后 {crop.GrowthDays} 天\n每轮 {crop.HarvestQuantity} 份\n" +
                $"适宜：{FormatSeasons(crop.GrowingSeasons)}\n" +
                $"原料报价 {FormatCoins(game.GetRawPriceCents(crop.Kind))} 金币\n" +
                $"原料库存 {game.GetRawStock(crop.Kind)}\n{ShortPlantingNotice(planting)}";
            _options[(int)crop.Kind].TooltipText = planting;
        }
    }

    private static string ShortPlantingNotice(string notice) => notice switch
    {
        "当前禁生季节，选种后等待适季" => "当前禁生季节\n选种后等待适季",
        "当前可播种，预计越过禁生边界，有枯萎风险" => "本季可播种\n跨季有枯萎风险",
        _ => notice,
    };

    private static void RefreshColumns(ScrollContainer scroll, GridContainer cards)
    {
        if (scroll.Size.X <= 0) return;
        float cardWidth = 0;
        foreach (Node child in cards.GetChildren())
            if (child is Button card)
                cardWidth = Math.Max(cardWidth, card.GetCombinedMinimumSize().X);
        if (cardWidth <= 0) return;
        int gap = cards.GetThemeConstant("h_separation");
        float available = scroll.Size.X - scroll.GetVScrollBar().GetCombinedMinimumSize().X;
        int columns = Math.Clamp((int)((available + gap) / (cardWidth + gap)), 1, 4);
        if (cards.Columns != columns) cards.Columns = columns;
    }

    private static string FormatSeasons(GrowingSeasons seasons)
    {
        string result = "";
        foreach (var (flag, name) in new[]
        {
            (GrowingSeasons.Spring, "春"), (GrowingSeasons.Summer, "夏"),
            (GrowingSeasons.Autumn, "秋"), (GrowingSeasons.Winter, "冬"),
        })
        {
            if ((seasons & flag) != 0)
                result += result.Length == 0 ? name : $" / {name}";
        }
        return result;
    }
}
