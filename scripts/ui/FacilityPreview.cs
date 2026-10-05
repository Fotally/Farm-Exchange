using System.Collections.Generic;
using Godot;
using FarmExchange.Gameplay;
using FarmExchange.Land;
using static FarmExchange.UI.UiElements;

namespace FarmExchange.UI;

/**
 * <summary>按只读设施快照显示 UI 缩略图和真实锚点，不维护生产状态。</summary>
 */
public partial class FacilityPreview : PanelContainer
{
    private static readonly Dictionary<string, Texture2D> Textures = new();
    private readonly TextureRect _art;
    private readonly Label _caption;

    public FacilityPreview()
    {
        CustomMinimumSize = new Vector2(0, 107);
        MouseFilter = MouseFilterEnum.Ignore;
        var background = Style(new Color("e6ddad"), 0);
        background.BorderColor = new Color("c1b182");
        background.SetBorderWidthAll(2);
        AddThemeStyleboxOverride("panel", background);
        var content = new Control { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(content);
        _art = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        content.AddChild(_art);
        _art.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _caption = MakeLabel("", 13, Muted);
        _caption.Name = "FacilityPreviewPlacement";
        _caption.AutowrapMode = TextServer.AutowrapMode.Off;
        _caption.MouseFilter = MouseFilterEnum.Ignore;
        content.AddChild(_caption);
        _caption.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        _caption.OffsetLeft = 12;
        _caption.OffsetRight = -12;
        _caption.OffsetTop = -28;
        _caption.OffsetBottom = -7;
        _caption.AddThemeStyleboxOverride("normal", Style(Cream, 0));
    }

    /**
     * <summary>更新当前设施示意和实际占地锚点。</summary>
     * <param name="space">当前选中实例的只读空间快照。</param>
     * <param name="crop">选中实例的作物种类；道路不使用此值。</param>
     * <param name="stage">农田本轮阶段；未播种显示空田，待水显示苗点。</param>
     */
    public void Refresh(BuildingSpaceSnapshot space, CropKind crop, CropStage stage)
    {
        _art.Texture = Texture(space.Building, crop, stage);
        string footprint = space.Building == BuildingKind.Road ? "1 × 1" : "3 × 3";
        _caption.Text = $"{footprint} 基础格 · ({space.AnchorCell.X}, {space.AnchorCell.Y})";
    }

    internal static Texture2D CropTexture(CropKind crop) => LoadTexture($"crop_{crop.ToString().ToLowerInvariant()}");

    internal static Texture2D Texture(BuildingKind building, CropKind crop, CropStage stage = CropStage.Growing) =>
        LoadTexture(building switch
        {
            BuildingKind.Road => "road",
            BuildingKind.Processor => crop == CropKind.Wheat ? "mill" : "workshop",
            _ => stage switch
            {
                CropStage.None => "field_empty",
                CropStage.Seeded => "field_seeded",
                _ => $"field_{crop.ToString().ToLowerInvariant()}",
            },
        });

    private static Texture2D LoadTexture(string name)
    {
        if (!Textures.TryGetValue(name, out Texture2D? texture))
        {
            texture = GD.Load<Texture2D>($"res://assets/ui/facilities/{name}.svg");
            Textures.Add(name, texture);
        }
        return texture;
    }
}
