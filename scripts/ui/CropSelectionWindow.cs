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

    public CropSelectionWindow() : base("CropWindow", "选择作物", new Vector2(455, 140),
        new Vector2(430, 390), avoidBottomBar: true)
    {
        _manualNotice = MakeLabel(ImmediateNotice, 12, Ink);
        _manualNotice.Name = "ManualCultivationNotice";
        _manualNotice.AutowrapMode = TextServer.AutowrapMode.Off;
        Body.AddChild(_manualNotice);
        var scroll = new ScrollContainer
        {
            Name = "CropScroll",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        Body.AddChild(scroll);
        var cards = new VBoxContainer { Name = "CropCards", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        cards.AddThemeConstantOverride("separation", 7);
        scroll.AddChild(cards);
        foreach (CropDefinition crop in FarmGame.Crops)
        {
            CropKind kind = crop.Kind;
            Button option = MakeButton("", Mid, 390, 57);
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
                $"{crop.CropName} · 原料当前报价：{FormatCoins(game.GetRawPriceCents(crop.Kind))} 金币\n" +
                $"原料库存 {game.GetRawStock(crop.Kind)}\n{planting}";
            _options[(int)crop.Kind].TooltipText = planting;
        }
    }
}
