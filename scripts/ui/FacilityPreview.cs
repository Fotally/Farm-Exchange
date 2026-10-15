using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
    private readonly TextureRect? _art;
    private readonly FacilityThumbnail? _worldArt;
    private readonly Label _caption;

    /**
     * <summary>创建指定设施的详情缩略图与真实占地标签。</summary>
     * <param name="building">详情设施类型；农田保持原 SVG 排版，加工和道路使用正式素材及独立图片区。</param>
     */
    public FacilityPreview(BuildingKind building = BuildingKind.Farm)
    {
        CustomMinimumSize = new Vector2(0, 107);
        MouseFilter = MouseFilterEnum.Ignore;
        var background = Style(new Color("e6ddad"), 0);
        background.BorderColor = new Color("c1b182");
        background.SetBorderWidthAll(2);
        AddThemeStyleboxOverride("panel", background);
        _caption = MakeLabel("", 13, Muted);
        _caption.Name = "FacilityPreviewPlacement";
        _caption.AutowrapMode = TextServer.AutowrapMode.Off;
        _caption.MouseFilter = MouseFilterEnum.Ignore;
        if (building == BuildingKind.Farm)
        {
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
            content.AddChild(_caption);
            _caption.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
            _caption.OffsetLeft = 12;
            _caption.OffsetRight = -12;
            _caption.OffsetTop = -28;
            _caption.OffsetBottom = -7;
        }
        else
        {
            var stack = new VBoxContainer();
            stack.AddThemeConstantOverride("separation", 4);
            WrapMargin(this, 12, 7).AddChild(stack);
            _worldArt = new FacilityThumbnail
            {
                CustomMinimumSize = new Vector2(0, 64),
                SizeFlagsVertical = SizeFlags.ExpandFill,
            };
            stack.AddChild(_worldArt);
            stack.AddChild(_caption);
        }
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
        if (_worldArt != null)
            _worldArt.Texture = space.Building == BuildingKind.Road ?
                Texture(space.Building, crop, stage, space.RoadMaterial!.Value) : Texture(space.Building, crop, stage);
        else
            _art!.Texture = Texture(space.Building, crop, stage);
        string footprint = space.Building == BuildingKind.Road ? "1 × 1" : "3 × 3";
        _caption.Text = $"{footprint} 基础格 · ({space.AnchorCell.X}, {space.AnchorCell.Y})";
    }

    internal static Texture2D CropTexture(CropKind crop) => LoadTexture($"crop_{crop.ToString().ToLowerInvariant()}");

    internal static Texture2D Texture(BuildingKind building, CropKind crop,
        CropStage stage = CropStage.Growing, RoadMaterial roadMaterial = RoadMaterial.Dirt)
    {
        if (building == BuildingKind.Processor)
        {
            string cropName = crop.ToString().ToLowerInvariant();
            string key = $"processor_{cropName}";
            if (!Textures.TryGetValue(key, out Texture2D? texture))
            {
                var source = GD.Load<Texture2D>($"res://assets/gameplay/buildings/{cropName}_workshop_q0.png");
                using Image image = source.GetImage();
                texture = MakeThumbnail(image, source.ResourcePath);
                Textures.Add(key, texture);
            }
            return texture;
        }
        if (building == BuildingKind.Road) return RoadTexture(roadMaterial);
        return LoadTexture(stage switch
        {
            CropStage.None => "field_empty",
            CropStage.Seeded => "field_seeded",
            _ => $"field_{crop.ToString().ToLowerInvariant()}",
        });
    }

    private static Texture2D RoadTexture(RoadMaterial material)
    {
        string name = material.ToString().ToLowerInvariant();
        string key = $"road_{name}";
        if (Textures.TryGetValue(key, out Texture2D? texture)) return texture;
        var atlas = GD.Load<Texture2D>($"res://assets/gameplay/terrain/roads/road_{name}_atlas.png");
        using JsonDocument regions = JsonDocument.Parse(FileAccess.GetFileAsString(
            "res://assets/gameplay/terrain/roads/atlas_regions.json"));
        using Image source = atlas.GetImage();
        using var road = Image.CreateEmpty(128, 64, false, Image.Format.Rgba8);
        int[] masks = { 2, 10, 8 };
        for (int index = 0; index < masks.Length; index++)
        {
            JsonElement region = regions.RootElement.EnumerateArray().First(entry =>
                entry.GetProperty("material").GetString() == name &&
                entry.GetProperty("mask").GetInt32() == masks[index] &&
                entry.GetProperty("variant").GetInt32() == 0).GetProperty("region");
            road.BlendRect(source, new Rect2I(region[0].GetInt32(), region[1].GetInt32(),
                region[2].GetInt32(), region[3].GetInt32()), new Vector2I(index * 32, index * 16));
        }
        texture = MakeThumbnail(road, atlas.ResourcePath);
        Textures.Add(key, texture);
        return texture;
    }

    internal static Texture2D RoadRemovalTexture()
    {
        const string key = "road_removal";
        if (Textures.TryGetValue(key, out Texture2D? texture)) return texture;
        using Image source = RoadTexture(RoadMaterial.Dirt).GetImage();
        using Image image = source.GetRegion(new Rect2I(0, 0, source.GetWidth(), source.GetHeight()));
        image.FillRect(new Rect2I(113, 72, 32, 32), Cream);
        image.FillRect(new Rect2I(115, 74, 28, 28), new Color("9c5542"));
        using Image iconSource = UiIcons.Texture(UiIcon.Build).GetImage();
        using Image icon = iconSource.GetRegion(new Rect2I(0, 0, iconSource.GetWidth(), iconSource.GetHeight()));
        icon.Resize(24, 24, Image.Interpolation.Lanczos);
        image.BlendRect(icon, new Rect2I(0, 0, 24, 24), new Vector2I(117, 76));
        texture = ImageTexture.CreateFromImage(image);
        texture.ResourceName = "拆除道路 · 土路 + 建造工具角标";
        Textures.Add(key, texture);
        return texture;
    }

    private static Texture2D MakeThumbnail(Image source, string sourcePath)
    {
        using Image art = source.GetRegion(source.GetUsedRect());
        float factor = Math.Min(145f / art.GetWidth(), 104f / art.GetHeight());
        int width = (int)Math.Round(art.GetWidth() * factor);
        int height = (int)Math.Round(art.GetHeight() * factor);
        art.Resize(width, height, Image.Interpolation.Nearest);
        using var canvas = Image.CreateEmpty(145, 108, false, Image.Format.Rgba8);
        canvas.BlitRect(art, new Rect2I(0, 0, width, height), new Vector2I((145 - width) / 2, 104 - height));
        var texture = ImageTexture.CreateFromImage(canvas);
        texture.ResourceName = sourcePath;
        return texture;
    }

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
